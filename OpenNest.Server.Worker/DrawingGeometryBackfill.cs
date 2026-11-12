using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Server.Worker;

/// <summary>
/// Measures library drawing packages the server lists as unmeasured and stores each
/// <see cref="DrawingGeometrySummary"/>, so the library can be filtered by size and holes.
/// A package that cannot be read or measured is stored as failed and not tried again; a
/// server or network failure stores nothing, so the package is tried on a later round.
/// </summary>
public sealed class DrawingGeometryBackfill
{
    public const int BatchSize = 100;

    // A pathological drawing must not stall the rest; its worker thread is abandoned.
    private static readonly TimeSpan MeasureTimeout = TimeSpan.FromSeconds(30);

    private readonly RemoteDrawingGeometry _server;
    private readonly ILogger _logger;

    public DrawingGeometryBackfill(RemoteDrawingGeometry server, ILogger<DrawingGeometryBackfill> logger)
    {
        _server = server;
        _logger = logger;
    }

    /// <summary>Measures one batch; returns how many summaries were stored (0 when none were waiting).</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var hashes = await _server.GetUnmeasuredAsync(DrawingGeometrySummary.CurrentVersion, BatchSize, cancellationToken);
        var stored = 0;
        foreach (var sha in hashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var package = await _server.GetPackageAsync(sha, cancellationToken);
            if (package is null)
                continue;
            var summary = await MeasureAsync(package, cancellationToken);
            if (!summary.IsMeasured)
                _logger.LogInformation("Library package {Sha} could not be measured: {Error}", sha, summary.Error);
            if (await _server.PutAsync(sha, summary, cancellationToken))
                stored++;
        }
        return stored;
    }

    /// <summary>Reads and measures one package; a failure becomes a failed summary.</summary>
    public static async Task<DrawingGeometrySummary> MeasureAsync(byte[] package, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() =>
            {
                var contents = DrawingPackage.Read(package);
                return DrawingGeometrySummary.Measure(contents.Drawing, contents.Units);
            }, cancellationToken).WaitAsync(MeasureTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return DrawingGeometrySummary.Failed($"Measuring took over {MeasureTimeout.TotalSeconds:0} s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DrawingGeometrySummary.Failed(ex.Message);
        }
    }
}

/// <summary>
/// Runs <see cref="DrawingGeometryBackfill"/> beside the nest job worker: batch after batch
/// while packages are waiting, then every <see cref="WorkerOptions.GeometryPollInterval"/>.
/// </summary>
public sealed class DrawingGeometryBackfillService : BackgroundService
{
    private readonly DrawingGeometryBackfill _backfill;
    private readonly WorkerOptions _options;
    private readonly ILogger<DrawingGeometryBackfillService> _logger;

    public DrawingGeometryBackfillService(DrawingGeometryBackfill backfill, WorkerOptions options,
        ILogger<DrawingGeometryBackfillService> logger)
    {
        _backfill = backfill;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var total = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var stored = 0;
            try
            {
                stored = await _backfill.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (ServerTooOldException)
            {
                _logger.LogWarning("The nest server does not measure library drawings; update it to filter the library by size.");
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("Nest server unavailable for library measurement: {Message}", ex.Message);
            }

            if (stored > 0)
            {
                total += stored;
                _logger.LogInformation("Measured {Count} library drawing package(s) so far.", total);
                continue;
            }
            try { await Task.Delay(_options.GeometryPollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
