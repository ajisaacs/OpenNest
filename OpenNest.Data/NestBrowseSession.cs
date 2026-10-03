using System.Globalization;

namespace OpenNest.Data;

/// <summary>
/// Browse state for the Database-mode Open dialog: the requested search, sort and page,
/// and the last page applied. Every change issues one bounded server query. A response
/// that a later request superseded is discarded (and the earlier request is cancelled),
/// so an older result can never replace a newer one. Call from one thread (the UI thread).
/// </summary>
public sealed class NestBrowseSession : IDisposable
{
    private readonly INestRepository _repository;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _disposed;

    public NestBrowseSession(INestRepository repository, int pageSize = NestQuery.DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (pageSize < 1 || pageSize > NestQuery.MaxLimit)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"Page size must be 1 to {NestQuery.MaxLimit}.");

        _repository = repository;
        PageSize = pageSize;
    }

    public int PageSize { get; }

    /// <summary>Trimmed search text of the latest request.</summary>
    public string Search { get; private set; } = "";

    public NestSortField Sort { get; private set; } = NestSortField.SavedAt;

    public bool Descending { get; private set; } = true;

    /// <summary>Offset of the latest request.</summary>
    public int Offset { get; private set; }

    /// <summary>The latest applied page; null before the first result and after a failed request.</summary>
    public NestPage? Page { get; private set; }

    public bool CanGoPrevious => Offset > 0;

    public bool CanGoNext => Page is { } page && page.Offset + page.Items.Count < page.Total;

    /// <summary>Operator-facing summary of <see cref="Page"/>, such as "Showing 101-200 of 1,234 nests".</summary>
    public string Summary
    {
        get
        {
            if (Page is not { } page)
                return "";
            if (page.Total == 0)
                return Search.Length == 0 ? "No nests on the server." : "No nests match the filter.";
            if (page.Items.Count == 0)
                return $"No nests on this page ({Count(page.Total)} in total).";

            return $"Showing {Count(page.Offset + 1)}-{Count(page.Offset + page.Items.Count)} of {Count(page.Total)} nests";
        }
    }

    /// <summary>Applies new search text and returns to the first page.</summary>
    public Task<bool> SetSearchAsync(string? search)
    {
        Search = (search ?? "").Trim();
        Offset = 0;
        return RunAsync();
    }

    /// <summary>Sorts by a column: a new column starts ascending, the same column reverses.</summary>
    public Task<bool> SortByAsync(NestSortField field)
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
        // Superseded sources are cancelled but not disposed: their request may still observe the token.
        _pending?.Cancel();
        var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        var generation = ++_generation;

        try
        {
            var page = await QueryAsync(cancellation.Token);
            if (generation != _generation)
                return false;

            // A deletion or another PC can leave the requested page past the end.
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
    }

    private Task<NestPage> QueryAsync(CancellationToken cancellationToken) =>
        _repository.QueryAsync(
            new NestQuery
            {
                Search = Search,
                Sort = Sort,
                Descending = Descending,
                Offset = Offset,
                Limit = PageSize,
            },
            cancellationToken);

    private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Cancels any request in flight; the repository is not owned and stays open.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _generation++;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }
}
