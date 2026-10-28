namespace OpenNest.Server;

/// <summary>
/// Refreshes the <see cref="UpdateMirror"/> every <see cref="Interval"/>, and at once when a
/// client asks for a channel the mirror does not hold yet.
/// </summary>
public sealed class UpdateMirrorService : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly UpdateMirror _mirror;
    private readonly SemaphoreSlim _wake = new(0, 1);

    public UpdateMirrorService(UpdateMirror mirror)
    {
        _mirror = mirror;
        _mirror.RefreshRequested += Wake;
    }

    public override void Dispose()
    {
        _mirror.RefreshRequested -= Wake;
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _mirror.RefreshAsync(stoppingToken);
            try { await _wake.WaitAsync(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Wake()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { } // A wake is already pending.
    }
}
