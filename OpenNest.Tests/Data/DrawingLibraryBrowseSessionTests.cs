using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class DrawingLibraryBrowseSessionTests
{
    private static Nest SyntheticNest() => new("Synthetic job")
    {
        Thickness = 0.187504,
        Material = new Material(" Synthetic Steel ", "Grade A"),
    };

    [Fact]
    public async Task Refresh_DefaultsToTheNestsMaterialGradeAndThicknessNewestSavedFirst()
    {
        var library = new PagedLibrary(25);
        using var session = new DrawingLibraryBrowseSession(library, SyntheticNest(), pageSize: 10);

        Assert.True(await session.RefreshAsync());

        var query = Assert.Single(library.Queries);
        Assert.Equal(("Synthetic Steel", "Grade A", (double?)0.1875), (query.MaterialName, query.MaterialGrade, query.Thickness));
        Assert.Equal(("", (CutType?)null, false), (query.Search, query.CutType, query.IncludeArchived));
        Assert.Equal((DrawingSortField.SavedAt, true, 0, 10), (query.Sort, query.Descending, query.Offset, query.Limit));
        Assert.True(session.IsFilteredToNest);
        Assert.Equal("Showing 1-10 of 25 drawings", session.Summary);
    }

    [Fact]
    public async Task ShowAll_LiftsTheNestFilterAndReturnsToTheFirstPage()
    {
        var library = new PagedLibrary(25);
        using var session = new DrawingLibraryBrowseSession(library, SyntheticNest(), pageSize: 10);
        await session.RefreshAsync();
        await session.NextPageAsync();

        await session.SetShowAllAsync(true);

        var all = library.Queries[^1];
        Assert.Equal(("", "", (double?)null, 0), (all.MaterialName, all.MaterialGrade, all.Thickness, all.Offset));
        Assert.False(session.IsFilteredToNest);

        await session.SetShowAllAsync(false);

        var filtered = library.Queries[^1];
        Assert.Equal(("Synthetic Steel", "Grade A", (double?)0.1875), (filtered.MaterialName, filtered.MaterialGrade, filtered.Thickness));
    }

    [Fact]
    public async Task NestWithoutMaterialOrThickness_IsNotFiltered()
    {
        var library = new PagedLibrary(3);
        using var session = new DrawingLibraryBrowseSession(library, new Nest("Blank job") { Material = null! });

        await session.RefreshAsync();

        var query = Assert.Single(library.Queries);
        Assert.Equal(("", "", (double?)null), (query.MaterialName, query.MaterialGrade, query.Thickness));
        Assert.False(session.IsFilteredToNest);
    }

    [Fact]
    public async Task WithoutANest_ListsEveryDrawing()
    {
        var library = new PagedLibrary(3);
        using var session = new DrawingLibraryBrowseSession(library, null);

        await session.RefreshAsync();

        var query = Assert.Single(library.Queries);
        Assert.Equal(("", "", (double?)null), (query.MaterialName, query.MaterialGrade, query.Thickness));
        Assert.False(session.IsFilteredToNest);
    }

    [Fact]
    public async Task SearchSortAndPaging_FollowTheNestBrowseRules()
    {
        var library = new PagedLibrary(25);
        using var session = new DrawingLibraryBrowseSession(library, SyntheticNest(), pageSize: 10);
        await session.RefreshAsync();
        Assert.False(session.CanGoPrevious);

        Assert.True(await session.NextPageAsync());
        Assert.True(await session.NextPageAsync());
        Assert.Equal("Showing 21-25 of 25 drawings", session.Summary);
        Assert.False(session.CanGoNext);
        var sent = library.Queries.Count;
        Assert.False(await session.NextPageAsync());
        Assert.Equal(sent, library.Queries.Count);
        Assert.True(await session.PreviousPageAsync());
        Assert.Equal(10, library.Queries[^1].Offset);

        await session.SetSearchAsync("  bracket ");
        Assert.Equal(("bracket", 0), (library.Queries[^1].Search, library.Queries[^1].Offset));

        await session.SortByAsync(DrawingSortField.Name);
        Assert.Equal((DrawingSortField.Name, false), (library.Queries[^1].Sort, library.Queries[^1].Descending));
        await session.SortByAsync(DrawingSortField.Name);
        Assert.Equal((DrawingSortField.Name, true), (library.Queries[^1].Sort, library.Queries[^1].Descending));
    }

    [Theory]
    [InlineData(false, "", "No library drawings match this nest's material and thickness. Tick Show all materials to see every drawing.")]
    [InlineData(true, "", "No drawings in the library.")]
    [InlineData(false, "no such text", "No drawings match the search.")]
    public async Task Summary_ExplainsAnEmptyResult(bool showAll, string search, string expected)
    {
        using var session = new DrawingLibraryBrowseSession(new PagedLibrary(0), SyntheticNest());

        await session.SetShowAllAsync(showAll);
        await session.SetSearchAsync(search);

        Assert.Equal(expected, session.Summary);
    }

    [Fact]
    public async Task OlderResponse_ArrivingLast_NeverReplacesTheNewerResult()
    {
        var library = new ControlledLibrary();
        using var session = new DrawingLibraryBrowseSession(library, SyntheticNest());
        var older = session.SetSearchAsync("a");
        var newer = session.SetShowAllAsync(true);
        var newerPage = Page("show all result");

        library.Requests[1].Reply.SetResult(newerPage);
        Assert.True(await newer);
        library.Requests[0].Reply.SetResult(Page("a result"));
        Assert.False(await older);

        Assert.Same(newerPage, session.Page);
        Assert.True(library.Requests[0].Token.IsCancellationRequested);
        Assert.False(library.Requests[1].Token.IsCancellationRequested);
    }

    [Fact]
    public async Task SupersededFailure_IsIgnored_LatestFailure_ClearsThePageAndPropagates()
    {
        var library = new ControlledLibrary();
        using var session = new DrawingLibraryBrowseSession(library, SyntheticNest());
        var older = session.SetSearchAsync("a");
        var newer = session.SetSearchAsync("ab");

        library.Requests[0].Reply.SetException(new IOException("superseded failure"));
        Assert.False(await older);
        library.Requests[1].Reply.SetResult(Page("ab result"));
        Assert.True(await newer);

        var failing = session.RefreshAsync();
        library.Requests[2].Reply.SetException(new ServerTooOldException());
        await Assert.ThrowsAsync<ServerTooOldException>(() => failing);

        Assert.Null(session.Page);
        Assert.Equal("", session.Summary);
        Assert.False(session.IsLoading);
    }

    [Fact]
    public async Task SupersededRequest_CompletingSynchronouslyOnCancellation_IsNotReportedAsTheLatestFailure()
    {
        var library = new ControlledLibrary { CompleteOnCancellation = true };
        using var session = new DrawingLibraryBrowseSession(library, SyntheticNest());
        var older = session.SetSearchAsync("a");
        var newer = session.SetSearchAsync("ab");

        Assert.False(await older);
        library.Requests[1].Reply.SetResult(Page("ab result"));
        Assert.True(await newer);

        Assert.Equal("ab result", Assert.Single(session.Page!.Items).Name);
    }

    [Fact]
    public async Task IsLoading_FollowsOnlyTheLatestRequest()
    {
        var library = new ControlledLibrary();
        using var session = new DrawingLibraryBrowseSession(library, SyntheticNest());

        var first = session.SetSearchAsync("a");
        var second = session.SetSearchAsync("ab");
        library.Requests[0].Reply.SetResult(Page("a result"));
        Assert.False(await first);
        Assert.True(session.IsLoading);
        library.Requests[1].Reply.SetResult(Page("ab result"));
        Assert.True(await second);
        Assert.False(session.IsLoading);
    }

    [Fact]
    public async Task Refresh_AfterTheLastPageWasEmptied_StepsBackToTheNewLastPage()
    {
        var library = new PagedLibrary(21);
        using var session = new DrawingLibraryBrowseSession(library, SyntheticNest(), pageSize: 10);
        await session.RefreshAsync();
        await session.NextPageAsync();
        await session.NextPageAsync();

        library.RemoveLast();
        Assert.True(await session.RefreshAsync());

        Assert.Equal(new[] { 20, 10 }, library.Queries.TakeLast(2).Select(q => q.Offset));
        Assert.Equal("Showing 11-20 of 20 drawings", session.Summary);
    }

    [Fact]
    public async Task Dispose_CancelsTheRequestAndDiscardsItsLateResponse()
    {
        var library = new ControlledLibrary();
        var session = new DrawingLibraryBrowseSession(library, SyntheticNest());
        var pending = session.SetSearchAsync("a");

        session.Dispose();
        library.Requests[0].Reply.SetResult(Page("late"));

        Assert.True(library.Requests[0].Token.IsCancellationRequested);
        Assert.False(await pending);
        Assert.Null(session.Page);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.RefreshAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(DrawingLibraryQuery.MaxLimit + 1)]
    public void Constructor_RejectsPageSizesTheServerWouldRefuse(int pageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingLibraryBrowseSession(new PagedLibrary(0), SyntheticNest(), pageSize));
    }

    private static DrawingLibraryPage Page(string name) =>
        new() { Items = new[] { new DrawingLibraryRecord { Id = Guid.NewGuid(), Revision = 1, Name = name } }, Total = 1, Limit = 100 };

    /// <summary>Serves pages of an in-memory set (ignoring filters) and records each query.</summary>
    private sealed class PagedLibrary : IDrawingLibrary
    {
        private readonly List<DrawingLibraryRecord> _records;

        public PagedLibrary(int count) =>
            _records = Enumerable.Range(0, count)
                .Select(i => new DrawingLibraryRecord { Id = Guid.NewGuid(), Revision = 1, Name = $"Drawing {i}" })
                .ToList();

        public List<DrawingLibraryQuery> Queries { get; } = new();

        public void RemoveLast() => _records.RemoveAt(_records.Count - 1);

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
        {
            Assert.Null(query.GetValidationError());
            Queries.Add(query);
            var matches = query.NormalizedSearch.Length == 0 ? _records : new List<DrawingLibraryRecord>();
            return Task.FromResult(new DrawingLibraryPage
            {
                Items = matches.Skip(query.Offset).Take(query.Limit).ToArray(),
                Total = matches.Count,
                Offset = query.Offset,
                Limit = query.Limit,
            });
        }

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Leaves every query pending until the test completes it. With
    /// <see cref="CompleteOnCancellation"/>, cancellation completes the query synchronously
    /// inside the canceller's call, as some HTTP handlers do.
    /// </summary>
    private sealed class ControlledLibrary : IDrawingLibrary
    {
        public bool CompleteOnCancellation { get; init; }

        public List<(DrawingLibraryQuery Query, TaskCompletionSource<DrawingLibraryPage> Reply, CancellationToken Token)> Requests { get; } = new();

        public Task<DrawingLibraryPage> QueryAsync(DrawingLibraryQuery query, CancellationToken cancellationToken = default)
        {
            var reply = new TaskCompletionSource<DrawingLibraryPage>();
            if (CompleteOnCancellation)
                cancellationToken.Register(() => reply.TrySetCanceled(cancellationToken));
            Requests.Add((query, reply, cancellationToken));
            return reply.Task;
        }

        public Task<byte[]?> GetRevisionPackageAsync(Guid id, int revision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
