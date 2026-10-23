namespace OpenNest.Data;

/// <summary>
/// Browse state for Nest &gt; Drawing Sets: the requested search, archived toggle, sort and
/// page, and the last page applied. Paging, superseding and disposal follow
/// <see cref="BrowseSession{TPage, TSort}"/>. Call from one thread (the UI thread).
/// </summary>
public sealed class DrawingSetBrowseSession : BrowseSession<DrawingSetPage, DrawingSetSortField>
{
    private readonly IDrawingSetStore _sets;

    public DrawingSetBrowseSession(IDrawingSetStore sets, int pageSize = DrawingSetQuery.DefaultLimit)
        : base(pageSize, DrawingSetQuery.MaxLimit, DrawingSetSortField.SavedAt)
    {
        ArgumentNullException.ThrowIfNull(sets);
        _sets = sets;
    }

    /// <summary>True when archived sets are listed with the active ones.</summary>
    public bool IncludeArchived { get; private set; }

    /// <inheritdoc />
    public override string Summary =>
        PageSummary("sets", Search.Length > 0 ? "No sets match the search." : "No drawing sets yet. Choose New Set to make one.");

    /// <summary>Lists or hides archived sets and returns to the first page.</summary>
    public Task<bool> SetIncludeArchivedAsync(bool includeArchived)
    {
        IncludeArchived = includeArchived;
        return FirstPageAsync();
    }

    protected override Task<DrawingSetPage> QueryAsync(CancellationToken cancellationToken) =>
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
}
