using System.Globalization;

namespace OpenNest.Data;

/// <summary>The paging facts a <see cref="BrowseSession{TPage, TSort}"/> reads from a server page.</summary>
public interface IBrowsePage
{
    int Offset { get; }

    /// <summary>Number of records matching the request across all pages.</summary>
    int Total { get; }

    /// <summary>Number of records on this page.</summary>
    int ItemCount { get; }
}

/// <summary>
/// Browse state shared by the paged server lists: the requested search, sort and page, and the
/// last page applied. Every change issues one bounded server query. A response that a later
/// request superseded is discarded (and the earlier request is canceled), so an older result
/// can never replace a newer one. Call from one thread (the UI thread).
/// </summary>
public abstract class BrowseSession<TPage, TSort> : IDisposable
    where TPage : class, IBrowsePage
    where TSort : struct, Enum
{
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _disposed;

    protected BrowseSession(int pageSize, int maxPageSize, TSort sort, bool descending = true)
    {
        if (pageSize < 1 || pageSize > maxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"Page size must be 1 to {maxPageSize}.");

        PageSize = pageSize;
        Sort = sort;
        Descending = descending;
    }

    public int PageSize { get; }

    /// <summary>Trimmed search text of the latest request.</summary>
    public string Search { get; private set; } = "";

    public TSort Sort { get; private set; }

    public bool Descending { get; private set; }

    /// <summary>Offset of the latest request.</summary>
    public int Offset { get; private set; }

    /// <summary>The latest applied page; null before the first result and after a failed request.</summary>
    public TPage? Page { get; private set; }

    /// <summary>True while the latest request is outstanding; superseded requests do not count.</summary>
    public bool IsLoading { get; private set; }

    public bool CanGoPrevious => Offset > 0;

    public bool CanGoNext => Page is { } page && page.Offset + page.ItemCount < page.Total;

    /// <summary>Operator-facing summary of <see cref="Page"/>, such as "Showing 101-200 of 1,234 nests".</summary>
    public abstract string Summary { get; }

    /// <summary>Applies new search text and returns to the first page.</summary>
    public Task<bool> SetSearchAsync(string? search)
    {
        Search = (search ?? "").Trim();
        return FirstPageAsync();
    }

    /// <summary>Sorts by a column: a new column starts ascending, the same column reverses.</summary>
    public Task<bool> SortByAsync(TSort field)
    {
        Descending = EqualityComparer<TSort>.Default.Equals(field, Sort) && !Descending;
        Sort = field;
        return FirstPageAsync();
    }

    public Task<bool> NextPageAsync()
    {
        if (!CanGoNext)
            return Task.FromResult(false);

        Offset = Page!.Offset + PageSize;
        return RunAsync();
    }

    public Task<bool> PreviousPageAsync()
    {
        if (!CanGoPrevious)
            return Task.FromResult(false);

        Offset = System.Math.Max(0, Offset - PageSize);
        return RunAsync();
    }

    /// <summary>Re-reads the current page; steps back to the last page if it is now past the end.</summary>
    public Task<bool> RefreshAsync() => RunAsync();

    /// <summary>Re-queries from the first page, after a derived filter changed.</summary>
    protected Task<bool> FirstPageAsync()
    {
        Offset = 0;
        return RunAsync();
    }

    /// <summary>Queries the page at <see cref="Offset"/> with the current filters.</summary>
    protected abstract Task<TPage> QueryAsync(CancellationToken cancellationToken);

    /// <summary>The standard summary: <paramref name="empty"/> when nothing matches, else the shown range of <paramref name="noun"/>.</summary>
    protected string PageSummary(string noun, string empty)
    {
        if (Page is not { } page)
            return "";
        if (page.Total == 0)
            return empty;
        if (page.ItemCount == 0)
            return $"No {noun} on this page ({Count(page.Total)} in total).";

        return $"Showing {Count(page.Offset + 1)}-{Count(page.Offset + page.ItemCount)} of {Count(page.Total)} {noun}";
    }

    /// <summary>
    /// Sends the current request. Returns true when its page was applied, false when a later
    /// request superseded it. Failures of the latest request clear <see cref="Page"/> and propagate;
    /// failures of superseded requests are ignored.
    /// </summary>
    private async Task<bool> RunAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Supersede before canceling: a request that completes synchronously when canceled
        // must already see itself as superseded. Each source is released by its own request.
        var previous = _pending;
        using var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        var generation = ++_generation;
        IsLoading = true;

        try
        {
            previous?.Cancel();
            var page = await QueryAsync(cancellation.Token);
            if (generation != _generation)
                return false;

            // A deletion, an archive or another PC can leave the requested page past the end.
            if (page.ItemCount == 0 && page.Offset > 0 && page.Total > 0)
            {
                Offset = (page.Total - 1) / PageSize * PageSize;
                page = await QueryAsync(cancellation.Token);
                if (generation != _generation)
                    return false;
            }

            Page = page;
            return true;
        }
        catch (Exception) when (generation != _generation)
        {
            return false;
        }
        catch
        {
            Page = null;
            throw;
        }
        finally
        {
            if (ReferenceEquals(_pending, cancellation))
                _pending = null;
            if (generation == _generation)
                IsLoading = false;
        }
    }

    private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Cancels any request in flight (which releases its own source when it ends);
    /// the server client is not owned and stays open.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _generation++;
        IsLoading = false;
        var pending = _pending;
        _pending = null;
        pending?.Cancel();
    }
}
