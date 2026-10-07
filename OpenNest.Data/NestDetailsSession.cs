namespace OpenNest.Data;

/// <summary>
/// Loads the details of the nest highlighted in the Database-mode nest browser. Moving
/// the highlight starts a new load and cancels the previous one; a superseded load's
/// result or failure is discarded, so details never belong to a nest that is no longer
/// highlighted. Also holds a small LRU cache of recently shown or <see cref="Prefetch"/>ed
/// nests, so browsing back onto one, or onto one a caller warmed ahead of time, resolves
/// without a server round trip. Call from one thread (the UI thread).
/// </summary>
public sealed class NestDetailsSession : IDisposable
{
    // Bounds memory from cached plate/drawing geometry; generous enough for a caller's
    // prefetch window plus some scrollback before the oldest entries are evicted.
    private const int CacheCapacity = 25;

    private readonly Func<Guid, CancellationToken, Task<NestDetails>> _load;
    private readonly Dictionary<Guid, NestDetails> _cache = new();
    private readonly List<Guid> _cacheOrder = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _prefetches = new();
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
            NestDetails details;
            if (_cache.TryGetValue(id, out var cached))
            {
                Touch(id);
                details = cached;
            }
            else
            {
                details = await _load(id, cancellation.Token);
                Remember(id, details);
            }

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

    /// <summary>
    /// Starts loading one nest's details in the background so a later <see cref="LoadAsync"/>
    /// for it resolves from the cache instead of the server. Skipped when the nest is already
    /// cached, already prefetching, or is the loaded/loading nest. Failures are swallowed here;
    /// an on-demand load surfaces the error if the nest is actually selected later.
    /// </summary>
    public void Prefetch(Guid id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (id == NestId || _cache.ContainsKey(id) || _prefetches.ContainsKey(id))
            return;

        var cancellation = new CancellationTokenSource();
        _prefetches[id] = cancellation;
        _ = PrefetchCoreAsync(id, cancellation);
    }

    private async Task PrefetchCoreAsync(Guid id, CancellationTokenSource cancellation)
    {
        try
        {
            var details = await _load(id, cancellation.Token);
            if (!cancellation.IsCancellationRequested)
                Remember(id, details);
        }
        catch
        {
            // Silent: this prefetch only mattered if the nest is later selected, and
            // LoadAsync's own on-demand fetch will surface the error then.
        }
        finally
        {
            if (ReferenceEquals(_prefetches.GetValueOrDefault(id), cancellation))
                _prefetches.Remove(id);
            cancellation.Dispose();
        }
    }

    private void Remember(Guid id, NestDetails details)
    {
        _cache[id] = details;
        Touch(id);
        while (_cacheOrder.Count > CacheCapacity)
        {
            var oldest = _cacheOrder[0];
            _cacheOrder.RemoveAt(0);
            _cache.Remove(oldest);
        }
    }

    private void Touch(Guid id)
    {
        _cacheOrder.Remove(id);
        _cacheOrder.Add(id);
    }

    /// <summary>Forgets the current details and discards any load or prefetch in flight.</summary>
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
        foreach (var prefetch in _prefetches.Values)
            prefetch.Cancel();
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
