namespace OpenNest.Data;

/// <summary>
/// Browse state for the Database-mode Open dialog: the requested search, sort and page,
/// and the last page applied. Paging, superseding and disposal follow
/// <see cref="BrowseSession{TPage, TSort}"/>. Call from one thread (the UI thread).
/// </summary>
public sealed class NestBrowseSession : BrowseSession<NestPage, NestSortField>
{
    private readonly INestRepository _repository;

    public NestBrowseSession(INestRepository repository, int pageSize = NestQuery.DefaultLimit,
        NestSortField sort = NestSortField.SavedAt, bool descending = true)
        : base(pageSize, NestQuery.MaxLimit, Enum.IsDefined(sort) ? sort : NestSortField.SavedAt, descending)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <inheritdoc />
    public override string Summary =>
        PageSummary("nests", Search.Length == 0 ? "No nests on the server." : "No nests match the filter.");

    protected override Task<NestPage> QueryAsync(CancellationToken cancellationToken) =>
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
}
