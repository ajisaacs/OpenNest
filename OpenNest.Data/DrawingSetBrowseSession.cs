using System.Globalization;

namespace OpenNest.Data;

/// <summary>
/// Browse state for Nest &gt; Drawing Sets: the requested search, archived toggle, sort and
/// page, and the last page applied. Every change issues one bounded server query. A response
/// that a later request superseded is discarded (and the earlier request is cancelled), so an
/// older result can never replace a newer one. Call from one thread (the UI thread).
/// </summary>
public sealed class DrawingSetBrowseSession : IDisposable
{
    private readonly IDrawingSetStore _sets;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _disposed;

    public DrawingSetBrowseSession(IDrawingSetStore sets, int pageSize = DrawingSetQuery.DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(sets);
        if (pageSize < 1 || pageSize > DrawingSetQuery.MaxLimit)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"Page size must be 1 to {DrawingSetQuery.MaxLimit}.");

        _sets = sets;
        PageSize = pageSize;
    }

    public int PageSize { get; }

    /// <summary>Trimmed search text of the latest request.</summary>
    public string Search { get; private set; } = "";

    /// <summary>True when archived sets are listed with the active ones.</summary>
    public bool IncludeArchived { get; private set; }

    public DrawingSetSortField Sort { get; private set; } = DrawingSetSortField.SavedAt;

    public bool Descending { get; private set; } = true;

    /// <summary>Offset of the latest request.</summary>
    public int Offset { get; private set; }

    /// <summary>The latest applied page; null before the first result and after a failed request.</summary>
    public DrawingSetPage? Page { get; private set; }

    /// <summary>True while the latest request is outstanding; superseded requests do not count.</summary>
    public bool IsLoading { get; private set; }

    public bool CanGoPrevious => Offset > 0;

    public bool CanGoNext => Page is { } page && page.Offset + page.Items.Count < page.Total;

    /// <summary>Operator-facing summary of <see cref="Page"/>, such as "Showing 1-100 of 240 sets".</summary>
    public string Summary
    {
        get
        {
            if (Page is not { } page)
                return "";
            if (page.Total == 0)
                return Search.Length > 0 ? "No sets match the search." : "No drawing sets yet. Choose New Set to make one.";
            if (page.Items.Count == 0)
                return $"No sets on this page ({Count(page.Total)} in total).";

            return $"Showing {Count(page.Offset + 1)}-{Count(page.Offset + page.Items.Count)} of {Count(page.Total)} sets";
        }
    }

    /// <summary>Applies new search text and returns to the first page.</summary>
    public Task<bool> SetSearchAsync(string? search)
    {
        Search = (search ?? "").Trim();
        Offset = 0;
        return RunAsync();
    }

    /// <summary>Lists or hides archived sets and returns to the first page.</summary>
    public Task<bool> SetIncludeArchivedAsync(bool includeArchived)
    {
        IncludeArchived = includeArchived;
        Offset = 0;
        return RunAsync();
    }

    /// <summary>Sorts by a column: a new column starts ascending, the same column reverses.</summary>
    public Task<bool> SortByAsync(DrawingSetSortField field)
    {
        Descending = field == Sort && !Descending;
        Sort = field;
        Offset = 0;
        return RunAsync();
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

    /// <summary>
    /// Sends the current request. Returns true when its page was applied, false when a later
    /// request superseded it. Failures of the latest request clear <see cref="Page"/> and propagate;
    /// failures of superseded requests are ignored.
    /// </summary>
    private async Task<bool> RunAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Supersede before cancelling: a request that completes synchronously when cancelled
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

            // An archive or another PC can leave the requested page past the end.
            if (page.Items.Count == 0 && page.Offset > 0 && page.Total > 0)
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

    private Task<DrawingSetPage> QueryAsync(CancellationToken cancellationToken) =>
        _sets.QueryAsync(
            new DrawingSetQuery
            {
                Search = Search,
                IncludeArchived = IncludeArchived,
                Sort = Sort,
                Descending = Descending,
                Offset = Offset,
                Limit = PageSize,
            },
            cancellationToken);

    private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Cancels any request in flight (which releases its own source when it ends);
    /// the set client is not owned and stays open.
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
