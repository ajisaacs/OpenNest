namespace OpenNest.Data;

/// <summary>
/// Browse state for Add from Drawing Library: the requested search, sort, page and
/// nest filter, and the last page applied. By default only drawings with the nest's
/// material, grade and thickness are listed; <see cref="SetShowAllAsync"/> lifts that
/// filter. Paging, superseding and disposal follow <see cref="BrowseSession{TPage, TSort}"/>.
/// Call from one thread (the UI thread).
/// </summary>
public sealed class DrawingLibraryBrowseSession : BrowseSession<DrawingLibraryPage, DrawingSortField>
{
    private readonly IDrawingLibrary _library;

    /// <param name="nest">The nest to filter to, or null to list every active drawing (File &gt; Drawing Library).</param>
    public DrawingLibraryBrowseSession(IDrawingLibrary library, Nest? nest, int pageSize = DrawingLibraryQuery.DefaultLimit)
        : base(pageSize, DrawingLibraryQuery.MaxLimit, DrawingSortField.SavedAt)
    {
        ArgumentNullException.ThrowIfNull(library);

        _library = library;
        MaterialName = (nest?.Material?.Name ?? "").Trim();
        MaterialGrade = (nest?.Material?.Grade ?? "").Trim();
        // A nest without a thickness has nothing to match; it filters by material only.
        Thickness = nest != null && double.IsFinite(nest.Thickness) && nest.Thickness > 0
            ? DrawingLibraryRules.NormalizeThickness(nest.Thickness)
            : null;
    }

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

    /// <inheritdoc />
    public override string Summary =>
        PageSummary("drawings",
            Search.Length > 0 ? "No drawings match the search."
            : IsFilteredToNest ? "No library drawings match this nest's material and thickness. Tick Show all materials to see every drawing."
            : "No drawings in the library.");

    /// <summary>Lifts or restores the nest filter and returns to the first page.</summary>
    public Task<bool> SetShowAllAsync(bool showAll)
    {
        ShowAll = showAll;
        return FirstPageAsync();
    }

    protected override Task<DrawingLibraryPage> QueryAsync(CancellationToken cancellationToken) =>
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
}
