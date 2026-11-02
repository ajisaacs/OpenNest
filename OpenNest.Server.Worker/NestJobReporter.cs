using OpenNest.Data;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Remote;

namespace OpenNest.Server.Worker;

/// <summary>
/// Collects a running job's engine progress and stores it on the nest server at most once per
/// interval: only the latest stage and the latest plan are sent, so a chatty engine costs one
/// small write and one plan write per interval at most. Each round, a report (or, with nothing
/// new to send, a read of the job) also tells the worker promptly that someone asked to stop.
/// </summary>
public sealed class NestJobReporter : IProgress<NestJobProgress>
{
    private readonly object _sync = new();
    private RemoteNestProgress? _stage;
    private RemoteNestPlan? _plan;

    public void Report(NestJobProgress value)
    {
        if (value is null)
            return;
        // Copied here, on the engine's thread, so later changes to its objects cannot leak in;
        // serialized later, off that thread.
        if (value.Stage == NestJobStage.PlanImproved)
        {
            if (value.Plan is null)
                return;
            var plan = RemoteNestPlan.From(value.Plan);
            lock (_sync)
                _plan = plan;
            return;
        }
        var stage = RemoteNestProgress.From(value);
        lock (_sync)
            _stage = stage;
    }

    /// <summary>
    /// Sends reports every <paramref name="interval"/> until <paramref name="token"/> is
    /// cancelled. Returns why the job must stop (a cancel request, or the worker lost it),
    /// or <see cref="NestJobWorker.StopReason.None"/> when cancelled first.
    /// </summary>
    public async Task<NestJobWorker.StopReason> RunAsync(RemoteNestJobs jobs, Guid id, string workerId, TimeSpan interval,
        ILogger logger, CancellationToken token)
    {
        while (true)
        {
            try { await Task.Delay(interval, token); }
            catch (OperationCanceledException) { return NestJobWorker.StopReason.None; }

            RemoteNestProgress? stage;
            RemoteNestPlan? plan;
            lock (_sync)
            {
                (stage, plan) = (_stage, _plan);
                (_stage, _plan) = (null, null);
            }
            try
            {
                var beats = new List<NestJobHeartbeat?>();
                if (plan is not null)
                    beats.Add(await jobs.ReportPlanAsync(id, workerId, plan.ToJson(), token));
                if (stage is not null)
                    beats.Add(await jobs.ReportProgressAsync(id, workerId, stage.ToJson(), token));
                if (beats.Count == 0)
                {
                    // Nothing new: read the job instead, which costs the server no write.
                    var job = await jobs.GetAsync(id, token);
                    beats.Add(job is { State: NestJobState.Running } ? new NestJobHeartbeat { CancelRequested = job.CancelRequested } : null);
                }
                if (beats.Any(b => b is null))
                    return NestJobWorker.StopReason.LeaseLost;
                if (beats.Any(b => b!.CancelRequested))
                    return NestJobWorker.StopReason.CancelRequested;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return NestJobWorker.StopReason.None;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                // Keep solving; the next round sends whatever is latest by then.
                lock (_sync)
                {
                    _stage ??= stage;
                    _plan ??= plan;
                }
                logger.LogWarning("Progress for nest job {Id} not stored: {Message}", id, ex.Message);
            }
        }
    }
}
