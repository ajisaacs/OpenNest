using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines;
using OpenNest.Engine.NestingEngines.Default;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class DeadlineNestingEngineTests
{
    // Two 4x4 squares on 10x10 sheets with no spacing.
    private static NestJob SquaresJob() =>
        Job([Part("p", Rectangle(4, 4), 2, RotationPolicy.Fixed(0))], [Stock("s", 10, 10)]);

    private static NestJobResult Layout(NestJob job, params (double X, double Y)[][] sheets)
    {
        var builder = new NestJobResultBuilder(job);
        foreach (var sheet in sheets)
            builder.AddSheet(job.Plates[0], sheet.Select(p => ("p", p.X, p.Y, 0.0)));
        return builder.Build(
            builder.IsComplete ? NestJobStopReason.Completed : NestJobStopReason.NoPlacementFound
        );
    }

    private static NestJobResult OneSheet(NestJob job) => Layout(job, [(0, 0), (5, 0)]);

    private static NestJobResult TwoSheets(NestJob job) => Layout(job, [(0, 0)], [(0, 0)]);

    private static NestJobResult Overlapping(NestJob job) => Layout(job, [(0, 0), (1, 0)]);

    private sealed class Stub(
        Func<NestJob, IProgress<NestJobProgress>?, CancellationToken, NestJobResult> solve
    ) : INestingEngine
    {
        public NestJobResult Solve(
            NestJob job,
            IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default
        ) => solve(job, progress, token);
    }

    private sealed class Recorder : IProgress<NestJobProgress>
    {
        public List<NestJobProgress> Reports { get; } = new();

        public void Report(NestJobProgress value) => Reports.Add(value);
    }

    private static void Plan(IProgress<NestJobProgress>? progress, NestJobResult plan) =>
        progress?.Report(new NestJobProgress(NestJobStage.PlanImproved, "", -1, 0, 0, Plan: plan));

    /// <summary>Reports the given plans, then waits for cancellation like a long search.</summary>
    private static Stub ReportsThenRunsUntilCancelled(params Func<NestJob, NestJobResult>[] plans) =>
        new((job, progress, token) =>
        {
            foreach (var plan in plans)
                Plan(progress, plan(job));
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("the limit never cancelled the search");
        });

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void AnEngineThatFinishesInTimeKeepsItsResult()
    {
        var job = SquaresJob();
        var result = TwoSheets(job);
        var progress = new Recorder();

        var solved = new DeadlineNestingEngine(
            new Stub((_, p, _) =>
            {
                Plan(p, OneSheet(job));
                return result;
            }),
            TimeSpan.FromMinutes(1)
        ).Solve(job, progress);

        Assert.Same(result, solved);
        var commits = progress.Reports.Where(r => r.Stage == NestJobStage.PlateCommitted).ToList();
        Assert.Equal(2, commits.Count);
        Assert.Equal(2, commits[^1].CommittedParts);
    }

    [Fact]
    public void AtTheLimitTheBestValidPlanIsReturnedAndCommitted()
    {
        var job = SquaresJob();
        NestJobResult? cheap = null;
        var progress = new Recorder();

        var solved = new DeadlineNestingEngine(
            ReportsThenRunsUntilCancelled(TwoSheets, j => cheap = OneSheet(j), TwoSheets),
            Short
        ).Solve(job, progress);

        Assert.Same(cheap, solved);
        var commits = progress.Reports.Where(r => r.Stage == NestJobStage.PlateCommitted).ToList();
        Assert.Single(commits);
        Assert.Equal(2, commits[0].CommittedParts);
        // The later, worse plan is not shown after the better one.
        var shown = progress.Reports.Where(r => r.Stage == NestJobStage.PlanImproved).ToList();
        Assert.Equal(2, shown.Count);
        Assert.Same(cheap, shown[^1].Plan);
    }

    [Fact]
    public async Task TheLimitFiresWhileTheEngineHoldsEveryPoolThread()
    {
        // A CPU-bound parallel loop keeps every pool thread on its own local work, so a
        // thread-pool timer can fire seconds late; on one CPU it may wait for the whole search.
        // The solve runs on a pool thread, as the loop's own thread is one in a real engine.
        var job = SquaresJob();
        NestJobResult? plan = null;

        var engine = new DeadlineNestingEngine(
            new Stub((j, progress, token) =>
            {
                Plan(progress, plan = OneSheet(j));
                var stop = System.Diagnostics.Stopwatch.StartNew();
                Parallel.For(
                    0,
                    int.MaxValue,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    (_, loop) =>
                    {
                        if (token.IsCancellationRequested || stop.Elapsed.TotalSeconds > 30)
                            loop.Stop();
                        else
                            Thread.SpinWait(200);
                    }
                );
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("the limit never cancelled the search");
            }),
            Short
        );
        // Timed inside the pool thread: other tests can delay its start, not the limit.
        var (solved, elapsed) = await Task.Run(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            return (engine.Solve(job), clock.Elapsed);
        });

        Assert.Same(plan, solved);
        Assert.True(
            elapsed < TimeSpan.FromSeconds(3),
            $"the limit fired after {elapsed.TotalSeconds:0.0} s"
        );
    }

    [Fact]
    public void AnInvalidPlanIsNeverReturned()
    {
        var job = SquaresJob();
        Assert.NotEmpty(NestLayoutCheck.Violations(job, Overlapping(job)));

        var error = Assert.Throws<TimeoutException>(() =>
            new DeadlineNestingEngine(ReportsThenRunsUntilCancelled(Overlapping), Short).Solve(job)
        );

        Assert.Contains("No valid layout", error.Message);
    }

    [Fact]
    public void AValidPlanBeatsAnEarlierInvalidOne()
    {
        var job = SquaresJob();
        NestJobResult? valid = null;

        var solved = new DeadlineNestingEngine(
            ReportsThenRunsUntilCancelled(Overlapping, j => valid = TwoSheets(j)),
            Short
        ).Solve(job);

        Assert.Same(valid, solved);
    }

    [Fact]
    public void WithNoPlanTheLimitIsATimeout()
    {
        var job = SquaresJob();

        Assert.Throws<TimeoutException>(() =>
            new DeadlineNestingEngine(ReportsThenRunsUntilCancelled(), Short).Solve(job)
        );
    }

    [Fact]
    public void TheCallersCancellationIsNeverTurnedIntoAResult()
    {
        var job = SquaresJob();
        using var caller = new CancellationTokenSource(Short);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new DeadlineNestingEngine(ReportsThenRunsUntilCancelled(OneSheet), TimeSpan.FromMinutes(1))
                .Solve(job, null, caller.Token)
        );
    }

    [Fact]
    public void TheCallersCancellationIsNeverReportedAsATimeout()
    {
        var job = SquaresJob();
        using var caller = new CancellationTokenSource(Short);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new DeadlineNestingEngine(ReportsThenRunsUntilCancelled(), TimeSpan.FromMinutes(1))
                .Solve(job, null, caller.Token)
        );
    }

    [Fact]
    public void TheEnginesOwnCommitsAreNotForwarded()
    {
        var job = SquaresJob();
        var result = OneSheet(job);
        var progress = new Recorder();

        new DeadlineNestingEngine(
            new Stub((_, p, _) =>
            {
                p?.Report(new NestJobProgress(NestJobStage.PlateCommitted, "s", 0, 1, 2));
                return result;
            }),
            TimeSpan.FromMinutes(1)
        ).Solve(job, progress);

        Assert.Single(progress.Reports, r => r.Stage == NestJobStage.PlateCommitted);
    }

    [Fact]
    public void ALimitMustFitACancellationTimer()
    {
        var engine = new Stub((_, _, _) => null!);
        foreach (var bad in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromDays(50) })
            Assert.Throws<ArgumentOutOfRangeException>(() => new DeadlineNestingEngine(engine, bad));
        Assert.Equal(
            DeadlineNestingEngine.MaxLimit,
            new DeadlineNestingEngine(engine, DeadlineNestingEngine.MaxLimit).Limit
        );
        Assert.Throws<ArgumentNullException>(() => new DeadlineNestingEngine(null!, Short));
    }

    [Fact]
    public void ATiedLaterPlanReplacesTheEarlierOne()
    {
        // Engines report only plans they rank higher, so a tie follows the engine's judgment.
        var job = SquaresJob();
        NestJobResult? later = null;
        var progress = new Recorder();

        var solved = new DeadlineNestingEngine(
            ReportsThenRunsUntilCancelled(OneSheet, j => later = Layout(j, [(6, 0), (6, 4)])),
            Short
        ).Solve(job, progress);

        Assert.Same(later, solved);
        Assert.Same(later, progress.Reports.Last(r => r.Stage == NestJobStage.PlanImproved).Plan);
    }

    [Fact]
    public void AResultReturnedAfterTheLimitIsTheEnginesOwn()
    {
        var job = SquaresJob();
        var result = TwoSheets(job);

        var solved = new DeadlineNestingEngine(
            new Stub((j, p, token) =>
            {
                Plan(p, OneSheet(j));
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                return result;
            }),
            Short
        ).Solve(job);

        Assert.Same(result, solved);
    }

    [Fact]
    public void OtherProgressIsForwarded()
    {
        var job = SquaresJob();
        var progress = new Recorder();
        var stage = new NestJobProgress(NestJobStage.EvaluatingCandidate, "s", 0, 0, 2);

        new DeadlineNestingEngine(
            new Stub((j, p, _) =>
            {
                p?.Report(stage);
                return OneSheet(j);
            }),
            TimeSpan.FromMinutes(1)
        ).Solve(job, progress);

        Assert.Same(stage, progress.Reports[0]);
    }

    [Fact]
    public void DefaultUnderALimitReturnsTheFasterCandidatesResult()
    {
        // The slow candidate is first in tie-break order but runs second, so the limit finds
        // the quick candidate's layout already reported.
        var job = SquaresJob();
        NestJobResult? quick = null;
        var slow = ReportsThenRunsUntilCancelled();
        var fast = new Stub((j, _, _) => quick = TwoSheets(j));

        var solved = new DeadlineNestingEngine(
            new DefaultNestingEngine(new Func<INestingEngine>[] { () => slow, () => fast }, new[] { 1, 0 }),
            Short
        ).Solve(job);

        Assert.Same(quick, solved);
    }
}
