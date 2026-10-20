using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class DrawingSetBrowseSessionTests
{
    private static async Task<InMemoryDrawingSets> Store(int count, int archived = 0)
    {
        var store = new InMemoryDrawingSets();
        for (var i = 0; i < count; i++)
        {
            var created = await store.CreateAsync(new DrawingSetRecord { Id = Guid.NewGuid(), Name = $"Synthetic kit {i:D2}" });
            if (i < archived)
                await store.ArchiveAsync(created.Id, created.Revision);
        }

        return store;
    }

    [Fact]
    public async Task Refresh_ListsActiveSetsAndPages()
    {
        var store = await Store(5, archived: 1);
        using var session = new DrawingSetBrowseSession(store, pageSize: 2);

        Assert.True(await session.RefreshAsync());
        Assert.Equal("Showing 1-2 of 4 sets", session.Summary);
        Assert.True(session.CanGoNext);
        Assert.True(await session.NextPageAsync());

        Assert.Equal("Showing 3-4 of 4 sets", session.Summary);
        Assert.False(session.CanGoNext);
        Assert.False(await session.NextPageAsync());
        Assert.True(await session.PreviousPageAsync());
        Assert.Equal(0, session.Offset);
        Assert.False(store.Queries[0].IncludeArchived);
    }

    [Fact]
    public async Task SearchArchivedAndSort_ReturnToTheFirstPage()
    {
        var store = await Store(3, archived: 1);
        using var session = new DrawingSetBrowseSession(store, pageSize: 2);
        await session.RefreshAsync();
        await session.NextPageAsync();

        await session.SetIncludeArchivedAsync(true);
        Assert.Equal((0, 3), (session.Offset, session.Page!.Total));
        await session.SetSearchAsync("  kit 01 ");
        Assert.Equal(("kit 01", 1), (store.Queries[^1].Search, session.Page!.Total));
        await session.SortByAsync(DrawingSetSortField.Name);
        Assert.Equal((DrawingSetSortField.Name, false), (store.Queries[^1].Sort, store.Queries[^1].Descending));
        await session.SortByAsync(DrawingSetSortField.Name);
        Assert.True(store.Queries[^1].Descending);
    }

    [Fact]
    public async Task EmptyList_SaysHowToStart()
    {
        using var session = new DrawingSetBrowseSession(new InMemoryDrawingSets());

        await session.RefreshAsync();

        Assert.Contains("New Set", session.Summary);
        await session.SetSearchAsync("nothing");
        Assert.Equal("No sets match the search.", session.Summary);
    }

    [Fact]
    public async Task FailedQuery_ClearsThePageAndPropagates()
    {
        var store = await Store(1);
        using var session = new DrawingSetBrowseSession(store);
        await session.RefreshAsync();
        store.QueryFailure = new IOException("Synthetic offline.");

        await Assert.ThrowsAsync<IOException>(() => session.RefreshAsync());

        Assert.Null(session.Page);
        Assert.False(session.IsLoading);
    }
}
