namespace OpenNest.Data;

/// <summary>
/// Loads the details of the nest highlighted in the Database-mode nest browser. Moving
/// the highlight starts a new load and cancels the previous one; a superseded load's
/// result or failure is discarded, so details never belong to a nest that is no longer
/// highlighted. Call from one thread (the UI thread).
/// </summary>
public sealed class NestDetailsSession : IDisposable
{
    private readonly Func<Guid, CancellationToken, Task<NestDetails>> _load;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _disposed;

    /// <param name="load">
    /// Builds the details of one nest; throws when the nest cannot be read (for example
    /// when it no longer exists on the server).
    /// </param>
    public NestDetailsSession(Func<Guid, CancellationToken, Task<NestDetails>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
    }

    /// <summary>The nest of the latest load; null before the first load and after <see cref="Clear"/>.</summary>
    public Guid? NestId { get; private set; }

    /// <summary>Details of <see cref="NestId"/>; null while loading, after a failure and after <see cref="Clear"/>.</summary>
    public NestDetails? Details { get; private set; }

    /// <summary>True while the latest load is outstanding; superseded loads do not count.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>
    /// Loads one nest's details. Returns true when they were applied, false when a later
    /// load or <see cref="Clear"/> superseded this one. A failure of the latest load
    /// propagates; failures of superseded loads are ignored.
    /// </summary>
    public async Task<bool> LoadAsync(Guid id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Supersede before cancelling: a load that completes synchronously when cancelled
        // must already see itself as superseded. Each source is released by its own load.
        var cancellation = Supersede();
        var generation = _generation;
        NestId = id;
        IsLoading = true;

        try
        {
            var details = await _load(id, cancellation.Token);
            if (generation != _generation)
                return false;

            Details = details;
            return true;
        }
        catch (Exception) when (generation != _generation)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(_pending, cancellation))
                _pending = null;
            if (generation == _generation)
                IsLoading = false;
            cancellation.Dispose();
        }
    }

    /// <summary>Forgets the current details and discards any load in flight (no nest highlighted).</summary>
    public void Clear()
    {
        if (_disposed)
            return;

        _generation++;
        var pending = _pending;
        _pending = null;
        NestId = null;
        Details = null;
        IsLoading = false;
        pending?.Cancel();
    }

    private CancellationTokenSource Supersede()
    {
        var previous = _pending;
        var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        _generation++;
        Details = null;
        previous?.Cancel();
        return cancellation;
    }

    /// <summary>Cancels any load in flight (which releases its own source when it ends).</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        _disposed = true;
    }
}
