using System.Globalization;
using OpenNest.CNC;
using OpenNest.Geometry;
using Xunit;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Golden-layout fixtures: the permanent regression net for the legacy-engine removal.
/// Each test solves a fixed job through the production path
/// (<see cref="PlateNesterFactory"/> + <see cref="NestJobRunner"/>, which for the remnant
/// strategies still routes through <see cref="LegacyPlateNesterAdapter"/>) and asserts the
/// exact committed poses captured from the pre-migration code. The extraction phases must
/// keep these green byte-for-byte (modulo 1e-9 float noise).
/// </summary>
/// <remarks>
/// Captured on Linux/.NET 8 at commit 42bbde7 (post ShrinkFiller axis fix), verified
/// identical across 30 repeat runs per strategy. The Strip strategy is only pinned on the
/// rectangle-variety job: on dense mixed-shape jobs the iterative shrink path intermittently
/// proposes overlapping candidates (pre-existing scheduling nondeterminism, not a regression),
/// so its mixed-geometry layout is deliberately not pinned here.
/// </remarks>
public class GoldenLayoutTests
{
    private const double PoseTolerance = 1e-9;

    private static Program Rect(double width, double length)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(width, 0);
        program.LineTo(width, length);
        program.LineTo(0, length);
        program.LineTo(0, 0);
        return program;
    }

    private static Program LShape()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(6, 0);
        program.LineTo(6, 4);
        program.LineTo(3, 4);
        program.LineTo(3, 2);
        program.LineTo(0, 2);
        program.LineTo(0, 0);
        return program;
    }

    private static Program ArcPart()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(3, 0);
        program.ArcTo(3, 5, 3, 2.5, RotationType.CCW);
        program.LineTo(0, 5);
        program.LineTo(0, 0);
        return program;
    }

    private static NestJobPart Part(string id, Program program, int quantity) =>
        new(id, PartGeometrySnapshot.FromProgram(program), quantity);

    /// <summary>Dense mixed-shape job: rects + L-shape + arc part on one 30x50 stock.</summary>
    private static NestJob MixedJob(string strategy) =>
        new(
            new[]
            {
                Part("rect-a", Rect(6, 4), 8),
                Part("rect-b", Rect(4, 3), 6),
                Part("lshape", LShape(), 3),
                Part("arc", ArcPart(), 3),
            },
            new[] { new NestPlateStock("stock", new Size(30, 50), 5, 1, new Spacing(1, 1, 1, 1)) },
            new NestJobOptions(strategy)
        );

    /// <summary>Strip fixture: three rectangle sizes (mixed-shape Strip layouts are not deterministic today).</summary>
    private static NestJob StripJob() =>
        new(
            new[]
            {
                Part("rect-a", Rect(6, 4), 5),
                Part("rect-b", Rect(4, 3), 4),
                Part("rect-c", Rect(2, 7), 3),
            },
            new[] { new NestPlateStock("stock", new Size(30, 50), 4, 1, new Spacing(1, 1, 1, 1)) },
            new NestJobOptions("Strip")
        );

    private sealed record Pose(
        string PartId,
        int InstanceIndex,
        double X,
        double Y,
        double Rotation
    );

    private static List<Pose> Poses(NestJobResult result) =>
        result
            .Plates.SelectMany(plate => plate.Placements)
            .Select(p => new Pose(p.PartId, p.InstanceIndex, p.X, p.Y, p.Rotation))
            .OrderBy(p => p.PartId, StringComparer.Ordinal)
            .ThenBy(p => p.InstanceIndex)
            .ToList();

    private static bool AnglesEqual(double left, double right)
    {
        var delta = System.Math.Abs(left - right) % (System.Math.PI * 2);
        return System.Math.Min(delta, System.Math.PI * 2 - delta) <= PoseTolerance;
    }

    private static void AssertGolden(NestJobResult result, Pose[] expected)
    {
        var actual = Poses(result);
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].PartId, actual[i].PartId);
            Assert.Equal(expected[i].InstanceIndex, actual[i].InstanceIndex);
            Assert.True(
                System.Math.Abs(expected[i].X - actual[i].X) <= PoseTolerance
                    && System.Math.Abs(expected[i].Y - actual[i].Y) <= PoseTolerance
                    && AnglesEqual(expected[i].Rotation, actual[i].Rotation),
                $"pose {i} differs: expected {Format(expected[i])} actual {Format(actual[i])}"
            );
        }
    }

    private static string Format(Pose pose) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}#{1}@({2},{3},{4})",
            pose.PartId,
            pose.InstanceIndex,
            pose.X,
            pose.Y,
            pose.Rotation
        );

    private static Pose P(string id, int index, double x, double y, double rotation) =>
        new(id, index, x, y, rotation);

    private static NestJobResult SolveStable(INestingEngine engine, NestJob job, int repeats = 3)
    {
        var layouts = new List<string>();
        NestJobResult? first = null;
        for (var i = 0; i < repeats; i++)
        {
            var result = engine.Solve(job);
            layouts.Add(string.Join(";", Poses(result).Select(Format)));
            first ??= result;
        }
        Assert.True(
            layouts.Distinct(StringComparer.Ordinal).Count() == 1,
            "golden job must be layout-deterministic across repeat runs"
        );
        return first!;
    }

    [Fact]
    public void Default_MixedJob_GoldenLayout()
    {
        var result = SolveStable(new NestJobRunner(PlateNesterFactory.Create), MixedJob("Default"));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(20, result.Plates.Sum(p => p.Placements.Count));
        AssertGolden(
            result,
            [
                P("arc", 0, 15, 1, 0),
                P("arc", 1, 15, 7, 0),
                P("arc", 2, 15, 13, 0),
                P("lshape", 0, 15, 19, 0),
                P("lshape", 1, 15, 24, 0),
                P("lshape", 2, 21.5, 1, 0),
                P("rect-a", 0, 5, 21, 1.5707963267948966),
                P("rect-a", 1, 1, 1, 0),
                P("rect-a", 2, 1, 6, 0),
                P("rect-a", 3, 1, 11, 0),
                P("rect-a", 4, 1, 16, 0),
                P("rect-a", 5, 10, 21, 1.5707963267948966),
                P("rect-a", 6, 8, 1, 0),
                P("rect-a", 7, 8, 6, 0),
                P("rect-b", 0, 21.5, 6, 0),
                P("rect-b", 1, 21.5, 10, 0),
                P("rect-b", 2, 21.5, 14, 0),
                P("rect-b", 3, 22, 19, 0),
                P("rect-b", 4, 22, 23, 0),
                P("rect-b", 5, 26.5, 6, 0),
            ]
        );
    }

    [Fact]
    public void VerticalRemnant_MixedJob_GoldenLayout()
    {
        var result = SolveStable(
            new NestJobRunner(PlateNesterFactory.Create),
            MixedJob("Vertical Remnant")
        );
        Assert.Equal(NestJobStatus.Complete, result.Status);
        // Coincides with Default on this job; pinned independently so strategy divergence
        // after extraction is caught even where layouts agree today.
        AssertGolden(
            result,
            [
                P("arc", 0, 15, 1, 0),
                P("arc", 1, 15, 7, 0),
                P("arc", 2, 15, 13, 0),
                P("lshape", 0, 15, 19, 0),
                P("lshape", 1, 15, 24, 0),
                P("lshape", 2, 21.5, 1, 0),
                P("rect-a", 0, 5, 21, 1.5707963267948966),
                P("rect-a", 1, 1, 1, 0),
                P("rect-a", 2, 1, 6, 0),
                P("rect-a", 3, 1, 11, 0),
                P("rect-a", 4, 1, 16, 0),
                P("rect-a", 5, 10, 21, 1.5707963267948966),
                P("rect-a", 6, 8, 1, 0),
                P("rect-a", 7, 8, 6, 0),
                P("rect-b", 0, 21.5, 6, 0),
                P("rect-b", 1, 21.5, 10, 0),
                P("rect-b", 2, 21.5, 14, 0),
                P("rect-b", 3, 22, 19, 0),
                P("rect-b", 4, 22, 23, 0),
                P("rect-b", 5, 26.5, 6, 0),
            ]
        );
    }

    [Fact]
    public void HorizontalRemnant_MixedJob_GoldenLayout()
    {
        var result = SolveStable(
            new NestJobRunner(PlateNesterFactory.Create),
            MixedJob("Horizontal Remnant")
        );
        Assert.Equal(NestJobStatus.Complete, result.Status);
        AssertGolden(
            result,
            [
                P("arc", 0, 1, 23, 0),
                P("arc", 1, 7.5, 23, 0),
                P("arc", 2, 8, 8, 0),
                P("lshape", 0, 1, 8, 0),
                P("lshape", 1, 1, 13, 0),
                P("lshape", 2, 1, 18, 0),
                P("rect-a", 0, 1, 1, 0),
                P("rect-a", 1, 8, 1, 0),
                P("rect-a", 2, 15, 1, 0),
                P("rect-a", 3, 26, 1, 1.5707963267948966),
                P("rect-a", 4, 31, 1, 1.5707963267948966),
                P("rect-a", 5, 36, 1, 1.5707963267948966),
                P("rect-a", 6, 41, 1, 1.5707963267948966),
                P("rect-a", 7, 46, 1, 1.5707963267948966),
                P("rect-b", 0, 8, 14, 0),
                P("rect-b", 1, 8, 18, 0),
                P("rect-b", 2, 13, 14, 0),
                P("rect-b", 3, 13, 18, 0),
                P("rect-b", 4, 14, 23, 0),
                P("rect-b", 5, 14.5, 8, 0),
            ]
        );
    }

    [Fact]
    public void Strip_RectangleVarietyJob_GoldenLayout()
    {
        var result = SolveStable(new NestJobRunner(PlateNesterFactory.Create), StripJob());
        Assert.Equal(NestJobStatus.Complete, result.Status);
        AssertGolden(
            result,
            [
                P("rect-a", 0, 8, 1, 0),
                P("rect-a", 1, 15, 1, 0),
                P("rect-a", 2, 22, 1, 0),
                P("rect-a", 3, 29, 1, 0),
                P("rect-a", 4, 1, 6, 0),
                P("rect-b", 0, 8, 9, 0),
                P("rect-b", 1, 8, 13, 0),
                P("rect-b", 2, 8, 17, 0),
                P("rect-b", 3, 8, 21, 0),
                P("rect-c", 0, 23, 6, 1.5707963267948966),
                P("rect-c", 1, 31, 6, 1.5707963267948966),
                P("rect-c", 2, 1, 11, 0),
            ]
        );
    }

    /// <summary>
    /// OrderedPlateNester has no legacy counterpart, so it is pinned with plain deterministic
    /// poses (via its production wiring: StockLadderNestingEngine's default nester) instead of
    /// a legacy-parity comparison.
    /// </summary>
    [Fact]
    public void OrderedViaStockLadder_MixedJob_GoldenLayout()
    {
        var result = SolveStable(new StockLadderNestingEngine(), MixedJob("Default"));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        AssertGolden(
            result,
            [
                P("arc", 0, 1, 1, 0),
                P("arc", 1, 7.50001, 1, 0),
                P("arc", 2, 14.00002, 1, 0),
                P("lshape", 0, 15.00001, 12, 0),
                P("lshape", 1, 22.00002, 12, 0),
                P("lshape", 2, 29.00003, 12, 0),
                P("rect-a", 0, 1, 7, 0),
                P("rect-a", 1, 8.00001, 7, 0),
                P("rect-a", 2, 15.00002, 7, 0),
                P("rect-a", 3, 22.00003, 7, 0),
                P("rect-a", 4, 29.00004, 7, 0),
                P("rect-a", 5, 36.00005, 7, 0),
                P("rect-a", 6, 1, 12.00001, 0),
                P("rect-a", 7, 8.00001, 12.00001, 0),
                P("rect-b", 0, 1, 17.00001, 0),
                P("rect-b", 1, 6.00001, 17.00001, 0),
                P("rect-b", 2, 11.00002, 17.00001, 0),
                P("rect-b", 3, 16.00003, 17.00001, 0),
                P("rect-b", 4, 21.00004, 17.00001, 0),
                P("rect-b", 5, 26.000049999999998, 17.00001, 0),
            ]
        );
    }

    // --- progress-stream fixtures (preview behavior) ---

    private static NestJob ProgressJob() =>
        new(
            new[] { Part("a", Rect(6, 4), 5), Part("b", Rect(4, 3), 4) },
            new[]
            {
                new NestPlateStock("small", new Size(14, 14), 4, 1, new Spacing(1, 1, 1, 1)),
                new NestPlateStock("large", new Size(30, 24), 3, 1, new Spacing(1, 1, 1, 1)),
            },
            new NestJobOptions("Default")
        );

    private sealed record AuthoritativeStage(
        NestJobStage Stage,
        string StockId,
        int CommittedPlates,
        int CommittedParts
    );

    private sealed class StageCollector(
        List<AuthoritativeStage> stages,
        SortedSet<string> legacyPhases
    ) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value)
        {
            // Only authoritative (count-bearing) reports keep a stable order; legacy detail
            // reports arrive from parallel fill threads, so only their phase set is pinned.
            if (value.LegacyProgress == null)
                stages.Add(
                    new AuthoritativeStage(
                        value.Stage,
                        value.StockId,
                        value.CommittedPlates,
                        value.CommittedParts
                    )
                );
            else
                legacyPhases.Add(value.LegacyProgress.Phase.ToString());
        }
    }

    [Fact]
    public void StockLadder_ProgressStream_GoldenSequence()
    {
        var stages = new List<AuthoritativeStage>();
        var legacyPhases = new SortedSet<string>();
        var result = new StockLadderNestingEngine().Solve(
            ProgressJob(),
            new StageCollector(stages, legacyPhases)
        );

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(
            new[]
            {
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "small", 0, 0),
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "large", 0, 0),
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "small", 0, 0),
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "large", 0, 0),
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "small", 0, 0),
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "large", 0, 0),
                new AuthoritativeStage(NestJobStage.PlateCommitted, "small", 1, 4),
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "small", 1, 4),
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "large", 1, 4),
                new AuthoritativeStage(NestJobStage.PlateCommitted, "small", 2, 9),
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "small", 2, 9),
            },
            stages
        );
        // StockLadder evaluates its nester without a progress sink.
        Assert.Empty(legacyPhases);
    }

    [Fact]
    public void FixedStrategy_ProgressStream_GoldenSequence()
    {
        var stages = new List<AuthoritativeStage>();
        var legacyPhases = new SortedSet<string>();
        var result = new FixedStrategyNestingEngine("Default").Solve(
            ProgressJob(),
            new StageCollector(stages, legacyPhases)
        );

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(
            new[]
            {
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "small", 0, 0),
                new AuthoritativeStage(NestJobStage.EvaluatingCandidate, "large", 0, 0),
                new AuthoritativeStage(NestJobStage.PlateCommitted, "large", 1, 9),
            },
            stages
        );
        // The fill strategies report these phases as preview detail while evaluating candidates.
        Assert.Equal(["Linear", "Pairs", "RectBestFit"], legacyPhases);
    }
}
