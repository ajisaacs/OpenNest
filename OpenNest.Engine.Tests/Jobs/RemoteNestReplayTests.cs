using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Remote;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// A nest solved elsewhere (the nest server's worker) comes back as the engine's recorded
/// answers and is replayed here: the proposal must be the local run's, validated and bound
/// to the caller's own drawings, and a replay that does not fit these drawings must refuse.
/// </summary>
public class RemoteNestReplayTests
{
    private static NestPipelineRequest Request(Drawing a, Drawing b, string engine = "Rectangles")
    {
        var template = new Plate(48, 96) { EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25), PartSpacing = 0.25 };
        template.SetClampRail(new ClampRail(1.5, 0.625, new[] { 6.0, 30.0 }));
        return new NestPipelineRequest(engine,
            [new NestItem { Drawing = a, Quantity = 12 }, new NestItem { Drawing = b, Quantity = 7, Priority = 1 }],
            NestStockBuilder.FromTemplate(template,
                [new PlateOption { Width = 48, Length = 96, Cost = 100 }, new PlateOption { Width = 60, Length = 120, Quantity = 2, Cost = 160 }]),
            new NestJobOptions(maxPlates: 10, salvageRate: 0.5, minimumSalvageDimension: 6));
    }

    private static NestSolveTranscript Solve(NestPipelineRequest request)
    {
        var recorder = new RecordingNestingEngine(NestingEngineRegistry.Create(request.EngineName));
        NestPipeline.Run(recorder, request.EngineName, request);
        // Over the wire and back, as the server sends it.
        return NestSolveTranscript.FromJson(recorder.Transcript.ToJson());
    }

    [Theory]
    [InlineData("Rectangles")]
    [InlineData("Default")]
    public void ReplayGivesTheLocalProposalBoundToTheCallersDrawings(string engine)
    {
        var a = new Drawing("a", TestDrawingFactory.Rectangle(10, 20));
        var b = new Drawing("b", TestDrawingFactory.LShape());
        var request = Request(a, b, engine);
        var submission = new RemoteNestSubmission(request);
        var transcript = Solve(request);

        var local = NestPipeline.Run(request);
        var replayed = submission.Complete(transcript);

        Assert.True(replayed.IsValid, string.Join("; ", replayed.Violations));
        Assert.True(replayed.CanKeep);
        Assert.Equal(local.Plates.Count, replayed.Plates.Count);
        for (var i = 0; i < local.Plates.Count; i++)
        {
            Assert.Equal(local.Plates[i].Quantity, replayed.Plates[i].Quantity);
            Assert.Equal(local.Plates[i].Stock.Id, replayed.Plates[i].Stock.Id);
            Assert.Same(request.Stock.Single(s => s.Id == replayed.Plates[i].Stock.Id), replayed.Plates[i].Stock);
            Assert.Equal(
                local.Plates[i].Parts.Select(p => (p.BaseDrawing.Name, p.Location.X, p.Location.Y, p.Rotation)),
                replayed.Plates[i].Parts.Select(p => (p.BaseDrawing.Name, p.Location.X, p.Location.Y, p.Rotation)));
            Assert.All(replayed.Plates[i].Parts, p => Assert.True(ReferenceEquals(p.BaseDrawing, a) || ReferenceEquals(p.BaseDrawing, b)));
        }
        Assert.Equal(local.Unplaced.Count, replayed.Unplaced.Count);
    }

    [Fact]
    public void CutoutTrialAndOrdinarySolveReplayInOrder()
    {
        // A 20 x 20 frame with a 12 x 14 window, a strap that fits the window, and a square
        // that fits nowhere on the one sheet: the bundled trial is incomplete, so the pipeline
        // also runs the ordinary solve, and the replay must answer both in order.
        var frame = TestDrawingFactory.Rectangle(20, 20);
        frame.Codes.Add(new RapidMove(4, 3));
        frame.Codes.Add(new LinearMove(16, 3));
        frame.Codes.Add(new LinearMove(16, 17));
        frame.Codes.Add(new LinearMove(4, 17));
        frame.Codes.Add(new LinearMove(4, 3));
        var request = new NestPipelineRequest("Default",
            [
                new NestItem { Drawing = new Drawing("frame", frame), Quantity = 1 },
                new NestItem { Drawing = new Drawing("strap", TestDrawingFactory.Rectangle(13, 1.25)), Quantity = 1 },
                new NestItem { Drawing = new Drawing("square", TestDrawingFactory.Rectangle(15, 15)), Quantity = 1 },
            ],
            new[] { new NestPlateStock("sheet", new Size(20.5, 20.5), 1, 0.25) });
        var submission = new RemoteNestSubmission(request);
        var transcript = Solve(request);
        Assert.True(transcript.Solves.Count >= 2, $"Expected the trial and the ordinary solve, got {transcript.Solves.Count}.");

        var local = NestPipeline.Run(request);
        var replayed = submission.Complete(transcript);

        Assert.Equal(local.IsValid, replayed.IsValid);
        Assert.Equal(local.Plates.SelectMany(p => p.Parts).Select(p => (p.BaseDrawing.Name, p.Location.X, p.Location.Y)),
            replayed.Plates.SelectMany(p => p.Parts).Select(p => (p.BaseDrawing.Name, p.Location.X, p.Location.Y)));
        Assert.Equal(local.Unplaced.Select(u => u.ToString()), replayed.Unplaced.Select(u => u.ToString()));
    }

    [Fact]
    public void ADrawingEditedAfterSendingRefusesTheResult()
    {
        var a = new Drawing("a", TestDrawingFactory.Rectangle(10, 20));
        var b = new Drawing("b", TestDrawingFactory.LShape());
        var request = Request(a, b);
        var submission = new RemoteNestSubmission(request);
        var transcript = Solve(request);

        a.Program = TestDrawingFactory.Rectangle(11, 20);

        var refused = Assert.Throws<NestReplayMismatchException>(() => submission.Complete(transcript));
        Assert.Contains("'a'", refused.Message);
    }

    [Fact]
    public void AnswersForADifferentJobDoNotReplay()
    {
        var a = new Drawing("a", TestDrawingFactory.Rectangle(10, 20));
        var b = new Drawing("b", TestDrawingFactory.LShape());
        var transcript = Solve(Request(a, b));
        var other = Request(a, b);
        other.Items[0].Quantity = 13;

        Assert.Throws<NestReplayMismatchException>(() => new RemoteNestSubmission(other).Complete(transcript));
    }

    [Fact]
    public void StockSurvivesTheWireBitForBit()
    {
        var outline = new SheetOutline(
            new[] { new Vector(0, 0), new Vector(96, 0), new Vector(96, 48), new Vector(0, 48) },
            new[] { new[] { new Vector(10, 10), new Vector(12, 10), new Vector(12, 12.125), new Vector(10, 12.125) } });
        var template = new Plate(48, 96) { Quadrant = 3, EdgeSpacing = new Spacing(0.1, 0.2, 0.3, 0.4), PartSpacing = 0.1 + 0.2 };
        template.SetClampRail(new ClampRail(1.5, 0.625, new[] { new RailClamp(ClampEdge.OriginSide, 6), new RailClamp(ClampEdge.FarSide, 40.1) }, 7, 0.3));
        var stock = NestStockBuilder.FromTemplate(template, [new PlateOption { Width = 48, Length = 96, Cost = 1.1, Quantity = 3, Id = "ml-7" }])
            .Append(new NestPlateStock("odd", new Size(48, 96), null, 0.25, new Spacing(0, 0, 0, 0), 1, null, null, outline))
            .ToList();

        foreach (var sheet in stock)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(NestStockJson.From(sheet));
            var back = System.Text.Json.JsonSerializer.Deserialize<NestStockJson>(json)!.ToStock();
            Assert.True(RepeatedPlateGrouping.SameStock(sheet, back));
            Assert.Equal(sheet.ClampRail, back.ClampRail);
            Assert.Equal(sheet.SheetClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)),
                back.SheetClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)));
        }
    }

    [Fact]
    public void OnlyShippedEnginesAreBuiltIn()
    {
        Assert.True(NestingEngineRegistry.IsBuiltIn("Default"));
        Assert.True(NestingEngineRegistry.IsBuiltIn("rectangles"));
        Assert.True(NestingEngineRegistry.IsBuiltIn("Opus55NestingEngine"));
        Assert.False(NestingEngineRegistry.IsBuiltIn("SomePlugIn"));
        Assert.False(NestingEngineRegistry.IsBuiltIn(null));
    }

    [Fact]
    public void NonfinitePosesSurviveTheWire()
    {
        var transcript = new NestSolveTranscript();
        var stock = new NestPlateStock("s", new Size(10, 10));
        var job = new NestJob([], [stock]);
        transcript.Add(job, new NestJobResult(NestJobStatus.Incomplete, NestJobStopReason.NoPlacementFound,
            [new NestJobPlateResult(0, stock, [new NestJobPlacement("p", 0, double.NaN, double.PositiveInfinity, 0)])], [], []));

        var back = NestSolveTranscript.FromJson(transcript.ToJson()).Solves.Single().Result.ToResult(job);

        var pose = back.Plates.Single().Placements.Single();
        Assert.True(double.IsNaN(pose.X));
        Assert.True(double.IsPositiveInfinity(pose.Y));
        Assert.Same(stock, back.Plates.Single().Stock);
    }

    [Fact]
    public void ADrawingsFingerprintFollowsItsExactGeometryAndTravelsWithTheJob()
    {
        var a = new Drawing("a", TestDrawingFactory.Rectangle(10, 20));
        var same = new Drawing("other name", TestDrawingFactory.Rectangle(10, 20));
        var moved = new Drawing("a", TestDrawingFactory.Rectangle(10, 20.000000001));
        Assert.Equal(64, RemoteNestSubmission.Fingerprint(a).Length);
        Assert.Equal(RemoteNestSubmission.Fingerprint(a), RemoteNestSubmission.Fingerprint(same));
        Assert.NotEqual(RemoteNestSubmission.Fingerprint(a), RemoteNestSubmission.Fingerprint(moved));

        var b = new Drawing("b", TestDrawingFactory.LShape());
        var request = Request(a, b);
        var document = NestJobDocument.FromJson(NestJobDocument.From(request, [a, b]).ToJson());
        Assert.Equal([RemoteNestSubmission.Fingerprint(a), RemoteNestSubmission.Fingerprint(b)], document.DrawingFingerprints);

        // Reopened against new copies of the same drawings, the job replays to the local proposal.
        var copyA = new Drawing("a", TestDrawingFactory.Rectangle(10, 20));
        var copyB = new Drawing("b", TestDrawingFactory.LShape());
        var reopened = RemoteNestSubmission.Reopen(document, [copyB, copyA]).Complete(Solve(request));
        var local = NestPipeline.Run(Request(copyA, copyB));
        Assert.Equal(local.Plates.SelectMany(p => p.Parts).Select(p => (p.BaseDrawing, p.Location)),
            reopened.Plates.SelectMany(p => p.Parts).Select(p => (p.BaseDrawing, p.Location)));
    }
}
