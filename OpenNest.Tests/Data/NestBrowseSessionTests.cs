using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class NestBrowseSessionTests
{
    [Fact]
    public async Task Refresh_RequestsTheFirstBoundedPageNewestSavedFirst()
    {
        var repository = new PagedRepository(25);
        using var session = new NestBrowseSession(repository, pageSize: 10);

        Assert.True(await session.RefreshAsync());

        var query = Assert.Single(repository.Queries);
        Assert.Equal("", query.Search);
        Assert.Equal(NestSortField.SavedAt, query.Sort);
        Assert.True(query.Descending);
        Assert.Equal(0, query.Offset);
        Assert.Equal(10, query.Limit);
        Assert.Equal(10, session.Page!.Items.Count);
        Assert.Equal("Showing 1-10 of 25 nests", session.Summary);
    }

    [Fact]
    public async Task NextAndPrevious_StayWithinTheMatches()
    {
        var repository = new PagedRepository(25);
        using var session = new NestBrowseSession(repository, pageSize: 10);
        await session.RefreshAsync();
        Assert.False(session.CanGoPrevious);
        Assert.True(session.CanGoNext);

        Assert.True(await session.NextPageAsync());
        Assert.Equal("Showing 11-20 of 25 nests", session.Summary);
        Assert.True(await session.NextPageAsync());
        Assert.Equal("Showing 21-25 of 25 nests", session.Summary);
        Assert.False(session.CanGoNext);
        Assert.True(session.CanGoPrevious);

        var sent = repository.Queries.Count;
        Assert.False(await session.NextPageAsync());
        Assert.Equal(sent, repository.Queries.Count);

        Assert.True(await session.PreviousPageAsync());
        Assert.Equal(10, repository.Queries[^1].Offset);
        Assert.Equal("Showing 11-20 of 25 nests", session.Summary);
    }

    [Fact]
    public async Task SetSearch_TrimsTextAndReturnsToTheFirstPage()
    {
        var repository = new PagedRepository(25);
        using var session = new NestBrowseSession(repository, pageSize: 10);
        await session.RefreshAsync();
        await session.NextPageAsync();

        await session.SetSearchAsync("  beta works ");

        var query = repository.Queries[^1];
        Assert.Equal("beta works", query.Search);
        Assert.Equal(0, query.Offset);
        Assert.Equal("beta works", session.Search);
    }

    [Fact]
    public async Task SortBy_NewColumnAscends_SameColumnReverses_AndResetsTheOffset()
    {
        var repository = new PagedRepository(25);
        using var session = new NestBrowseSession(repository, pageSize: 10);
        await session.RefreshAsync();
        await session.NextPageAsync();

        await session.SortByAsync(NestSortField.Customer);
        Assert.Equal((NestSortField.Customer, false, 0), Last(repository));

        await session.SortByAsync(NestSortField.Customer);
        Assert.Equal((NestSortField.Customer, true, 0), Last(repository));

        await session.SortByAsync(NestSortField.Thickness);
        Assert.Equal((NestSortField.Thickness, false, 0), Last(repository));

        await session.SortByAsync(NestSortField.SavedAt);
        await session.SortByAsync(NestSortField.SavedAt);
        Assert.Equal((NestSortField.SavedAt, true, 0), Last(repository));
    }

    [Fact]
    public async Task OlderResponse_ArrivingLast_NeverReplacesTheNewerResult()
    {
        var repository = new ControlledRepository();
        using var session = new NestBrowseSession(repository);
        var older = session.SetSearchAsync("a");
        var newer = session.SetSearchAsync("ab");
        var newerPage = Page("ab result");

        repository.Requests[1].Reply.SetResult(newerPage);
        Assert.True(await newer);
        repository.Requests[0].Reply.SetResult(Page("a result"));
        Assert.False(await older);

        Assert.Same(newerPage, session.Page);
        Assert.True(repository.Requests[0].Token.IsCancellationRequested);
        Assert.False(repository.Requests[1].Token.IsCancellationRequested);
    }

    [Fact]
    public async Task SupersededFailure_IsIgnored_LatestFailure_ClearsThePageAndPropagates()
    {
        var repository = new ControlledRepository();
        using var session = new NestBrowseSession(repository);
        var older = session.SetSearchAsync("a");
        var newer = session.SetSearchAsync("ab");

        repository.Requests[0].Reply.SetException(new IOException("superseded failure"));
        Assert.False(await older);
        repository.Requests[1].Reply.SetResult(Page("ab result"));
        Assert.True(await newer);
        Assert.NotNull(session.Page);

        var failing = session.RefreshAsync();
        repository.Requests[2].Reply.SetException(new IOException("server offline"));
        var error = await Assert.ThrowsAsync<IOException>(() => failing);

        Assert.Equal("server offline", error.Message);
        Assert.Null(session.Page);
        Assert.Equal("", session.Summary);
    }

    [Fact]
    public async Task Refresh_AfterTheLastPageWasEmptied_StepsBackToTheNewLastPage()
    {
        var repository = new PagedRepository(21);
        using var session = new NestBrowseSession(repository, pageSize: 10);
        await session.RefreshAsync();
        await session.NextPageAsync();
        await session.NextPageAsync();
        Assert.Equal("Showing 21-21 of 21 nests", session.Summary);

        repository.RemoveLast();
        Assert.True(await session.RefreshAsync());

        Assert.Equal(new[] { 20, 10 }, repository.Queries.TakeLast(2).Select(q => q.Offset));
        Assert.Equal(10, session.Offset);
        Assert.Equal("Showing 11-20 of 20 nests", session.Summary);
    }

    [Fact]
    public async Task SupersededRequest_CompletingSynchronouslyOnCancellation_IsNotReportedAsTheLatestFailure()
    {
        var repository = new ControlledRepository { CompleteOnCancellation = true };
        using var session = new NestBrowseSession(repository);
        var older = session.SetSearchAsync("a");
        var newer = session.SetSearchAsync("ab");

        Assert.False(await older);
        repository.Requests[1].Reply.SetResult(Page("ab result"));
        Assert.True(await newer);

        Assert.Equal("ab result", Assert.Single(session.Page!.Items).Name);
    }

    [Fact]
    public async Task IsLoading_FollowsOnlyTheLatestRequest()
    {
        var repository = new ControlledRepository();
        using var session = new NestBrowseSession(repository, pageSize: 1);
        Assert.False(session.IsLoading);

        // A superseded request finishing first leaves the latest one loading.
        var first = session.SetSearchAsync("a");
        var second = session.SetSearchAsync("ab");
        repository.Requests[0].Reply.SetResult(Page("a result"));
        Assert.False(await first);
        Assert.True(session.IsLoading);
        repository.Requests[1].Reply.SetResult(Page("ab result"));
        Assert.True(await second);
        Assert.False(session.IsLoading);

        // The latest request finishing first ends loading although a superseded one is still out.
        var third = session.SetSearchAsync("abc");
        var fourth = session.SetSearchAsync("abcd");
        var record = new NestRecord { Id = Guid.NewGuid(), Name = "abcd result" };
        repository.Requests[3].Reply.SetResult(new NestPage { Items = new[] { record }, Total = 3, Limit = 1 });
        Assert.True(await fourth);
        Assert.False(session.IsLoading);
        Assert.True(session.CanGoNext);
        repository.Requests[2].Reply.SetResult(Page("abc result"));
        Assert.False(await third);
        Assert.False(session.IsLoading);

        var failing = session.RefreshAsync();
        Assert.True(session.IsLoading);
        repository.Requests[4].Reply.SetException(new IOException("offline"));
        await Assert.ThrowsAsync<IOException>(() => failing);
        Assert.False(session.IsLoading);
    }

    [Fact]
    public async Task EachRequestsCancellationSource_IsReleasedWhenThatRequestEnds()
    {
        var repository = new ControlledRepository();
        var session = new NestBrowseSession(repository);
        var older = session.SetSearchAsync("a");
        var newer = session.SetSearchAsync("ab");

        repository.Requests[1].Reply.SetResult(Page("ab"));
        await newer;
        Assert.Throws<ObjectDisposedException>(() => repository.Requests[1].Token.WaitHandle);
        repository.Requests[0].Reply.SetResult(Page("a"));
        await older;
        Assert.Throws<ObjectDisposedException>(() => repository.Requests[0].Token.WaitHandle);

        var pending = session.RefreshAsync();
        session.Dispose();
        repository.Requests[2].Reply.SetResult(Page("late"));
        await pending;
        Assert.Throws<ObjectDisposedException>(() => repository.Requests[2].Token.WaitHandle);
    }

    [Theory]
    [InlineData("", "No nests on the server.")]
    [InlineData("no such text", "No nests match the filter.")]
    public async Task Summary_DistinguishesAnEmptyServerFromNoMatches(string search, string expected)
    {
        using var session = new NestBrowseSession(new PagedRepository(0));

        await session.SetSearchAsync(search);

        Assert.Equal(expected, session.Summary);
    }

    [Fact]
    public async Task Dispose_CancelsTheRequestAndDiscardsItsLateResponse()
    {
        var repository = new ControlledRepository();
        var session = new NestBrowseSession(repository);
        var pending = session.SetSearchAsync("a");

        session.Dispose();
        repository.Requests[0].Reply.SetResult(Page("late"));

        Assert.True(repository.Requests[0].Token.IsCancellationRequested);
        Assert.False(await pending);
        Assert.Null(session.Page);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.RefreshAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(NestQuery.MaxLimit + 1)]
    public void Constructor_RejectsPageSizesTheServerWouldRefuse(int pageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NestBrowseSession(new PagedRepository(0), pageSize));
    }

    private static (NestSortField, bool, int) Last(PagedRepository repository)
    {
        var query = repository.Queries[^1];
        return (query.Sort, query.Descending, query.Offset);
    }

    private static NestPage Page(string name) =>
        new() { Items = new[] { new NestRecord { Id = Guid.NewGuid(), Name = name } }, Total = 1, Limit = 100 };

    /// <summary>Serves pages of an in-memory set and records each query.</summary>
    private sealed class PagedRepository : RepositoryBase
    {
        private readonly List<NestRecord> _records;

        public PagedRepository(int count) =>
            _records = Enumerable.Range(0, count).Select(i => new NestRecord { Id = Guid.NewGuid(), Name = $"Nest {i}" }).ToList();

        public List<NestQuery> Queries { get; } = new();

        public void RemoveLast() => _records.RemoveAt(_records.Count - 1);

        public override Task<NestPage> QueryAsync(NestQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var matches = query.NormalizedSearch.Length == 0 ? _records : new List<NestRecord>();
            return Task.FromResult(new NestPage
            {
                Items = matches.Skip(query.Offset).Take(query.Limit).ToArray(),
                Total = matches.Count,
                Offset = query.Offset,
                Limit = query.Limit,
            });
        }
    }

    /// <summary>
    /// Leaves every query pending until the test completes it. With
    /// <see cref="CompleteOnCancellation"/>, cancellation completes the query synchronously
    /// inside the canceller's call, as some HTTP handlers do.
    /// </summary>
    private sealed class ControlledRepository : RepositoryBase
    {
        public bool CompleteOnCancellation { get; init; }

        public List<(NestQuery Query, TaskCompletionSource<NestPage> Reply, CancellationToken Token)> Requests { get; } = new();

        public override Task<NestPage> QueryAsync(NestQuery query, CancellationToken cancellationToken = default)
        {
            var reply = new TaskCompletionSource<NestPage>();
            if (CompleteOnCancellation)
                cancellationToken.Register(() => reply.TrySetCanceled(cancellationToken));
            Requests.Add((query, reply, cancellationToken));
            return reply.Task;
        }
    }

    private abstract class RepositoryBase : INestRepository
    {
        public abstract Task<NestPage> QueryAsync(NestQuery query, CancellationToken cancellationToken = default);

        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UploadAsync(byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
