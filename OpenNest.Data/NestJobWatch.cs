namespace OpenNest.Data;

/// <summary>
/// Follows one server nest job to its final state for a desktop: the job's event stream
/// while it holds, polling while it does not. A dropped stream is followed by one poll (so
/// nothing is missed), then reconnects with the last event id; a server without the stream is
/// polled throughout. Reports go to <see cref="IProgress{T}"/> sinks, so a UI receives them
/// on its own thread. See <c>docs/server-nesting.md</c>.
/// </summary>
public sealed class NestJobWatch
{
    private readonly RemoteNestJobs _jobs;

    public NestJobWatch(RemoteNestJobs jobs)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    /// <summary>How long to wait between polls, and before reconnecting a dropped stream.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan IdleTimeout { get; init; } = NestJobRules.StreamIdleTimeout;

    /// <summary>Each new state of the job, the first being the one <see cref="RunAsync"/> was given.</summary>
    public IProgress<NestJobRecord>? Job { get; init; }

    /// <summary>Each new best plan so far (<c>RemoteNestPlan</c> JSON).</summary>
    public IProgress<string>? Plan { get; init; }

    /// <summary>Why the job's updates are late, or empty once they arrive again. The job keeps running meanwhile.</summary>
    public IProgress<string>? Problem { get; init; }

    /// <summary>Waits for <paramref name="job"/> to finish and returns its final state.</summary>
    public async Task<NestJobRecord> RunAsync(NestJobRecord job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        Job?.Report(job);
        var lastId = 0L;
        var planRevision = 0L;
        var streaming = true;
        while (!job.IsFinished)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (streaming)
            {
                try
                {
                    await foreach (var update in _jobs.WatchAsync(job.Id, lastId, IdleTimeout, cancellationToken))
                    {
                        switch (update.Kind)
                        {
                            case NestJobEventKind.Open:
                                Problem?.Report("");
                                break;
                            case NestJobEventKind.Gone:
                                throw Gone();
                            case NestJobEventKind.Plan:
                                lastId = System.Math.Max(lastId, update.Id);
                                planRevision = update.Id;
                                Plan?.Report(update.Plan);
                                break;
                            case NestJobEventKind.Job:
                                lastId = System.Math.Max(lastId, update.Id);
                                job = update.Job!;
                                Job?.Report(job);
                                if (job.IsFinished)
                                    return job;
                                break;
                        }
                    }
                    Problem?.Report("The nest server's job updates stopped; reconnecting");
                }
                catch (ServerTooOldException)
                {
                    streaming = false;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                    && ex is IOException or HttpRequestException or TaskCanceledException)
                {
                    Problem?.Report($"Waiting for the nest server: {ex.Message}");
                }
            }

            // Between streams, or instead of them on a server without one: one poll.
            try
            {
                var polled = await _jobs.GetAsync(job.Id, cancellationToken) ?? throw Gone();
                if (polled.PlanRevision > planRevision && await _jobs.GetPlanAsync(job.Id, cancellationToken) is { } plan
                    && plan.Revision > planRevision)
                {
                    planRevision = plan.Revision;
                    Plan?.Report(plan.Plan);
                }
                if (polled.Revision != job.Revision || polled.QueuePosition != job.QueuePosition || polled.IsFinished)
                    Job?.Report(polled);
                job = polled;
                if (!streaming)
                    Problem?.Report("");
                if (job.IsFinished)
                    return job;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                && ex is IOException or HttpRequestException or TaskCanceledException)
            {
                Problem?.Report($"Waiting for the nest server: {ex.Message}");
            }
            await Task.Delay(PollInterval, cancellationToken);
        }
        return job;
    }

    private static InvalidOperationException Gone() => new("The nest server no longer has this job.");
}
