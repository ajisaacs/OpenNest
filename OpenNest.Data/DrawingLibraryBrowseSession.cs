using System.Globalization;
using OpenNest;

namespace OpenNest.Data;

/// <summary>
/// Browse state for Add from Drawing Library: the requested search, sort, page and
/// nest filter, and the last page applied. By default only drawings with the nest's
/// material, grade and thickness are listed; <see cref="SetShowAllAsync"/> lifts that
/// filter. Every change issues one bounded server query. A response that a later request
/// superseded is discarded (and the earlier request is cancelled), so an older result can
/// never replace a newer one. Call from one thread (the UI thread).
/// </summary>
public sealed class DrawingLibraryBrowseSession : IDisposable
{
    private readonly IDrawingLibrary _library;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _disposed;

    public DrawingLibraryBrowseSession(IDrawingLibrary library, Nest nest, int pageSize = DrawingLibraryQuery.DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(nest);
        if (pageSize < 1 || pageSize > DrawingLibraryQuery.MaxLimit)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"Page size must be 1 to {DrawingLibraryQuery.MaxLimit}.");

        _library = library;
        PageSize = pageSize;
        MaterialName = (nest.Material?.Name ?? "").Trim();
        MaterialGrade = (nest.Material?.Grade ?? "").Trim();
        // A nest without a thickness has nothing to match; it filters by material only.
        Thickness = double.IsFinite(nest.Thickness) && nest.Thickness > 0
            ? DrawingLibraryRules.NormalizeThickness(nest.Thickness)
            : null;
    }

    public int PageSize { get; }

    /// <summary>The nest's material name, applied unless <see cref="ShowAll"/>; blank means any.</summary>
    public string MaterialName { get; }

    /// <summary>The nest's grade, applied unless <see cref="ShowAll"/>; blank means any.</summary>
    public string MaterialGrade { get; }

    /// <summary>The nest's thickness, applied unless <see cref="ShowAll"/>; null means any.</summary>
    public double? Thickness { get; }

    /// <summary>True when the nest filter is lifted and every active drawing is listed.</summary>
    public bool ShowAll { get; private set; }

    /// <summary>True when the latest request narrows by the nest's material, grade or thickness.</summary>
    public bool IsFilteredToNest =>
        !ShowAll && (MaterialName.Length > 0 || MaterialGrade.Length > 0 || Thickness is not null);

    /// <summary>Trimmed search text of the latest request.</summary>
    public string Search { get; private set; } = "";

    public DrawingSortField Sort { get; private set; } = DrawingSortField.SavedAt;

    public bool Descending { get; private set; } = true;

    /// <summary>Offset of the latest request.</summary>
    public int Offset { get; private set; }

    /// <summary>The latest applied page; null before the first result and after a failed request.</summary>
    public DrawingLibraryPage? Page { get; private set; }

    /// <summary>True while the latest request is outstanding; superseded requests do not count.</summary>
    public bool IsLoading { get; private set; }

    public bool CanGoPrevious => Offset > 0;

    public bool CanGoNext => Page is { } page && page.Offset + page.Items.Count < page.Total;

    /// <summary>Operator-facing summary of <see cref="Page"/>, such as "Showing 101-200 of 1,234 drawings".</summary>
    public string Summary
    {
        get
        {
            if (Page is not { } page)
                return "";
            if (page.Total == 0)
            {
                if (Search.Length > 0)
                    return "No drawings match the search.";
                return IsFilteredToNest
                    ? "No library drawings match this nest's material and thickness. Choose Show all to see every drawing."
                    : "No drawings in the library.";
            }
            if (page.Items.Count == 0)
                return $"No drawings on this page ({Count(page.Total)} in total).";

            return $"Showing {Count(page.Offset + 1)}-{Count(page.Offset + page.Items.Count)} of {Count(page.Total)} drawings";
        }
    }

    /// <summary>Applies new search text and returns to the first page.</summary>
    public Task<bool> SetSearchAsync(string? search)
    {
        Search = (search ?? "").Trim();
        Offset = 0;
        return RunAsync();
    }

    /// <summary>Lifts or restores the nest filter and returns to the first page.</summary>
    public Task<bool> SetShowAllAsync(bool showAll)
    {
        ShowAll = showAll;
        Offset = 0;
        return RunAsync();
    }

    /// <summary>Sorts by a column: a new column starts ascending, the same column reverses.</summary>
    public Task<bool> SortByAsync(DrawingSortField field)
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

    private Task<DrawingLibraryPage> QueryAsync(CancellationToken cancellationToken) =>
        _library.QueryAsync(
            new DrawingLibraryQuery
            {
                Search = Search,
                MaterialName = ShowAll ? "" : MaterialName,
                MaterialGrade = ShowAll ? "" : MaterialGrade,
                Thickness = ShowAll ? null : Thickness,
                Sort = Sort,
                Descending = Descending,
                Offset = Offset,
                Limit = PageSize,
            },
            cancellationToken);

    private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Cancels any request in flight (which releases its own source when it ends);
    /// the library client is not owned and stays open.
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
