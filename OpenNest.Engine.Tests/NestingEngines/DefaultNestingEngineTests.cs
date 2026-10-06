using OpenNest.Engine.Jobs;
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
    private static NestJobResult OneSheetHigher(NestJob job) => Layout(job, Sheet((0, 5), (5, 5)));
    private static NestJobResult TwoSheets(NestJob job) => Layout(job, Sheet((0, 0)), Sheet((0, 0)));
    private static NestJobResult Overlapping(NestJob job) => Layout(job, Sheet((0, 0), (1, 0)));
    private static NestJobResult OnePlaced(NestJob job) => Layout(job, Sheet((0, 0)));

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
        var high = OneSheetHigher(job);

        Assert.Same(low, Choose(job, Returns(_ => low), Returns(_ => high)));
        Assert.Same(high, Choose(job, Returns(_ => high), Returns(_ => low)));
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

    private sealed class Capture(Action<NestJobProgress> action) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value) => action(value);
    }
}

public sealed class DefaultContractTests : EngineContractTests<DefaultNestingEngine> { }
