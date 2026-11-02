namespace OpenNest.Server.Worker;

/// <summary>
/// Runs <see cref="NestJobWorker"/> until the host stops: straight on to the next job after
/// one finishes, otherwise every <see cref="WorkerOptions.PollInterval"/>. Touches
/// <see cref="WorkerOptions.AliveFile"/> each round and on each heartbeat of a running job;
/// the container health check reads it.
/// </summary>
public sealed class NestJobWorkerService : BackgroundService
{
    private readonly NestJobWorker _worker;
    private readonly WorkerOptions _options;
    private readonly ILogger<NestJobWorkerService> _logger;

    public NestJobWorkerService(NestJobWorker worker, WorkerOptions options, ILogger<NestJobWorkerService> logger)
    {
        _worker = worker;
        _options = options;
        _logger = logger;
        _worker.Alive = Touch;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Nest worker {Id} polling {Server}.", _worker.WorkerId, _options.ServerUrl);
        while (!stoppingToken.IsCancellationRequested)
        {
            Touch();
            var ran = false;
            try
            {
                ran = await _worker.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("Nest server unavailable: {Message}", ex.Message);
            }

            if (ran)
                continue;
            try { await Task.Delay(_options.PollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Touch()
    {
        try { File.WriteAllText(_options.AliveFile, DateTime.UtcNow.ToString("o")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
