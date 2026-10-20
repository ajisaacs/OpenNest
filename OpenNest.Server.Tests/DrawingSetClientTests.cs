using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingSetFixtures;

namespace OpenNest.Server.Tests;

/// <summary>The real <see cref="RemoteDrawingSetStore"/> against the production set and release routes.</summary>
public sealed class DrawingSetClientTests
{
    [Fact]
    public async Task Sets_RoundTripThroughEveryRoute()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var store = new RemoteDrawingSetStore(client, client.BaseAddress!.ToString());
        var a = await AddDrawing(client, "Synthetic A");
        var b = await AddDrawing(client, "Synthetic B");

        var created = await store.CreateAsync(Set(members: [(a, 2)]));
        var read = await store.GetAsync(created.Id);
        read!.Members = read.Members.Append(new DrawingSetMember { DrawingId = b, Quantity = 5 }).ToList();
        read.Notes = "Synthetic revised notes";
        var updated = await store.UpdateAsync(created.Id, read, read.Revision);
        var archived = await store.ArchiveAsync(created.Id, updated.Revision);
        var restored = await store.RestoreAsync(created.Id, archived.Revision);

        Assert.Equal((1, 2, 3, 4), (created.Revision, updated.Revision, archived.Revision, restored.Revision));
        Assert.Equal("Synthetic A", read.Members[0].Drawing!.Name);
        Assert.Equal(new[] { (a, 2), (b, 5) }, MembersOf(updated));
        Assert.True(archived.Archived);
        Assert.False(restored.Archived);

        var history = await store.GetRevisionsAsync(created.Id, 1, 2);
        Assert.Equal(new[] { 3, 2 }, history!.Items.Select(r => r.Revision));
        Assert.Equal(4, history.Total);
        var first = await store.GetRevisionAsync(created.Id, 1);
        Assert.Equal(new[] { (a, 2) }, MembersOf(first!));
        Assert.Equal("Synthetic B", (await store.GetAsync(created.Id))!.Members[1].Drawing!.Name);

        var usedIn = await store.GetSetsContainingAsync(b);
        Assert.Equal(created.Id, Assert.Single(usedIn!.Items).Id);
        var page = await store.QueryAsync(new DrawingSetQuery { Search = "revised", Customer = " synthetic  CUSTOMER" });
        Assert.Equal(created.Id, Assert.Single(page.Items).Id);
        Assert.Empty((await store.QueryAsync(new DrawingSetQuery { Customer = "Someone else" })).Items);
    }

    [Fact]
    public async Task Writes_ThatConflict_CarryTheCurrentRecord()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var store = new RemoteDrawingSetStore(client, client.BaseAddress!.ToString());
        var created = await store.CreateAsync(Set());
        var renamed = Set("Synthetic renamed", created.Id);
        await store.UpdateAsync(created.Id, renamed, 1);

        var stale = await Assert.ThrowsAsync<DrawingSetSaveConflictException>(
            () => store.UpdateAsync(created.Id, Set("Synthetic stale", created.Id), 1));
        var duplicate = await Assert.ThrowsAsync<DrawingSetSaveConflictException>(() => store.CreateAsync(Set("Second", created.Id)));
        var restoreActive = await Assert.ThrowsAsync<DrawingSetSaveConflictException>(() => store.RestoreAsync(created.Id, 2));

        Assert.All(new[] { stale, duplicate, restoreActive }, conflict =>
            Assert.Equal(("Synthetic renamed", 2), (conflict.Current.Name, conflict.Current.Revision)));
    }

    [Fact]
    public async Task UnknownIds_ReadAsNull()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var store = new RemoteDrawingSetStore(client, client.BaseAddress!.ToString());
        var set = await store.CreateAsync(Set());

        Assert.Null(await store.GetAsync(Guid.NewGuid()));
        Assert.Null(await store.GetRevisionAsync(set.Id, 2));
        Assert.Null(await store.GetRevisionsAsync(Guid.NewGuid()));
        Assert.Null(await store.GetSetsContainingAsync(Guid.NewGuid()));
        Assert.Null(await store.GetReleasesAsync(Guid.NewGuid()));
        Assert.Null(await store.GetReleaseForNestAsync(Guid.NewGuid()));
        var unknown = Set();
        await Assert.ThrowsAsync<IOException>(() => store.UpdateAsync(unknown.Id, unknown, 1));
    }

    [Fact]
    public async Task Releases_PostReadAndRefuseANestTwice()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var store = new RemoteDrawingSetStore(client, client.BaseAddress!.ToString());
        var set = await store.CreateAsync(Set());
        var steel = await AddNest(client, "Synthetic steel nest");
        var aluminum = await AddNest(client, "Synthetic aluminum nest");
        var release = Release(1, steel, aluminum);

        var stored = await store.PostReleaseAsync(set.Id, release);
        var repost = await Assert.ThrowsAsync<SetReleasePostConflictException>(() => store.PostReleaseAsync(set.Id, release));
        var reuse = await Assert.ThrowsAsync<SetReleasePostConflictException>(() => store.PostReleaseAsync(set.Id, Release(1, steel)));

        Assert.Equal((release.Id, set.Id, "Synthetic kit", 2), (stored.Id, stored.SetId, stored.SetName, stored.Multiplier));
        Assert.Equal(new[] { steel, aluminum }, stored.Nests.Select(n => n.NestId));
        Assert.Equal(release.Id, repost.Current.Id);
        Assert.Equal(release.Id, reuse.Current.Id);

        var page = await store.GetReleasesAsync(set.Id);
        var listed = Assert.Single(page!.Items);
        Assert.Equal(new[] { "Synthetic steel nest", "Synthetic aluminum nest" }, listed.Nests.Select(n => n.Nest!.Name));
        Assert.Equal(release.Id, (await store.GetReleaseForNestAsync(aluminum))!.Id);
        await Assert.ThrowsAsync<IOException>(() => store.PostReleaseAsync(Guid.NewGuid(), Release(1, Guid.NewGuid())));
    }
}
