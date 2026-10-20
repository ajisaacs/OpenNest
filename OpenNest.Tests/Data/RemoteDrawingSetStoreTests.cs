using System.Net;
using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class RemoteDrawingSetStoreTests
{
    private static RemoteDrawingSetStore Store(StubHandler handler) =>
        new(new HttpClient(handler), "http://server:8090/base/");

    private static DrawingSetRecord Set(Guid? drawing = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Synthetic kit",
        Members = new[] { new DrawingSetMember { DrawingId = drawing ?? Guid.NewGuid(), Quantity = 2 } },
    };

    [Fact]
    public async Task QueryAsync_SendsEscapedFilters()
    {
        var handler = new StubHandler(_ => StubHandler.Json(new { items = Array.Empty<object>(), total = 0, offset = 5, limit = 5 }));
        using var store = Store(handler);

        await store.QueryAsync(new DrawingSetQuery
        {
            Search = " 50%_ ",
            Customer = "A&B",
            IncludeArchived = true,
            Sort = DrawingSetSortField.Customer,
            Descending = false,
            Offset = 5,
            Limit = 5,
        });

        Assert.Equal(
            "http://server:8090/base/api/sets/query?search=50%25_&sort=customer&order=asc&offset=5&limit=5&customer=A%26B&includeArchived=true",
            handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task OldServer_RaisesServerTooOld()
    {
        using var store = Store(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var query = await Assert.ThrowsAsync<ServerTooOldException>(() => store.QueryAsync(new DrawingSetQuery()));
        await Assert.ThrowsAsync<ServerTooOldException>(() => store.GetAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ServerTooOldException>(() => store.GetReleaseForNestAsync(Guid.NewGuid()));
        Assert.Contains("drawing sets", query.Message);
    }

    [Fact]
    public async Task UnknownSet_OnASetAwareServer_IsNull()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/api/sets/query")
            ? StubHandler.Json(new { items = Array.Empty<object>(), total = 0, offset = 0, limit = 1 })
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var store = Store(handler);

        Assert.Null(await store.GetAsync(Guid.NewGuid()));
        Assert.EndsWith("api/sets/query?limit=1", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task InvalidSet_IsRefusedBeforeSending()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Nothing may be sent."));
        using var store = Store(handler);
        var duplicate = Set();
        duplicate.Members = duplicate.Members.Concat(duplicate.Members).ToList();

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(duplicate));
        await Assert.ThrowsAsync<ArgumentException>(() => store.PostReleaseAsync(Guid.NewGuid(),
            new SetReleaseRecord { Id = Guid.NewGuid(), SetRevision = 1, Multiplier = 0 }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateAsync_SendsMembersWithoutJoinedDrawingsAndChecksTheReply()
    {
        var drawing = Guid.NewGuid();
        var set = Set(drawing);
        set.Members[0].Drawing = new DrawingLibraryRecord { Id = drawing, Name = "Synthetic joined" };
        var handler = new StubHandler(_ => StubHandler.Json(new
        {
            id = set.Id,
            revision = 1,
            name = set.Name,
            members = new[] { new { drawingId = drawing, quantity = 3 } },
        }));
        using var store = Store(handler);

        await Assert.ThrowsAsync<IOException>(() => store.CreateAsync(set));
        Assert.DoesNotContain("Synthetic joined", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task PostReleaseAsync_ReplyForOtherNests_IsRefused()
    {
        var setId = Guid.NewGuid();
        var release = new SetReleaseRecord
        {
            Id = Guid.NewGuid(),
            SetRevision = 1,
            Multiplier = 1,
            Nests = new[] { new SetReleaseNest { NestId = Guid.NewGuid(), MaterialName = "Synthetic Steel" } },
        };
        var handler = new StubHandler(_ => StubHandler.Json(new
        {
            id = release.Id,
            setId,
            setRevision = 1,
            nests = new[] { new { nestId = Guid.NewGuid() } },
        }));
        using var store = Store(handler);

        await Assert.ThrowsAsync<IOException>(() => store.PostReleaseAsync(setId, release));
    }
}
