using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class DrawingSetEditSessionTests
{
    private static DrawingLibraryRecord Drawing(string name) => new()
    {
        Id = Guid.NewGuid(),
        Revision = 1,
        Name = name,
        MaterialName = "Synthetic Steel",
        Thickness = 0.25,
    };

    [Fact]
    public void AddDrawings_SkipsDrawingsAlreadyListed()
    {
        var a = Drawing("SYN-A");
        var b = Drawing("SYN-B");
        var session = new DrawingSetEditSession(new InMemoryDrawingSets(), null, "Synthetic operator");

        var first = session.AddDrawings(new[] { a, b, a }, 3);
        var second = session.AddDrawings(new[] { b, Drawing("SYN-C") });

        Assert.Equal(new[] { a, b }, first.Added);
        Assert.Equal(new[] { a }, first.AlreadyMembers);
        Assert.Equal(new[] { b }, second.AlreadyMembers);
        Assert.Equal(new[] { ("SYN-A", 3), ("SYN-B", 3), ("SYN-C", 1) },
            session.Members.Select(m => (m.Drawing!.Name, m.Quantity)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.AddDrawings(new[] { Drawing("SYN-D") }, 0));
    }

    [Fact]
    public void UpdateDrawing_ShowsANewerRevisionOnItsMember_WithoutChangingTheSet()
    {
        var a = Drawing("SYN-A");
        var session = new DrawingSetEditSession(new InMemoryDrawingSets(), null, "");
        session.AddDrawings(new[] { a, Drawing("SYN-B") }, 2);
        var before = session.Build();
        var edited = new DrawingLibraryRecord { Id = a.Id, Revision = 2, Name = "SYN-A2", MaterialName = "Synthetic Aluminum", Thickness = 0.125 };

        Assert.True(session.UpdateDrawing(edited));
        Assert.False(session.UpdateDrawing(a));
        Assert.False(session.UpdateDrawing(Drawing("SYN-C")));

        Assert.Same(edited, session.Members[0].Drawing);
        Assert.Equal(before.Members.Select(m => (m.DrawingId, m.Quantity)), session.Build().Members.Select(m => (m.DrawingId, m.Quantity)));
    }

    [Fact]
    public void MoveRemoveAndQuantity_ChangeTheMemberList()
    {
        var session = new DrawingSetEditSession(new InMemoryDrawingSets(), null, "");
        session.AddDrawings(new[] { Drawing("SYN-A"), Drawing("SYN-B"), Drawing("SYN-C") });

        Assert.True(session.Move(2, -2));
        Assert.False(session.Move(0, -1));
        session.SetQuantity(1, 7);
        session.RemoveAt(2);

        Assert.Equal(new[] { ("SYN-C", 1), ("SYN-A", 7) }, session.Members.Select(m => (m.Drawing!.Name, m.Quantity)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetQuantity(0, DrawingSetRules.MaxQuantity + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.RemoveAt(2));
    }

    [Fact]
    public async Task NewSet_SavesRevisionOneUnderItsAllocatedId()
    {
        var store = new InMemoryDrawingSets();
        var session = new DrawingSetEditSession(store, null, "Synthetic operator");
        Assert.Equal("name is required.", session.GetError());
        session.Name = "Synthetic kit";
        session.Customer = "Synthetic customer";
        session.AddDrawings(new[] { Drawing("SYN-A") }, 2);

        var saved = await session.SaveAsync();

        Assert.Equal((session.Id, 1, "Synthetic operator"), (saved.Id, saved.Revision, saved.SavedBy));
        Assert.False(session.IsNew);
        Assert.False(session.IsChanged);
        Assert.Same(saved, session.Saved);
        Assert.Single(store.Creates);
    }

    [Fact]
    public async Task ExistingSet_SavesGuardedByTheRevisionItWasReadAt_AndIdenticalSaveSendsNothing()
    {
        var store = new InMemoryDrawingSets();
        var a = Drawing("SYN-A");
        var created = await store.CreateAsync(new DrawingSetRecord
        {
            Id = Guid.NewGuid(),
            Name = "Synthetic kit",
            Members = new[] { new DrawingSetMember { DrawingId = a.Id, Quantity = 1, Drawing = a } },
        });
        var session = new DrawingSetEditSession(store, created, "");

        Assert.False(session.IsChanged);
        Assert.Same(created, await session.SaveAsync());
        session.SetQuantity(0, 4);
        Assert.True(session.IsChanged);
        var saved = await session.SaveAsync();

        Assert.Equal(new[] { (created.Id, 1) }, store.Updates.Select(u => (u.Id, u.ExpectedRevision)));
        Assert.Equal(2, saved.Revision);
        Assert.Equal(4, session.Members.Single().Quantity);
    }

    [Fact]
    public async Task StaleSave_RaisesTheConflictAndKeepsTheEdits()
    {
        var store = new InMemoryDrawingSets();
        var created = await store.CreateAsync(new DrawingSetRecord { Id = Guid.NewGuid(), Name = "Synthetic kit" });
        var session = new DrawingSetEditSession(store, created, "");
        await store.UpdateAsync(created.Id, new DrawingSetRecord { Name = "Renamed elsewhere" }, 1);
        session.Notes = "Synthetic notes";

        var conflict = await Assert.ThrowsAsync<DrawingSetSaveConflictException>(() => session.SaveAsync());

        Assert.Equal(2, conflict.Current.Revision);
        Assert.Equal("Synthetic notes", session.Notes);
        Assert.Equal(1, session.Saved!.Revision);
    }

    [Fact]
    public async Task NewSet_AmbiguousFailureThenRetry_AdoptsTheStoredSetInsteadOfCreatingTwice()
    {
        var store = new InMemoryDrawingSets { LoseNextCreateReply = true };
        var session = new DrawingSetEditSession(store, null, "");
        session.Name = "Synthetic kit";

        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync());
        Assert.True(session.HasPendingSave);
        session.Notes = "Changed after the lost reply";
        var saved = await session.SaveAsync();

        Assert.Single(store.Creates);
        Assert.Equal(new[] { (session.Id, 1) }, store.Updates.Select(u => (u.Id, u.ExpectedRevision)));
        Assert.Equal((2, "Changed after the lost reply"), (saved.Revision, saved.Notes));
        Assert.False(session.HasPendingSave);
    }

    [Fact]
    public async Task NewSet_FailureThatStoredNothing_RetriesTheSameId()
    {
        var store = new InMemoryDrawingSets { FailNextCreate = true };
        var session = new DrawingSetEditSession(store, null, "");
        session.Name = "Synthetic kit";

        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync());
        var saved = await session.SaveAsync();

        Assert.Equal(new[] { session.Id, session.Id }, store.Creates.Select(c => c.Id));
        Assert.Equal(1, saved.Revision);
    }

    [Fact]
    public void Constructor_RefusesAnArchivedOrUnjoinedSet()
    {
        var store = new InMemoryDrawingSets();
        var archived = new DrawingSetRecord { Id = Guid.NewGuid(), Revision = 2, Name = "Synthetic kit", Archived = true };
        var unjoined = new DrawingSetRecord
        {
            Id = Guid.NewGuid(),
            Revision = 1,
            Name = "Synthetic kit",
            Members = new[] { new DrawingSetMember { DrawingId = Guid.NewGuid(), Quantity = 1 } },
        };

        Assert.Throws<ArgumentException>(() => new DrawingSetEditSession(store, archived, ""));
        Assert.Throws<ArgumentException>(() => new DrawingSetEditSession(store, unjoined, ""));
    }
}
