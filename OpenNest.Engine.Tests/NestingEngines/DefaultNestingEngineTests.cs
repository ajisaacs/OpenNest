using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines;
using OpenNest.Engine.NestingEngines.Default;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Engine.NestingEngines.Rectangles;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class DefaultNestingEngineTests
{
    // Two 4x4 squares on 10x10 sheets with no spacing: either both on one sheet (cost 100),
    // or one per sheet (cost 200), or one placed and one unplaced (100 + a 100 penalty).
    private static NestJob SquaresJob() =>
        Job([Part("p", Rectangle(4, 4), 2, RotationPolicy.Fixed(0))], [Stock("s", 10, 10)]);

    private static NestJobResult Layout(NestJob job, params (double X, double Y)[][] sheets)
    {
        var builder = new NestJobResultBuilder(job);
        foreach (var sheet in sheets)
            builder.AddSheet(job.Plates[0], sheet.Select(p => ("p", p.X, p.Y, 0.0)));
        return builder.Build(builder.IsComplete ? NestJobStopReason.Completed : NestJobStopReason.NoPlacementFound);
    }

    private static (double X, double Y)[] Sheet(params (double X, double Y)[] poses) => poses;

    private static NestJobResult OneSheet(NestJob job) => Layout(job, Sheet((0, 0), (5, 0)));
    // Same 10 x 6 free strip as OneSheet, below the parts instead of above them.
    private static NestJobResult OneSheetAtTop(NestJob job) => Layout(job, Sheet((0, 6), (5, 6)));
    // Only a 10 x 5 strip left below the parts.
    private static NestJobResult OneSheetHigher(NestJob job) => Layout(job, Sheet((0, 5), (5, 5)));
    private static NestJobResult TwoSheets(NestJob job) => Layout(job, Sheet((0, 0)), Sheet((0, 0)));
    private static NestJobResult Raised(NestJob job, double y) => Layout(job, Sheet((0, y), (5, y)));
    private static NestJobResult Overlapping(NestJob job) => Layout(job, Sheet((0, 0), (1, 0)));
    private static NestJobResult OnePlaced(NestJob job) => Layout(job, Sheet((0, 0)));

    [Fact]
    public void BuiltInDefaultRunsIrregularLast()
    {
        // Rectangles is the cheap, always-valid fallback: under a limit it must have reported
        // its plan before the slower Irregular search starts, and Irregular, which may search
        // until cancelled, must not keep Circles from running. Candidates stay in tie-break
        // order Irregular, Rectangles, Circles.
        Assert.Equal(new[] { 1, 2, 0 }, new DefaultNestingEngine().RunOrder);
    }

    [Fact]
    public void DefaultsRunOrderNeverChangesTheChoice()
    {
        // Equal layouts keep tie-break (candidate) order whichever candidate runs first.
        var job = SquaresJob();
        var first = OneSheet(job);
        var second = OneSheet(job);
        Func<INestingEngine>[] candidates =
        [
            () => new Stub((_, _, _) => first),
            () => new Stub((_, _, _) => second),
        ];

        Assert.Same(first, new DefaultNestingEngine(candidates, new[] { 0, 1 }).Solve(job));
        Assert.Same(first, new DefaultNestingEngine(candidates, new[] { 1, 0 }).Solve(job));
    }

    [Fact]
    public void DefaultRejectsARunOrderThatIsNotAPermutation()
    {
        Func<INestingEngine>[] candidates =
        [
            () => new Stub((_, _, _) => null!),
            () => new Stub((_, _, _) => null!),
        ];

        Assert.Throws<ArgumentException>(() => new DefaultNestingEngine(candidates, new[] { 0, 0 }));
        Assert.Throws<ArgumentException>(() => new DefaultNestingEngine(candidates, new[] { 0 }));
        Assert.Throws<ArgumentException>(() => new DefaultNestingEngine(candidates, new[] { 0, 2 }));
    }

    [Fact]
    public void WhenEveryCandidateThrowsTheFirstInTieBreakOrderIsRethrown()
    {
        Func<INestingEngine>[] candidates =
        [
            () => new Stub((_, _, _) => throw new InvalidOperationException("first")),
            () => new Stub((_, _, _) => throw new InvalidOperationException("second")),
        ];

        var error = Assert.Throws<InvalidOperationException>(() =>
            new DefaultNestingEngine(candidates, new[] { 1, 0 }).Solve(SquaresJob())
        );

        Assert.Equal("first", error.Message);
    }

    [Fact]
    public void NearTiesAreChosenInTieBreakOrderWhateverTheRunOrder()
    {
        // Offcuts within the comparison tolerance tie, so these three are not transitively
        // ordered: neighbours tie, but the lowest layout beats the highest outright. Choosing
        // in run order would then depend on the order; choosing in tie-break order must not.
        var job = SquaresJob();
        var heights = new[] { 8e-9, 4e-9, 0 };
        var scores = heights.Select(y => CandidateScore.Of(job, Raised(job, y), 0)).ToArray();
        Assert.False(scores[0].IsBetterThan(scores[1]) || scores[1].IsBetterThan(scores[0]));
        Assert.False(scores[1].IsBetterThan(scores[2]) || scores[2].IsBetterThan(scores[1]));
        Assert.True(scores[2].IsBetterThan(scores[0]));

        var candidates = heights.Select(y => Returns(j => Raised(j, y))).ToArray();
        var orders = new[]
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
        };
        var picks = orders
            .Select(order => new DefaultNestingEngine(candidates, order).Solve(job).Plates[0].Placements[0].Y)
            .ToList();
        Assert.All(picks, y => Assert.Equal(picks[0], y));
    }

    private sealed class Stub(Func<NestJob, IProgress<NestJobProgress>?, CancellationToken, NestJobResult> solve) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null, CancellationToken token = default) =>
            solve(job, progress, token);
    }

    private static Func<INestingEngine> Returns(Func<NestJob, NestJobResult> layout) =>
        () => new Stub((job, _, _) => layout(job));

    private static NestJobResult Choose(NestJob job, params Func<INestingEngine>[] candidates) =>
        new DefaultNestingEngine(candidates).Solve(job);

    [Fact]
    public void CheaperValidLayoutWinsInEitherOrder()
    {
        var job = SquaresJob();
        var cheap = OneSheet(job);
        var dear = TwoSheets(job);

        Assert.Same(cheap, Choose(job, Returns(_ => dear), Returns(_ => cheap)));
        Assert.Same(cheap, Choose(job, Returns(_ => cheap), Returns(_ => dear)));
    }

    [Fact]
    public void InvalidLayoutLosesToAValidOneEvenWhenSmaller()
    {
        var job = SquaresJob();
        var invalid = Overlapping(job);
        var valid = TwoSheets(job);
        Assert.NotEmpty(NestLayoutCheck.Violations(job, invalid));

        Assert.Same(valid, Choose(job, Returns(_ => invalid), Returns(_ => valid)));
        Assert.Same(valid, Choose(job, Returns(_ => valid), Returns(_ => invalid)));
    }

    [Fact]
    public void PlacingEveryPartWinsAtEqualCost()
    {
        var job = SquaresJob();
        var complete = TwoSheets(job);
        var partial = OnePlaced(job);
        Assert.Equal(NestJobCost.Evaluate(job, complete), NestJobCost.Evaluate(job, partial), 9);

        Assert.Same(complete, Choose(job, Returns(_ => partial), Returns(_ => complete)));
        Assert.Same(complete, Choose(job, Returns(_ => complete), Returns(_ => partial)));
    }

    [Fact]
    public void EqualLayoutsKeepCandidateOrder()
    {
        var job = SquaresJob();
        var low = OneSheet(job);
        var high = OneSheetAtTop(job);

        Assert.Same(low, Choose(job, Returns(_ => low), Returns(_ => high)));
        Assert.Same(high, Choose(job, Returns(_ => high), Returns(_ => low)));
    }

    [Fact]
    public void LargerOffcutWinsAtEqualCostInEitherOrder()
    {
        var job = SquaresJob();
        var compact = OneSheet(job);
        var loose = OneSheetHigher(job);
        Assert.Equal(NestJobCost.Evaluate(job, compact), NestJobCost.Evaluate(job, loose));

        Assert.Same(compact, Choose(job, Returns(_ => loose), Returns(_ => compact)));
        Assert.Same(compact, Choose(job, Returns(_ => compact), Returns(_ => loose)));
    }

    [Fact]
    public void ACrashingCandidateIsSkipped()
    {
        var job = SquaresJob();
        var valid = TwoSheets(job);

        var result = Choose(job, () => new Stub((_, _, _) => throw new InvalidOperationException("boom")), Returns(_ => valid));

        Assert.Same(valid, result);
    }

    [Fact]
    public void ACandidateWithoutAResultIsSkipped()
    {
        var job = SquaresJob();
        var valid = TwoSheets(job);

        Assert.Same(valid, Choose(job, () => new Stub((_, _, _) => null!), Returns(_ => valid)));
    }

    [Fact]
    public void WhenEveryCandidateCrashesTheFirstFailureIsRethrown()
    {
        var job = SquaresJob();

        var error = Assert.Throws<InvalidOperationException>(() => Choose(job,
            () => new Stub((_, _, _) => throw new InvalidOperationException("first")),
            () => new Stub((_, _, _) => throw new InvalidOperationException("second"))));

        Assert.Equal("first", error.Message);
    }

    [Fact]
    public void CancellationStopsTheSearch()
    {
        var job = SquaresJob();
        using var cancellation = new CancellationTokenSource();
        var laterRan = false;

        Assert.ThrowsAny<OperationCanceledException>(() => new DefaultNestingEngine(new Func<INestingEngine>[]
        {
            () => new Stub((_, _, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return OneSheet(job); }),
            () => { laterRan = true; return new Stub((j, _, _) => OneSheet(j)); },
        }).Solve(job, token: cancellation.Token));
        Assert.False(laterRan);
    }

    [Fact]
    public void OnlyTheChosenLayoutCommitsAreReported()
    {
        var job = SquaresJob();
        Func<INestingEngine> Reporting(Func<NestJob, NestJobResult> layout) => () => new Stub((j, progress, _) =>
        {
            progress?.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, "s", 0, 0, 0));
            var result = layout(j);
            foreach (var plate in result.Plates)
                progress?.Report(new NestJobProgress(NestJobStage.PlateCommitted, "s", plate.PlateIndex, 99, 99));
            return result;
        });
        var reports = new List<NestJobProgress>();

        var result = new DefaultNestingEngine(new[] { Reporting(TwoSheets), Reporting(OneSheet) })
            .Solve(job, new Capture(reports.Add));

        Assert.Single(result.Plates);
        Assert.Equal(2, reports.Count(r => r.Stage == NestJobStage.EvaluatingCandidate));
        var commit = Assert.Single(reports, r => r.Stage == NestJobStage.PlateCommitted);
        Assert.Equal((0, 1, 2), (commit.PlateIndex, commit.CommittedPlates, commit.CommittedParts));
        Assert.Equal(NestJobStage.PlateCommitted, reports[^1].Stage);
    }

    [Fact]
    public void BuiltInCandidatesReturnTheCheaperOfIrregularAndRectangles()
    {
        var job = Job([Part("disc", Disc(2), 6), Part("ell", LShape(9, 7, 3), 4), Part("plate", Rectangle(12, 5), 3)],
            [Stock("a", 24, 30, 0.25), Stock("b", 30, 40, 0.25)]);

        var result = new DefaultNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        var best = new INestingEngine[] { new IrregularNestingEngine(), new RectanglesNestingEngine() }
            .Select(engine => engine.Solve(job))
            .Where(r => NestLayoutCheck.Violations(job, r).Count == 0 && r.Status == NestJobStatus.Complete)
            .Min(r => NestJobCost.Evaluate(job, r));
        Assert.Equal(best, NestJobCost.Evaluate(job, result), 9);
    }

    /// <summary>A candidate that reports each given plan, then returns its layout.</summary>
    private static Func<INestingEngine> Plans(Func<NestJob, NestJobResult> layout, params Func<NestJob, NestJobResult>[] plans) =>
        () => new Stub((j, progress, _) =>
        {
            foreach (var plan in plans)
                progress?.Report(new NestJobProgress(NestJobStage.PlanImproved, string.Empty, -1, 0, 0, Plan: plan(j)));
            return layout(j);
        });

    private static List<NestJobResult> ReportedPlans(NestJob job, out NestJobResult result, params Func<INestingEngine>[] candidates)
    {
        var reports = new List<NestJobProgress>();
        result = new DefaultNestingEngine(candidates).Solve(job, new Capture(reports.Add));
        return reports.Where(r => r.Stage == NestJobStage.PlanImproved).Select(r => r.Plan).ToList();
    }

    [Fact]
    public void ALaterCandidatesWorsePlanIsNotShownAfterABetterOne()
    {
        var job = SquaresJob();
        var cheap = OneSheet(job);
        var dear = TwoSheets(job);

        var plans = ReportedPlans(job, out var result, Plans(_ => cheap, _ => cheap), Plans(_ => dear, _ => dear));

        Assert.Same(cheap, result);
        Assert.Same(cheap, Assert.Single(plans));
    }

    [Fact]
    public void ABetterPlanFromALaterCandidateReplacesTheShownOne()
    {
        var job = SquaresJob();
        var cheap = OneSheet(job);
        var dear = TwoSheets(job);
        var partial = OnePlaced(job);

        var plans = ReportedPlans(job, out var result,
            Plans(_ => dear, _ => partial, _ => dear), Plans(_ => cheap, _ => dear, _ => cheap), Plans(_ => dear, _ => dear));

        Assert.Same(cheap, result);
        // Fewest unplaced first, then cost: a plan equal to the shown one is not shown again.
        Assert.Equal(new[] { partial, dear, cheap }, plans);
    }

    [Fact]
    public void TheChosenResultIsTheLastPlanEvenWhenAnUnreturnedPlanScoredBetter()
    {
        var job = SquaresJob();
        var cheap = OneSheet(job);
        var invalid = Overlapping(job);
        var dear = TwoSheets(job);

        // The first candidate shows a cheap valid plan but returns an invalid layout; the second
        // reports no plans, and its valid result is chosen.
        var plans = ReportedPlans(job, out var result, Plans(_ => invalid, _ => cheap), Returns(_ => dear));

        Assert.Same(dear, result);
        Assert.Equal(new[] { cheap, dear }, plans);
    }

    [Fact]
    public void ResultsOfCandidatesWithoutPlansAreShownWhenBetter()
    {
        var job = SquaresJob();
        var cheap = OneSheet(job);
        var dear = TwoSheets(job);

        var plans = ReportedPlans(job, out var result, Returns(_ => dear), Returns(_ => cheap), Returns(_ => dear));

        Assert.Same(cheap, result);
        Assert.Equal(new[] { dear, cheap }, plans);
    }

    [Fact]
    public void BuiltInCandidatesReportTheirBestPlanEndingWithTheReturnedLayout()
    {
        var job = Job([Part("disc", Disc(2), 6), Part("ell", LShape(9, 7, 3), 4), Part("plate", Rectangle(12, 5), 3)],
            [Stock("a", 24, 30, 0.25), Stock("b", 30, 40, 0.25)]);

        PlanAssert.ReportedAndEndWithTheResult(() => new DefaultNestingEngine(), job);
    }

    private sealed class Capture(Action<NestJobProgress> action) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value) => action(value);
    }
}

public sealed class DefaultContractTests : EngineContractTests<DefaultNestingEngine> { }
