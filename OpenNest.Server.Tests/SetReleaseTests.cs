using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;
using static OpenNest.Server.Tests.DrawingSetFixtures;

namespace OpenNest.Server.Tests;

public sealed class SetReleaseTests
{
    [Fact]
    public void Release_OfSeveralSetsAtStaleRevisions_IsStoredWithNamesNestsAndItems()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var drawing = AddDrawing(database);
        var kit = Set("Kit as released", members: (drawing, 1));
        var other = Set("Other kit", members: (drawing, 2));
        database.InsertDrawingSet(kit.Id, kit);
        database.InsertDrawingSet(other.Id, other);
        database.UpdateDrawingSet(kit.Id, Set("Kit renamed later", kit.Id, (drawing, 3)), 1);
        var created = AddNest(database, "Created nest");
        var draft = AddNest(database, "Draft nest");
        var merged = Resave(database, draft);
        var release = Release(kit.Id, 1, created, draft).Merged(1, before: 1, after: merged);
        release.Sets = release.Sets.Append(new SetReleaseLine { SetId = other.Id, SetRevision = 1, Quantity = 3 }).ToList();
        release.Nests[1].Thickness = 0.123456;
        release.Nests[1].Units = Units.Millimeters;
        release.Nests[1].CutType = CutType.Waterjet;
        release.Nests[1].Items = new[]
        {
            new SetReleaseNestItem { SetId = kit.Id, DrawingId = drawing, DrawingRevision = 1, Quantity = 2 },
            new SetReleaseNestItem { SetId = other.Id, DrawingId = drawing, DrawingRevision = 1, Quantity = 6 },
        };

        var stored = database.InsertRelease(release);

        Assert.Equal(release.Id, stored.Id);
        Assert.Equal(new[] { (kit.Id, 1, 2, "Kit as released"), (other.Id, 1, 3, "Other kit") },
            stored.Sets.Select(s => (s.SetId, s.SetRevision, s.Quantity, s.SetName)));
        Assert.Equal(("Synthetic release", "Synthetic operator", false), (stored.Notes, stored.ReleasedBy, stored.IsUndone));
        Assert.NotEqual(default, stored.ReleasedAt);
        Assert.Equal(new[] { (created, SetReleaseNestKind.Created, (int?)null, 1), (draft, SetReleaseNestKind.Merged, 1, 2) },
            stored.Nests.Select(n => (n.NestId, n.Kind, n.RevisionBefore, n.RevisionAfter)));
        Assert.Equal(("Synthetic Steel 1", "Grade A", 0.1235, Units.Millimeters, CutType.Waterjet),
            (stored.Nests[1].MaterialName, stored.Nests[1].MaterialGrade, stored.Nests[1].Thickness, stored.Nests[1].Units, stored.Nests[1].CutType));
        Assert.Equal(new[] { ((Guid?)kit.Id, 2), (other.Id, 6) }, stored.Nests[1].Items.Select(i => (i.SetId, i.Quantity)));
        Assert.Empty(stored.Nests[0].Items);
        Assert.Equal(("Created nest", NestStatus.Draft, false), (stored.Nests[0].Nest!.Name, stored.Nests[0].Nest!.Status, stored.Nests[0].Nest!.Deleted));
        Assert.Null(stored.Nests[0].Undo);
        Assert.Equal(release.Id, database.GetRelease(release.Id)!.Id);
        Assert.Null(database.GetRelease(Guid.NewGuid()));
    }

    [Fact]
    public void JoinedNests_FollowStatusChangesAndDeletion()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        database.InsertDrawingSet(set.Id, set);
        var cut = AddNest(database, "Cut nest");
        var removed = AddNest(database, "Removed nest");
        database.InsertRelease(Release(set.Id, 1, cut, removed));

        var record = database.Get(cut)!;
        record.Status = NestStatus.HasBeenCut;
        database.UpdateMetadata(cut, record, 1);
        database.SetDeleted(removed, true, 1);

        var nests = database.QueryReleases(set.Id, null, 0, 100).Items.Single().Nests;
        Assert.Equal((NestStatus.HasBeenCut, false), (nests[0].Nest!.Status, nests[0].Nest!.Deleted));
        Assert.True(nests[1].Nest!.Deleted);
        Assert.True(database.QueryReleases(null, removed, 0, 100).Items.Single().Nests[1].Nest!.Deleted);
    }

    [Fact]
    public void Query_FiltersBySetAndNest_NewestFirst_AndAcceptsAnArchivedSet()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        var other = Set("Other kit");
        database.InsertDrawingSet(set.Id, set);
        database.InsertDrawingSet(other.Id, other);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var release = Release(set.Id, 1, AddNest(database));
            ids.Add(release.Id);
            database.InsertRelease(release);
            Thread.Sleep(5);
        }
        var shared = AddNest(database);
        var onOther = Release(other.Id, 1, shared);
        database.InsertRelease(onOther);
        Thread.Sleep(5);
        database.SetDrawingSetArchived(set.Id, true, 1);
        var merge = Release(set.Id, 1, shared).Merged(0, before: 1, after: Resave(database, shared));
        database.InsertRelease(merge);
        ids.Add(merge.Id);

        var page = database.QueryReleases(set.Id, null, 1, 2);
        Assert.Equal(4, page.Total);
        Assert.Equal(new[] { ids[2], ids[1] }, page.Items.Select(r => r.Id));
        Assert.Equal(new[] { onOther.Id }, database.QueryReleases(other.Id, null, 0, 100).Items.Select(r => r.Id));
        Assert.Equal(new[] { merge.Id, onOther.Id }, database.QueryReleases(null, shared, 0, 100).Items.Select(r => r.Id));
        Assert.Equal(new[] { merge.Id }, database.QueryReleases(set.Id, shared, 0, 100).Items.Select(r => r.Id));
        Assert.Equal(5, database.QueryReleases(null, null, 0, 100).Total);
        Assert.Empty(database.QueryReleases(Guid.NewGuid(), null, 0, 100).Items);
        Assert.Throws<ArgumentOutOfRangeException>(() => database.QueryReleases(set.Id, null, 0, 201));
    }

    [Fact]
    public void Refusals_LeaveNoState()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        database.InsertDrawingSet(set.Id, set);
        var free = AddNest(database);
        var taken = AddNest(database);
        var deleted = AddNest(database);
        database.SetDeleted(deleted, true, 1);
        var owner = Release(set.Id, 1, taken);
        database.InsertRelease(owner);

        Assert.Contains("does not exist", Assert.Throws<ArgumentException>(() => database.InsertRelease(Release(Guid.NewGuid(), 1, free))).Message);
        Assert.Contains("no revision 2", Assert.Throws<ArgumentException>(() => database.InsertRelease(Release(set.Id, 2, free))).Message);
        Assert.Contains("does not exist", Assert.Throws<ArgumentException>(() => database.InsertRelease(Release(set.Id, 1, free, Guid.NewGuid()))).Message);
        Assert.Contains("is deleted", Assert.Throws<ArgumentException>(() => database.InsertRelease(Release(set.Id, 1, free, deleted))).Message);
        Assert.Contains("no revision 3", Assert.Throws<ArgumentException>(() => database.InsertRelease(Release(set.Id, 1, free).Merged(0, 1, 3))).Message);
        var after = Release(set.Id, 1, free);
        after.Nests[0].RevisionAfter = 2;
        Assert.Contains("no revision 2", Assert.Throws<ArgumentException>(() => database.InsertRelease(after)).Message);
        Assert.Throws<ArgumentException>(() => database.InsertRelease(Release(set.Id, 1, free, free)));
        Assert.Throws<ArgumentException>(() => database.InsertRelease(Release(set.Id, 1)));
        var twice = Release(set.Id, 1, free);
        twice.Sets = twice.Sets.Concat(twice.Sets).ToList();
        Assert.Throws<ArgumentException>(() => database.InsertRelease(twice));
        var noId = Release(set.Id, 1, free);
        noId.Id = Guid.Empty;
        Assert.Throws<ArgumentException>(() => database.InsertRelease(noId));
        var tooMany = Release(set.Id, 1, free);
        tooMany.Sets[0].Quantity = SetReleaseRules.MaxQuantity + 1;
        Assert.Throws<ArgumentException>(() => database.InsertRelease(tooMany));
        var strangerItem = Release(set.Id, 1, free);
        strangerItem.Nests[0].Items = new[] { new SetReleaseNestItem { SetId = Guid.NewGuid(), DrawingId = Guid.NewGuid(), DrawingRevision = 1, Quantity = 1 } };
        Assert.Throws<ArgumentException>(() => database.InsertRelease(strangerItem));

        var claimed = Assert.Throws<SetReleaseConflictException>(() => database.InsertRelease(Release(set.Id, 1, free, taken)));
        Assert.Equal(owner.Id, claimed.Current.Id);
        var reused = Release(set.Id, 1, free);
        reused.Id = owner.Id;
        Assert.Equal(owner.Id, Assert.Throws<SetReleaseConflictException>(() => database.InsertRelease(reused)).Current.Id);

        Assert.Equal(1L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM releases"));
        Assert.Equal(1L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM release_sets"));
        Assert.Equal(1L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM release_nests"));
        Assert.Empty(database.QueryReleases(null, free, 0, 100).Items);
    }

    [Fact]
    public void MergedNest_MayBeInSeveralReleases_ButACreatedNestOnlyInItsOwn()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        database.InsertDrawingSet(set.Id, set);
        var nest = AddNest(database);
        database.InsertRelease(Release(set.Id, 1, nest));

        database.InsertRelease(Release(set.Id, 1, nest).Merged(0, 1, Resave(database, nest)));
        database.InsertRelease(Release(set.Id, 1, nest).Merged(0, 2, Resave(database, nest, 8)));

        Assert.Equal(3, database.QueryReleases(null, nest, 0, 100).Total);
    }

    [Fact]
    public void Undo_DeletesCreatedNests_AndRestoresMergedOnes_InOneStep()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        database.InsertDrawingSet(set.Id, set);
        var created = AddNest(database, "Created nest");
        var draft = AddNest(database, "Draft nest");
        var release = Release(set.Id, 1, created, draft).Merged(1, 1, Resave(database, draft));
        database.InsertRelease(release);

        var undone = database.UndoRelease(release.Id, new SetReleaseUndoRequest { UndoneBy = "Synthetic undoer" })!;

        Assert.True(undone.IsUndone);
        Assert.Equal("Synthetic undoer", undone.UndoneBy);
        Assert.Equal(new[] { (SetReleaseNestUndo.Deleted, (int?)2), (SetReleaseNestUndo.Restored, 3) },
            undone.Nests.Select(n => (n.Undo!.Value, n.UndoRevision)));
        Assert.Null(database.Get(created));
        Assert.Equal(2, database.Get(created, deleted: true)!.Revision);
        var restored = database.Get(draft)!;
        Assert.Equal((3, (int?)1), (restored.Revision, restored.RestoredFromRevision));
        Assert.Equal(new byte[] { 1, 2, 3 }, database.GetFile(draft));
        Assert.Equal("Undone", SetReleaseText.Progress(database.GetRelease(release.Id)!));
        Assert.Null(database.UndoRelease(Guid.NewGuid(), new SetReleaseUndoRequest()));
    }

    [Fact]
    public void Undo_WhenANestChanged_ChangesNothing_UntilThatNestIsSkipped()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        database.InsertDrawingSet(set.Id, set);
        var edited = AddNest(database, "Edited later");
        var untouched = AddNest(database, "Untouched");
        var removed = AddNest(database, "Deleted later");
        var release = Release(set.Id, 1, edited, untouched, removed);
        database.InsertRelease(release);
        Resave(database, edited);
        database.SetDeleted(removed, true, 1);

        var refused = Assert.Throws<ReleaseUndoRefusedException>(() =>
            database.UndoRelease(release.Id, new SetReleaseUndoRequest { UndoneBy = "Synthetic undoer" })).Conflict;

        Assert.Equal(SetReleaseUndoRefusal.NestsChanged, refused.Reason);
        Assert.Equal(new[] { (edited, 2, false), (removed, 2, true) }, refused.ChangedNests.Select(n => (n.Id, n.Revision, n.Deleted)));
        Assert.False(database.GetRelease(release.Id)!.IsUndone);
        Assert.NotNull(database.Get(untouched));

        var skipped = database.UndoRelease(release.Id, new SetReleaseUndoRequest { SkipNests = new[] { edited, removed } })!;

        Assert.Equal(new SetReleaseNestUndo?[] { SetReleaseNestUndo.Skipped, SetReleaseNestUndo.Deleted, SetReleaseNestUndo.Skipped },
            skipped.Nests.Select(n => n.Undo));
        Assert.Null(skipped.Nests[0].UndoRevision);
        Assert.Equal(2, database.Get(edited)!.Revision);
        Assert.Null(database.Get(untouched));
        var again = Assert.Throws<ReleaseUndoRefusedException>(() => database.UndoRelease(release.Id, new SetReleaseUndoRequest()));
        Assert.Equal(SetReleaseUndoRefusal.AlreadyUndone, again.Conflict.Reason);
        Assert.Throws<ArgumentException>(() => database.UndoRelease(Release(set.Id, 1).Id, new SetReleaseUndoRequest { SkipNests = new[] { Guid.Empty } }));
    }

    [Fact]
    public void Undo_OfReleasesSharingANest_GoesNewestFirst()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        database.InsertDrawingSet(set.Id, set);
        var nest = AddNest(database);
        var first = Release(set.Id, 1, nest);
        database.InsertRelease(first);
        var second = Release(set.Id, 1, nest).Merged(0, 1, Resave(database, nest));
        database.InsertRelease(second);
        var stranger = Release(set.Id, 1, AddNest(database));
        database.InsertRelease(stranger);
        Assert.Contains("is not part", Assert.Throws<ArgumentException>(() =>
            database.UndoRelease(first.Id, new SetReleaseUndoRequest { SkipNests = stranger.Nests.Select(n => n.NestId).ToList() })).Message);

        Assert.Throws<ReleaseUndoRefusedException>(() => database.UndoRelease(first.Id, new SetReleaseUndoRequest()));
        database.UndoRelease(second.Id, new SetReleaseUndoRequest());
        // The later undo restored revision 1 as revision 3, which the first release still accepts.
        var undone = database.UndoRelease(first.Id, new SetReleaseUndoRequest())!;

        Assert.Equal(SetReleaseNestUndo.Deleted, undone.Nests.Single().Undo);
        Assert.Equal(4, database.Get(nest, deleted: true)!.Revision);
    }

    [Fact]
    public void Version8Releases_MigrateToOneSetLine_AndCreatedNests()
    {
        using var factory = new ServerFactory();
        var set = Set();
        Guid first, second;
        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            database.InsertDrawingSet(set.Id, set);
            first = AddNest(database, "First");
            second = AddNest(database, "Second");
            Resave(database, second);
        }
        var releaseId = Guid.NewGuid();
        Execute(factory.DatabasePath, $"""
            {ReleaseTablesBackToV8}
            INSERT INTO set_releases (id, setId, setRevision, multiplier, notes, releasedAt, releasedBy)
                VALUES ('{releaseId}', '{set.Id}', 1, 4, 'Legacy notes', '2026-10-20T09:00:00.0000000-04:00', 'Legacy operator');
            INSERT INTO set_release_nests (releaseId, position, nestId, materialName, materialGrade, thickness, units, cutType)
                VALUES ('{releaseId}', 0, '{first}', 'Synthetic Steel', 'A36', 0.25, 'Inches', 'Laser'),
                       ('{releaseId}', 1, '{second}', 'Synthetic Aluminum', '', 0.125, 'Millimeters', 'Waterjet');
            PRAGMA user_version=8;
            """);

        using var upgraded = new NestDatabase(factory.DatabasePath, pooling: false);

        Assert.Equal(14L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
        Assert.Equal(0L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'set_release%' OR name LIKE 'ix_set_release%'"));
        var release = upgraded.GetRelease(releaseId)!;
        Assert.Equal(("Legacy notes", "Legacy operator", false), (release.Notes, release.ReleasedBy, release.IsUndone));
        Assert.Equal((set.Id, 1, 4, "Synthetic kit"), (release.Sets.Single().SetId, release.Sets.Single().SetRevision, release.Sets.Single().Quantity, release.Sets.Single().SetName));
        Assert.Equal(new[] { (first, SetReleaseNestKind.Created, (int?)null, 1), (second, SetReleaseNestKind.Created, null, 1) },
            release.Nests.Select(n => (n.NestId, n.Kind, n.RevisionBefore, n.RevisionAfter)));
        Assert.Equal((Units.Millimeters, CutType.Waterjet, 0.125), (release.Nests[1].Units, release.Nests[1].CutType, release.Nests[1].Thickness));
        Assert.All(release.Nests, n => Assert.Empty(n.Items));
        // The second nest was saved after its release, so undoing must leave it unless skipped.
        var refused = Assert.Throws<ReleaseUndoRefusedException>(() => upgraded.UndoRelease(releaseId, new SetReleaseUndoRequest()));
        Assert.Equal(second, refused.Conflict.ChangedNests.Single().Id);
    }

    // Code-level ownership gate, as for the set operations.
    [Theory]
    [InlineData("InsertRelease")]
    [InlineData("GetRelease")]
    [InlineData("QueryReleases")]
    [InlineData("UndoRelease")]
    public async Task Concurrency_EveryReleaseOperationWaitsForTheSameMonitor(string operation)
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        database.InsertDrawingSet(set.Id, set);
        var nest = AddNest(database);
        var next = AddNest(database);
        var release = Release(set.Id, 1, nest);
        database.InsertRelease(release);
        var sync = typeof(NestDatabase).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() => completed.SetResult(Xunit.Record.Exception(() =>
        {
            switch (operation)
            {
                case "InsertRelease": database.InsertRelease(Release(set.Id, 1, next)); break;
                case "GetRelease": database.GetRelease(release.Id); break;
                case "QueryReleases": database.QueryReleases(set.Id, nest, 0, 100); break;
                case "UndoRelease": database.UndoRelease(release.Id, new SetReleaseUndoRequest()); break;
                default: throw new InvalidOperationException($"Unknown test operation: {operation}");
            }
        })))
        { IsBackground = true };

        bool observedWaiting;
        bool completedWhileHeld;
        lock (sync)
        {
            worker.Start();
            observedWaiting = SpinWait.SpinUntil(() => completed.Task.IsCompleted ||
                (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10));
            completedWhileHeld = completed.Task.IsCompleted;
        }

        Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "Operation did not finish after releasing the monitor.");
        Assert.True(observedWaiting, "Caller never reached the operation within the watchdog.");
        Assert.False(completedWhileHeld, operation + " bypassed the shared monitor.");
        Assert.Null(await completed.Task);
    }

    [Fact]
    public void Concurrency_AllReleaseSqlStepsOwnTheMonitor()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        database.InsertDrawingSet(set.Id, set);
        var nest = AddNest(database);
        var release = Release(set.Id, 1, nest);
        var sync = typeof(NestDatabase).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var connection = (SqliteConnection)typeof(NestDatabase)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var steps = 0;
        var unownedSteps = 0;
        SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 1, _ =>
        {
            steps++;
            if (!Monitor.IsEntered(sync))
                unownedSteps++;
            return 0;
        }, null);
        try
        {
            Action[] operations =
            [
                () => database.InsertRelease(release),
                () => database.GetRelease(release.Id),
                () => database.QueryReleases(set.Id, nest, 0, 100),
                () => database.UndoRelease(release.Id, new SetReleaseUndoRequest()),
            ];
            foreach (var operation in operations)
            {
                var before = steps;
                operation();
                Assert.True(steps > before, "Operation did not execute the observed SQLite VM.");
                Assert.Equal(0, unownedSteps);
            }
        }
        finally
        {
            SQLitePCL.raw.sqlite3_progress_handler(connection.Handle, 0, null, null);
        }
    }

    [Fact]
    public async Task Api_PostsQueriesAndReadsReleasesWithCamelCaseEnums()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = await Create(client, Set());
        var first = await AddNest(client, "Nest one");
        var second = await AddNest(client, "Nest two");
        var release = Release(set.Id, 1, first, second);
        release.Nests[1].Units = Units.Millimeters;
        release.Nests[1].CutType = CutType.Plasma;

        using var posted = await client.PostAsync("/api/releases", Json(JsonSerializer.Serialize(release, Options)));

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var json = JsonNode.Parse(await posted.Content.ReadAsStringAsync())!;
        Assert.Equal(set.Id.ToString(), json["sets"]![0]!["setId"]!.GetValue<string>());
        Assert.Equal("Synthetic kit", json["sets"]![0]!["setName"]!.GetValue<string>());
        Assert.Equal("millimeters", json["nests"]![1]!["units"]!.GetValue<string>());
        Assert.Equal("plasma", json["nests"]![1]!["cutType"]!.GetValue<string>());
        Assert.Equal("created", json["nests"]![1]!["kind"]!.GetValue<string>());
        Assert.Equal("draft", json["nests"]![0]!["nest"]!["status"]!.GetValue<string>());

        var page = (await client.GetFromJsonAsync<SetReleasePage>($"/api/releases/query?setId={set.Id}", Options))!;
        Assert.Equal(release.Id, page.Items.Single().Id);
        Assert.Equal("Nest two", page.Items.Single().Nests[1].Nest!.Name);
        var byNest = (await client.GetFromJsonAsync<SetReleasePage>($"/api/releases/query?nestId={second}&limit=5", Options))!;
        Assert.Equal((release.Id, 2), (byNest.Items.Single().Id, byNest.Items.Single().Sets.Single().Quantity));
        Assert.Equal(release.Id, (await client.GetFromJsonAsync<SetReleaseRecord>($"/api/releases/{release.Id}", Options))!.Id);

        Assert.Equal(0, (await client.GetFromJsonAsync<SetReleasePage>($"/api/releases/query?nestId={await AddNest(client)}", Options))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/releases/{Guid.NewGuid()}")).StatusCode);
        foreach (var query in new[] { "limit=0", "limit=201", "offset=-1", "setId=x", $"setId={Guid.Empty}", "nestId=1&nestId=2", "other=1" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/releases/query?{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sets/{set.Id}/releases")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/nests/{first}/release")).StatusCode);
    }

    [Fact]
    public async Task Api_Conflicts_ReturnTheReleaseHoldingTheIdOrCreatedNest()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = await Create(client, Set());
        var taken = await AddNest(client);
        var owner = Release(set.Id, 1, taken);
        using (await client.PostAsync("/api/releases", Json(JsonSerializer.Serialize(owner, Options)))) { }

        var reused = Release(set.Id, 1, await AddNest(client));
        reused.Id = owner.Id;
        foreach (var body in new[] { Release(set.Id, 1, await AddNest(client), taken), reused })
        {
            using var response = await client.PostAsync("/api/releases", Json(JsonSerializer.Serialize(body, Options)));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(owner.Id, (await response.Content.ReadFromJsonAsync<SetReleaseRecord>(Options))!.Id);
        }

        Assert.Equal(1, (await client.GetFromJsonAsync<SetReleasePage>($"/api/releases/query?setId={set.Id}", Options))!.Total);
    }

    [Fact]
    public async Task Api_Undo_ReturnsTheUndoneRelease_OrWhyNot()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = await Create(client, Set());
        var nest = await AddNest(client);
        var release = Release(set.Id, 1, nest);
        using (await client.PostAsync("/api/releases", Json(JsonSerializer.Serialize(release, Options)))) { }

        using var undone = await client.PostAsync($"/api/releases/{release.Id}/undo", Json("""{ "undoneBy": "Synthetic undoer" }"""));
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        var json = JsonNode.Parse(await undone.Content.ReadAsStringAsync())!;
        Assert.Equal("Synthetic undoer", json["undoneBy"]!.GetValue<string>());
        Assert.Equal("deleted", json["nests"]![0]!["undo"]!.GetValue<string>());

        using var again = await client.PostAsync($"/api/releases/{release.Id}/undo", Json("{}"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("alreadyUndone", JsonNode.Parse(await again.Content.ReadAsStringAsync())!["reason"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/releases/{Guid.NewGuid()}/undo", Json("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/releases/{release.Id}/undo", Json("[1]"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/releases/{release.Id}/undo",
            Json($$"""{ "skipNests": ["{{Guid.Empty}}"] }"""))).StatusCode);
    }

    public static TheoryData<string, string> RejectedReleases()
    {
        var valid = JsonSerializer.Serialize(Release(SetA, 1, NestA), Options);
        string With(string property, JsonNode? value)
        {
            var node = JsonNode.Parse(valid)!.AsObject();
            node[property] = value;
            return node.ToJsonString();
        }
        string WithSet(string property, JsonNode? value)
        {
            var node = JsonNode.Parse(valid)!.AsObject();
            node["sets"]![0]![property] = value;
            return node.ToJsonString();
        }
        string WithNest(string property, JsonNode? value)
        {
            var node = JsonNode.Parse(valid)!.AsObject();
            node["nests"]![0]![property] = value;
            return node.ToJsonString();
        }
        string Without(string property)
        {
            var node = JsonNode.Parse(valid)!.AsObject();
            node.Remove(property);
            return node.ToJsonString();
        }

        return new TheoryData<string, string>
        {
            { "empty id", With("id", Guid.Empty.ToString()) },
            { "missing sets", Without("sets") },
            { "no sets", With("sets", new JsonArray()) },
            { "unknown set", WithSet("setId", Guid.NewGuid().ToString()) },
            { "zero set revision", WithSet("setRevision", 0) },
            { "unknown set revision", WithSet("setRevision", 2) },
            { "zero quantity", WithSet("quantity", 0) },
            { "quantity over the limit", WithSet("quantity", 1001) },
            { "repeated set", With("sets", new JsonArray(JsonNode.Parse(valid)!["sets"]![0]!.DeepClone(), JsonNode.Parse(valid)!["sets"]![0]!.DeepClone())) },
            { "long notes", With("notes", new string('x', 4001)) },
            { "null released by", With("releasedBy", null) },
            { "missing nests", Without("nests") },
            { "null nests", With("nests", null) },
            { "no nests", With("nests", new JsonArray()) },
            { "repeated nest", With("nests", new JsonArray(JsonNode.Parse(valid)!["nests"]![0]!.DeepClone(), JsonNode.Parse(valid)!["nests"]![0]!.DeepClone())) },
            { "unknown nest", WithNest("nestId", Guid.NewGuid().ToString()) },
            { "empty nest id", WithNest("nestId", Guid.Empty.ToString()) },
            { "integer kind", WithNest("kind", 1) },
            { "unknown kind", WithNest("kind", "copied") },
            { "created with a before revision", WithNest("revisionBefore", 1) },
            { "merged without a before revision", WithNest("kind", "merged") },
            { "zero after revision", WithNest("revisionAfter", 0) },
            { "integer units", WithNest("units", 0) },
            { "unknown units", WithNest("units", "Feet") },
            { "combined cut type", WithNest("cutType", "Laser, Plasma") },
            { "integer cut type", WithNest("cutType", 1) },
            { "negative thickness", WithNest("thickness", -1) },
            { "null material", WithNest("materialName", null) },
            { "long grade", WithNest("materialGrade", new string('g', 201)) },
            { "null items", WithNest("items", null) },
            { "item of another set", WithNest("items", JsonNode.Parse($$"""[{ "setId": "{{Guid.NewGuid()}}", "drawingId": "{{Guid.NewGuid()}}", "drawingRevision": 1, "quantity": 1 }]""")) },
            { "item with no quantity", WithNest("items", JsonNode.Parse($$"""[{ "setId": "{{SetA}}", "drawingId": "{{Guid.NewGuid()}}", "drawingRevision": 1, "quantity": 0 }]""")) },
            { "not an object", "[1]" },
            { "malformed", "{" },
        };
    }

    private static readonly Guid NestA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid SetA = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    [Theory]
    [MemberData(nameof(RejectedReleases))]
    public async Task Api_InvalidRelease_IsRejectedWithNoState(string row, string json)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        await Create(client, Set(id: SetA));
        await AddNest(client, id: NestA);

        using var response = await client.PostAsync("/api/releases", Json(json));

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, row + ": " + response.StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<SetReleasePage>("/api/releases/query", Options))!.Total);
    }

    [Fact]
    public async Task Api_JoinedAndServerComputedValuesInTheBody_AreIgnored()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = await Create(client, Set());
        var node = JsonNode.Parse(JsonSerializer.Serialize(Release(set.Id, 1, await AddNest(client)), Options))!;
        node["nests"]![0]!["nest"] = new JsonObject { ["status"] = 99, ["name"] = 5 };
        node["nests"]![0]!["undo"] = 7;
        node["sets"]![0]!["setName"] = 3;
        node["undoneAt"] = "2026-10-21T09:00:00Z";
        node["undoneBy"] = 4;

        using var response = await client.PostAsync("/api/releases", Json(node.ToJsonString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = (await response.Content.ReadFromJsonAsync<SetReleaseRecord>(Options))!;
        Assert.Equal((false, "Synthetic kit", (SetReleaseNestUndo?)null), (stored.IsUndone, stored.Sets[0].SetName, stored.Nests[0].Undo));
    }

    private static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
