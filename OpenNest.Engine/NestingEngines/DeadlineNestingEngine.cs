#nullable enable
using System;
using System.Threading;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines;

/// <summary>
/// Runs another engine under a wall-clock limit. When the engine finishes in time its result
/// is returned unchanged. When the limit cancels it, the best whole-job plan it reported
/// (<see cref="NestJobStage.PlanImproved"/>) is returned instead, provided that plan passes
/// <see cref="NestLayoutCheck"/>; plans are ranked like <see cref="CandidateScore"/> ranks
/// results, and a tie goes to the later plan, as engines report only plans they rank higher.
/// With no valid plan by the limit, the solve fails with a <see cref="TimeoutException"/>.
/// Cancellation by the caller's own token is never turned into a result. A result the engine
/// returns, even after the limit, is its own and is returned unchanged. The engine sees the
/// job with <see cref="NestJobOptions.SearchUntilCancelled"/> set, so an engine that supports
/// it keeps improving its plan until the limit.
/// </summary>
/// <remarks>
/// The engine's plate commits are not forwarded: the returned result's commits are reported
/// once it is known. Plans are forwarded only when they beat every plan shown so far. A
/// limited solve depends on the clock, so it is not reproducible like an unlimited one, and
/// the engine may overrun the limit by however long it takes to observe cancellation. Plans
/// are expected from the solve thread, as the built-in engines report them. Cancellation
/// callbacks run on a dedicated clock thread that the solve joins before returning, so a
/// callback must not wait on the solve thread.
/// </remarks>
public sealed class DeadlineNestingEngine : INestingEngine
{
    /// <summary>The longest limit accepted: about 49.7 days, kept from the original timer bound.
    /// </summary>
    public static readonly TimeSpan MaxLimit = TimeSpan.FromMilliseconds(uint.MaxValue - 1.0);

    private readonly INestingEngine inner;

    public DeadlineNestingEngine(INestingEngine inner, TimeSpan limit)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (limit <= TimeSpan.Zero || limit > MaxLimit)
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"The time limit must be positive and at most {MaxLimit}."
            );
        this.inner = inner;
        Limit = limit;
    }

    public TimeSpan Limit { get; }

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();
        // Engines that can keep searching until cancelled may use all the time.
        job = new NestJob(job.Parts, job.Plates, job.Options.WithSearchUntilCancelled(true));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var finished = new ManualResetEventSlim();
        var clock = StartClock(deadline, finished);
        var plans = new BestPlan(job, progress);

        NestJobResult result;
        try
        {
            result = inner.Solve(job, plans, deadline.Token);
        }
        catch (OperationCanceledException)
            when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            if (plans.Best is not { Valid: true } best)
                throw new TimeoutException(
                    $"No valid layout was found within the {Limit.TotalSeconds:0.###} s time limit."
                );
            result = best.Result;
        }
        finally
        {
            finished.Set();
            clock.Join();
        }

        token.ThrowIfCancellationRequested();
        if (result == null)
            throw new InvalidOperationException("The engine returned no result.");
        CandidateProgress.ReportCommits(result, progress);
        return result;
    }

    /// <summary>
    /// Cancels <paramref name="deadline"/> at the limit from a dedicated thread. A thread-pool
    /// timer (<see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>) can fire many
    /// seconds late, or not before the solve ends, when the engine's parallel loops hold every
    /// pool thread, as they do on one CPU.
    /// </summary>
    private Thread StartClock(CancellationTokenSource deadline, ManualResetEventSlim finished)
    {
        var thread = new Thread(() =>
        {
            var due = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                var left = Limit - due.Elapsed;
                if (left <= TimeSpan.Zero)
                    break;
                var wait = left.TotalMilliseconds >= int.MaxValue
                    ? TimeSpan.FromMilliseconds(int.MaxValue - 1.0)
                    : left;
                if (finished.Wait(wait))
                    return;
            }
            deadline.Cancel();
        })
        {
            IsBackground = true,
            Name = "OpenNest time limit",
        };
        thread.Start();
        return thread;
    }

    /// <summary>Keeps the best reported plan and forwards everything but plate commits.</summary>
    private sealed class BestPlan(NestJob job, IProgress<NestJobProgress>? inner)
        : IProgress<NestJobProgress>
    {
        public CandidateScore? Best { get; private set; }

        public void Report(NestJobProgress value)
        {
            if (value == null || value.Stage == NestJobStage.PlateCommitted)
                return;
            if (value.Stage != NestJobStage.PlanImproved)
            {
                inner?.Report(value);
                return;
            }
            if (value.Plan == null)
                return;

            CandidateScore plan;
            try
            {
                plan = CandidateScore.Of(job, value.Plan, 0);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A plan that cannot be scored is never kept or shown.
                return;
            }
            if (Best != null && Best.IsBetterThan(plan))
                return;
            Best = plan;
            inner?.Report(value);
        }
    }
}
