using System.Threading;
using OpenNest.CNC;
using OpenNest.CommonLines;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.CommonLines;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.Jobs.Remote;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Automatic common-line placement (docs/common-line-cutting.md): the pipeline's pair trial at
/// each usage level, the explicit-group validation, materialization and server replay.
/// </summary>
public class CommonLineAutoPlacementTests
{
    private const double Kerf = 0.012;

    /// <summary>A 6 x 12.44 plate with 0.38 chamfers on all four corners, like N26-295's PT42.</summary>
    private static Drawing Chamfered(string name = "pt", bool allow = true)
    {
        var program = new Program(Mode.Absolute);
        var points = new (double X, double Y)[]
        {
            (0.38, 0), (5.62, 0), (6, 0.38), (6, 12.06), (5.62, 12.44), (0.38, 12.44), (0, 12.06), (0, 0.38),
        };
        program.Codes.Add(new RapidMove(points[0].X, points[0].Y));
        foreach (var point in points.Skip(1).Append(points[0]))
            program.Codes.Add(new LinearMove(point.X, point.Y));
        return new Drawing(name, program) { AllowCommonLine = allow };
    }

    /// <summary>
    /// One row: the 55.1 x 12.44 work area holds 8 parts at 0.25 spacing (9 would need 56), but
    /// 4 kerf-gap pairs and a single need only 55.05.
    /// </summary>
    private static NestPipelineRequest Row(Drawing drawing, CommonLineUsage usage, double kerf = Kerf,
        int quantity = 9, double length = 55.6, string engine = "Rectangles") =>
        new(engine, [new NestItem { Drawing = drawing, Quantity = quantity }],
            [new NestPlateStock("sheet", new Size(12.94, length), 1, 0.25, new Spacing(0.25, 0.25, 0.25, 0.25), 1)],
            new NestJobOptions { CommonLineUsage = usage, CommonLineKerf = kerf });

    private static int Placed(NestPipelineResult result) => result.Raw.Plates.Sum(p => p.Placements.Count);

    private static IEnumerable<(string, double, double, double)> Poses(NestPipelineResult result) =>
        result.Raw.Plates.SelectMany(p => p.Placements).Select(p => (p.PartId, p.X, p.Y, p.Rotation));

    private static int Solves(NestPipelineRequest request)
    {
        var recorder = new RecordingNestingEngine(NestingEngineRegistry.Create(request.EngineName));
        NestPipeline.Run(recorder, request.EngineName, request);
        return recorder.Transcript.Solves.Count;
    }

    [Theory]
    [InlineData("Rectangles")]
    [InlineData("Default")]
    public void Off_IsTheLayoutWithoutCommonLineOptions_AndAsksNoMoreSolves(string engine)
    {
        var drawing = Chamfered();
        var plain = Row(drawing, CommonLineUsage.Off, kerf: 0, engine: engine) with { Options = new NestJobOptions() };
        var off = Row(drawing, CommonLineUsage.Off, engine: engine);

        Assert.Equal(Poses(NestPipeline.Run(plain)), Poses(NestPipeline.Run(off)));
        Assert.Equal(Solves(plain), Solves(off));
    }

    [Fact]
    public void LevelOn_WithoutAnEligibleDrawingOrKerf_IsTheOrdinarySolve()
    {
        var plain = NestPipeline.Run(Row(Chamfered(), CommonLineUsage.Off));
        foreach (var request in new[]
        {
            Row(Chamfered(allow: false), CommonLineUsage.Whenever),
            Row(Chamfered(), CommonLineUsage.Whenever, kerf: 0),
        })
        {
            Assert.Equal(1, Solves(request));
            Assert.Equal(Poses(plain), Poses(NestPipeline.Run(request)));
        }
    }

    [Theory]
    [InlineData(CommonLineUsage.WhenItAddsParts, "Rectangles")]
    [InlineData(CommonLineUsage.Whenever, "Rectangles")]
    [InlineData(CommonLineUsage.WhenItAddsParts, "Default")]
    public void PairsFitAPartTheSpacingCannot(CommonLineUsage usage, string engine)
    {
        var drawing = Chamfered();
        var off = NestPipeline.Run(Row(drawing, CommonLineUsage.Off, engine: engine));
        var on = NestPipeline.Run(Row(drawing, usage, engine: engine));

        Assert.Equal(8, Placed(off));
        Assert.Equal(9, Placed(on));
        Assert.True(on.IsValid, string.Join("; ", on.Violations));
        var plate = Assert.Single(on.Plates);
        Assert.NotEmpty(plate.CommonLines);
        Assert.All(plate.CommonLines, group =>
        {
            Assert.Equal(Kerf, group.Kerf);
            Assert.True(CommonLineGeometry.Check(group).IsIntact);
            Assert.All(group.Members, member => Assert.Contains(member, plate.Parts));
        });
    }

    [Fact]
    public void WhenItAddsParts_KeepsTheLayoutWithoutCommonLinesOnATie()
    {
        // A long sheet holds all nine either way.
        var drawing = Chamfered();
        var off = NestPipeline.Run(Row(drawing, CommonLineUsage.Off, length: 120));
        var on = NestPipeline.Run(Row(drawing, CommonLineUsage.WhenItAddsParts, length: 120));

        Assert.Equal(9, Placed(off));
        Assert.Equal(Poses(off), Poses(on));
        Assert.All(on.Plates, plate => Assert.Empty(plate.CommonLines));
    }

    [Theory]
    [InlineData(55.6)]
    [InlineData(120)]
    [InlineData(40)]
    public void Whenever_NeverLosesAPart(double length)
    {
        var drawing = Chamfered();
        var off = NestPipeline.Run(Row(drawing, CommonLineUsage.Off, length: length));
        var on = NestPipeline.Run(Row(drawing, CommonLineUsage.Whenever, length: length));

        Assert.True(on.IsValid, string.Join("; ", on.Violations));
        Assert.True(Placed(on) >= Placed(off));
    }

    [Fact]
    public void Ranking_CountFirst_TiesByLevel()
    {
        var drawing = Chamfered();
        var eight = NestPipeline.Run(Row(drawing, CommonLineUsage.Off));
        var nine = NestPipeline.Run(Row(drawing, CommonLineUsage.Whenever));

        foreach (var usage in new[] { CommonLineUsage.WhenItAddsParts, CommonLineUsage.Whenever })
        {
            Assert.True(CommonLineRanking.Prefers(usage, nine, eight));
            Assert.False(CommonLineRanking.Prefers(usage, eight, nine));
        }
        Assert.False(CommonLineRanking.Prefers(CommonLineUsage.Off, nine, eight));
        // Equal layouts: never a reason to switch.
        Assert.False(CommonLineRanking.Prefers(CommonLineUsage.WhenItAddsParts, nine, nine));
        Assert.False(CommonLineRanking.Prefers(CommonLineUsage.Whenever, nine, nine));
        // Shared spans are cut once.
        Assert.True(CommonLineRanking.CutLength(nine) < 9 * CutLengthOfOne(drawing));
    }

    private static double CutLengthOfOne(Drawing drawing) =>
        Converters.ConvertProgram.ToGeometry(drawing.Program).Sum(entity => entity.Length);

    [Fact]
    public void Pair_SharesTheLongestEdgeOneKerfApart()
    {
        var drawing = Chamfered();
        var pair = CommonLinePair.Build(PartGeometrySnapshot.FromProgram(drawing.Program), Kerf);

        Assert.NotNull(pair);
        Assert.Equal(12.44 - 2 * 0.38, pair!.SharedLength, 6);
        var first = new Part(drawing);
        var second = new Part(drawing);
        second.Rotate(CommonLinePair.SecondRotation);
        second.Location = new Vector(pair.SecondX, pair.SecondY);
        var span = Assert.Single(CommonLineGeometry.Spans(first, second, Kerf));
        Assert.Equal(pair.SharedLength, span.Length, 6);
        // The compound covers both copies: its box is theirs.
        var proxy = new Part(new Drawing("pair", OpenNest.Engine.Jobs.Adapters.DrawingJobMapper.ToProgram(pair.Geometry)));
        Assert.Equal(12 + Kerf, proxy.BoundingBox.Length, 4);
        Assert.Equal(12.44, proxy.BoundingBox.Width, 4);
    }

    [Fact]
    public void Pair_NoneWithoutALongStraightEdge()
    {
        var circle = new Program(Mode.Absolute);
        circle.Codes.Add(new RapidMove(5, 0));
        circle.Codes.Add(new ArcMove(5, 0, 0, 0, RotationType.CCW));
        Assert.Null(CommonLinePair.Build(PartGeometrySnapshot.FromProgram(circle), Kerf));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Rectangles")]
    [InlineData("Lattice")]
    public void Wedges_PairIntoUsableGeometry_AndStayValid(string engine)
    {
        // A long thin wedge: the pair's union once kept a near-repeated vertex that engines refuse.
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(28, 0));
        program.Codes.Add(new LinearMove(0, 10));
        program.Codes.Add(new LinearMove(0, 0));
        var wedge = new Drawing("wedge", program) { AllowCommonLine = true };
        var pair = CommonLinePair.Build(PartGeometrySnapshot.FromProgram(program), Kerf);
        Assert.NotNull(pair);
        Assert.NotNull(JobPartGeometry.Read(pair!.Geometry));

        foreach (var usage in new[] { CommonLineUsage.WhenItAddsParts, CommonLineUsage.Whenever })
        {
            var result = NestPipeline.Run(new NestPipelineRequest(engine,
                [new NestItem { Drawing = wedge, Quantity = 2 }],
                [new NestPlateStock("sheet", new Size(15, 30), null, 0.25, new Spacing(0.25, 0.25, 0.25, 0.25), 1)],
                new NestJobOptions { CommonLineUsage = usage, CommonLineKerf = Kerf }));
            Assert.True(result.IsValid, string.Join("; ", result.Violations));
            Assert.Equal(2, Placed(result));
        }
    }

    // Two copies placed as a pair, without asking any engine.
    private static (NestJob Job, NestJobResult Result) PairResult(bool group, bool allow = true,
        CommonLineUsage usage = CommonLineUsage.Whenever, double gap = Kerf)
    {
        var drawing = Chamfered(allow: allow);
        var part = OpenNest.Engine.Jobs.Adapters.DrawingJobMapper.FromDrawing("pt", drawing, 2);
        var stock = new NestPlateStock("sheet", new Size(48, 96), 1, 0.25, new Spacing(0.25, 0.25, 0.25, 0.25), 1);
        var job = new NestJob([part], [stock], new NestJobOptions { CommonLineUsage = usage, CommonLineKerf = Kerf });
        var pair = CommonLinePair.Build(part.Geometry, gap)!;
        var placements = new[]
        {
            new NestJobPlacement("pt", 0, 1, 1, 0),
            new NestJobPlacement("pt", 1, 1 + pair.SecondX, 1 + pair.SecondY, System.Math.PI),
        };
        var sheet = new NestJobPlateResult(0, stock, placements,
            group ? [new NestJobCommonLine(Kerf, [0, 1])] : null);
        return (job, new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed, [sheet],
            [new PartFulfillment("pt", 2, 2, 0)], [new StockUsage("sheet", 1, 0)]));
    }

    [Fact]
    public void LayoutCheck_ExemptsOnlyAnExplicitGroup()
    {
        var (job, ungrouped) = PairResult(group: false);
        Assert.Contains(NestLayoutCheck.Violations(job, ungrouped), v => v.Contains("closer than the required spacing"));

        var (_, grouped) = PairResult(group: true);
        Assert.Empty(NestLayoutCheck.Violations(job, grouped));
    }

    [Fact]
    public void LayoutCheck_RefusesGroupsTheJobDoesNotAllow()
    {
        var (offJob, off) = PairResult(group: true, usage: CommonLineUsage.Off);
        Assert.Contains(NestLayoutCheck.Violations(offJob, off), v => v.Contains("does not allow"));

        var (notAllowedJob, notAllowed) = PairResult(group: true, allow: false);
        Assert.Contains(NestLayoutCheck.Violations(notAllowedJob, notAllowed), v => v.Contains("does not allow common lines"));
    }

    [Fact]
    public void LayoutCheck_RefusesAGroupThatSharesNoEdge()
    {
        // Built at twice the kerf: the members do not share at the group's kerf.
        var (job, wide) = PairResult(group: true, gap: 2 * Kerf);
        Assert.Contains(NestLayoutCheck.Violations(job, wide), v => v.Contains("no longer shares"));
    }

    [Fact]
    public void Materializing_CreatesThePlatesCommonLines()
    {
        var (job, result) = PairResult(group: true);
        var plate = Assert.Single(OpenNest.Engine.Jobs.Adapters.NestResultMaterializer.Materialize(job, result).Nest.Plates);
        var group = Assert.Single(plate.CommonLines);
        Assert.Equal(plate.Parts, group.Members);
    }

    [Fact]
    public void Commit_AddsTheGroupsToThePlate()
    {
        var result = NestPipeline.Run(Row(Chamfered(), CommonLineUsage.WhenItAddsParts));
        var nest = new Nest();
        var plate = Assert.Single(NestPipelineCommit.ApplyToPlates(result, () =>
        {
            var created = new Plate(new Size(12.94, 55.6));
            nest.Plates.Add(created);
            return created;
        }));
        Assert.Equal(result.Plates[0].CommonLines.Count, plate.CommonLines.Count);
        Assert.All(plate.CommonLines, group => Assert.All(group.Members, m => Assert.Contains(m, plate.Parts)));
    }

    [Fact]
    public void ServerReplay_BindsTheSameGroups()
    {
        var drawing = Chamfered();
        var request = Row(drawing, CommonLineUsage.WhenItAddsParts);
        var recorder = new RecordingNestingEngine(NestingEngineRegistry.Create(request.EngineName));
        NestPipeline.Run(recorder, request.EngineName, request);
        var transcript = NestSolveTranscript.FromJson(recorder.Transcript.ToJson());

        var local = NestPipeline.Run(request);
        var replayed = new RemoteNestSubmission(request).Complete(transcript);

        Assert.True(replayed.IsValid, string.Join("; ", replayed.Violations));
        Assert.Equal(Poses(local), Poses(replayed));
        Assert.Equal(
            local.Plates.Select(p => p.CommonLines.Select(g => g.Members.Select(m => p.Parts.ToList().IndexOf(m)).ToArray()).ToArray()),
            replayed.Plates.Select(p => p.CommonLines.Select(g => g.Members.Select(m => p.Parts.ToList().IndexOf(m)).ToArray()).ToArray()));
        Assert.All(replayed.Plates.SelectMany(p => p.Parts), part => Assert.Same(drawing, part.BaseDrawing));
    }

    [Fact]
    public void JobDocument_CarriesTheLevelAndKerf()
    {
        var drawing = Chamfered();
        var on = NestJobDocument.From(Row(drawing, CommonLineUsage.Whenever), [drawing]);
        Assert.Equal(NestJobDocument.CommonLineVersion, on.Version);
        var options = NestJobDocument.FromJson(on.ToJson()).ToRequest([drawing]).Options!;
        Assert.Equal(CommonLineUsage.Whenever, options.CommonLineUsage);
        Assert.Equal(Kerf, options.CommonLineKerf);

        var off = NestJobDocument.From(Row(drawing, CommonLineUsage.Off), [drawing]);
        Assert.Equal(NestJobDocument.CurrentVersion, off.Version);
    }

    [Fact]
    public void Fill_OffIsThePlainFill()
    {
        var plate = new Plate(new Size(12.94, 55.6)) { PartSpacing = 0.25, EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25) };
        var drawing = Chamfered();
        var plain = PlateFillService.FillItem("Lattice", plate, new NestItem { Drawing = drawing }, plate.WorkArea(),
            null, CancellationToken.None);
        var off = PlateFillService.FillItem("Lattice", plate, new NestItem { Drawing = drawing }, plate.WorkArea(),
            CommonLineUsage.Off, Kerf, null, CancellationToken.None);

        Assert.Empty(off.CommonLines);
        Assert.Equal(plain.Select(p => (p.Location, p.Rotation)), off.Parts.Select(p => (p.Location, p.Rotation)));
    }

    [Theory]
    [InlineData(CommonLineUsage.WhenItAddsParts)]
    [InlineData(CommonLineUsage.Whenever)]
    public void Fill_PairsAreIntactAndNeverCostAPart(CommonLineUsage usage)
    {
        var plate = new Plate(new Size(12.94, 55.6)) { PartSpacing = 0.25, EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25) };
        var drawing = Chamfered();
        var plain = PlateFillService.FillItem("Lattice", plate, new NestItem { Drawing = drawing }, plate.WorkArea(),
            null, CancellationToken.None);
        var fill = PlateFillService.FillItem("Lattice", plate, new NestItem { Drawing = drawing }, plate.WorkArea(),
            usage, Kerf, null, CancellationToken.None);

        Assert.True(fill.Parts.Count >= plain.Count);
        if (fill.CommonLines.Count == 0)
            return;
        Assert.All(fill.CommonLines, group => Assert.True(CommonLineGeometry.Check(group).IsIntact));
        foreach (var part in fill.Parts)
            plate.Parts.Add(part);
        foreach (var group in fill.CommonLines)
            plate.CommonLines.Add(group);
        var violations = NestLayoutCheck.Validate([(plate, plate.Parts.ToList())],
            new Dictionary<Drawing, (string, int)> { [drawing] = ("pt", int.MaxValue) });
        Assert.Empty(violations);
    }
}
