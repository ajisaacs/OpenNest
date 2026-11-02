using OpenNest.Api;
using OpenNest.Data;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Remote;

namespace OpenNest.Server.Worker;

/// <summary>
/// Solves the nest server's queued jobs one at a time. Each job is read from its package and
/// run through <see cref="NestPipeline"/> with the named built-in engine wrapped in a
/// <see cref="RecordingNestingEngine"/>, with no time limit; the recorded engine answers are
/// the result the desktop replays. A heartbeat keeps the job's lease; the engine's progress and
/// best plan so far are stored on the server as it runs (<see cref="NestJobReporter"/>), and a
/// cancel request is seen within one report interval.
/// </summary>
public sealed class NestJobWorker
{
    private readonly RemoteNestJobs _jobs;
    private readonly ILogger _logger;
    private readonly TimeSpan _heartbeat;
    private readonly TimeSpan _reportInterval;

    public NestJobWorker(RemoteNestJobs jobs, ILogger logger, TimeSpan? heartbeat = null, TimeSpan? reportInterval = null)
    {
        _jobs = jobs;
        _logger = logger;
        _heartbeat = heartbeat ?? NestJobRules.HeartbeatInterval;
        _reportInterval = reportInterval ?? NestJobRules.ProgressInterval;
    }

    public string WorkerId { get; } = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    /// <summary>Called after each heartbeat the server accepts, so a long job still shows the worker alive.</summary>
    public Action? Alive { get; set; }

    /// <summary>Claims and runs one job. False when there was none to run.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken stoppingToken)
    {
        var job = await _jobs.ClaimAsync(WorkerId, stoppingToken);
        if (job is null)
            return false;

        _logger.LogInformation("Nest job {Id} ({Title}, {Engine}) started, attempt {Attempt}.",
            job.Id, job.Title, job.EngineName, job.Attempts);
        using var solve = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var stop = StopReason.None;
        using var beating = new CancellationTokenSource();
        void Stop(StopReason reason)
        {
            if (stop == StopReason.None)
                stop = reason;
            solve.Cancel();
        }
        var heartbeat = HeartbeatAsync(job.Id, solve, reason => stop = reason, beating.Token);
        var reporter = new NestJobReporter();
        var reporting = ReportAsync(job.Id, reporter, Stop, beating.Token);
        try
        {
            NestPipelineRequest request;
            try
            {
                request = NestJobPackage.Read(await _jobs.GetPackageAsync(job.Id, solve.Token));
            }
            catch (FormatException ex)
            {
                await Report(_jobs.FailAsync(job.Id, WorkerId, $"The nest job could not be read: {ex.Message}"));
                return true;
            }
            if (!NestingEngineRegistry.IsBuiltIn(request.EngineName))
            {
                await Report(_jobs.FailAsync(job.Id, WorkerId,
                    $"The nest server has no '{request.EngineName}' engine; only built-in engines run there."));
                return true;
            }

            var recorder = new RecordingNestingEngine(NestingEngineRegistry.Create(request.EngineName)) { Progress = reporter };
            var result = await Task.Factory.StartNew(() => NestPipeline.Run(recorder, request.EngineName, request, progress: null, solve.Token),
                solve.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            if (await _jobs.CompleteAsync(job.Id, WorkerId, recorder.Transcript.ToJson(), NestJobSummaries.From(result), CancellationToken.None))
                _logger.LogInformation("Nest job {Id} finished.", job.Id);
            else
                _logger.LogWarning("Nest job {Id} finished after this worker lost it; the result was dropped.", job.Id);
        }
        catch (OperationCanceledException) when (solve.IsCancellationRequested)
        {
            switch (stop)
            {
                case StopReason.CancelRequested:
                    await Report(_jobs.ConfirmCancelledAsync(job.Id, WorkerId));
                    _logger.LogInformation("Nest job {Id} stopped on request.", job.Id);
                    break;
                case StopReason.LeaseLost:
                    _logger.LogWarning("Nest job {Id} stopped: this worker no longer holds it.", job.Id);
                    break;
                default:
                    // Shutting down: the job goes back to the front of the queue.
                    await Report(_jobs.ReleaseAsync(job.Id, WorkerId));
                    _logger.LogInformation("Nest job {Id} given back while the worker stops.", job.Id);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Nest job {Id} failed.", job.Id);
            await Report(_jobs.FailAsync(job.Id, WorkerId, ex.Message));
        }
        finally
        {
            beating.Cancel();
            await heartbeat;
            await reporting;
        }
        return true;
    }

    private async Task ReportAsync(Guid id, NestJobReporter reporter, Action<StopReason> stopped, CancellationToken token)
    {
        var reason = await reporter.RunAsync(_jobs, id, WorkerId, _reportInterval, _logger, token);
        if (reason != StopReason.None)
            stopped(reason);
    }

    private async Task HeartbeatAsync(Guid id, CancellationTokenSource solve, Action<StopReason> stopped, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(_heartbeat, token); }
            catch (OperationCanceledException) { return; }
            try
            {
                var beat = await _jobs.HeartbeatAsync(id, WorkerId, token);
                if (beat is not null)
                    Alive?.Invoke();
                if (beat is null || beat.CancelRequested)
                {
                    stopped(beat is null ? StopReason.LeaseLost : StopReason.CancelRequested);
                    solve.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                // The lease outlasts several missed beats; keep solving and try again.
                _logger.LogWarning("Heartbeat for nest job {Id} failed: {Message}", id, ex.Message);
            }
        }
    }

    // Reports run after the solve's token may be cancelled, so they never take it.
    private async Task Report(Task<bool> report)
    {
        try
        {
            if (!await report)
                _logger.LogWarning("The nest server no longer gave this worker the job it reported on.");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Could not report to the nest server: {Message}", ex.Message);
        }
    }

    public enum StopReason
    {
        None,
        CancelRequested,
        LeaseLost,
    }
}
