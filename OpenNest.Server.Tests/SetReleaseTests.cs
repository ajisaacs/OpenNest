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
    public void Release_OfAStaleRevision_IsStoredAndJoinsItsSetNameAndNests()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var drawing = AddDrawing(database);
        var set = Set("Kit as released", members: (drawing, 1));
        database.InsertDrawingSet(set.Id, set);
        database.UpdateDrawingSet(set.Id, Set("Kit renamed later", set.Id, (drawing, 3)), 1);
        var first = AddNest(database, "Nest one");
        var second = AddNest(database, "Nest two");
        var release = Release(1, first, second);
        release.Nests[1].Thickness = 0.123456;
        release.Nests[1].Units = Units.Millimeters;
        release.Nests[1].CutType = CutType.Waterjet;

        var stored = database.InsertSetRelease(set.Id, release)!;

        Assert.Equal((release.Id, set.Id, 1, "Kit as released", 2), (stored.Id, stored.SetId, stored.SetRevision, stored.SetName, stored.Multiplier));
        Assert.Equal(("Synthetic release", "Synthetic operator"), (stored.Notes, stored.ReleasedBy));
        Assert.NotEqual(default, stored.ReleasedAt);
        Assert.Equal(new[] { first, second }, stored.Nests.Select(n => n.NestId));
        Assert.Equal(("Synthetic Steel 1", "Grade A", 0.1235, Units.Millimeters, CutType.Waterjet),
            (stored.Nests[1].MaterialName, stored.Nests[1].MaterialGrade, stored.Nests[1].Thickness, stored.Nests[1].Units, stored.Nests[1].CutType));
        Assert.Equal(("Nest one", NestStatus.Draft, false), (stored.Nests[0].Nest!.Name, stored.Nests[0].Nest!.Status, stored.Nests[0].Nest!.Deleted));
        Assert.Equal(release.Id, database.ReleaseForNest(second)!.Id);
        Assert.Null(database.ReleaseForNest(AddNest(database)));
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
        database.InsertSetRelease(set.Id, Release(1, cut, removed));

        var record = database.Get(cut)!;
        record.Status = NestStatus.HasBeenCut;
        database.UpdateMetadata(cut, record, 1);
        database.SetDeleted(removed, true, 1);

        var nests = database.SetReleases(set.Id, 0, 100)!.Items.Single().Nests;
        Assert.Equal((NestStatus.HasBeenCut, false), (nests[0].Nest!.Status, nests[0].Nest!.Deleted));
        Assert.True(nests[1].Nest!.Deleted);
        Assert.True(database.ReleaseForNest(removed)!.Nests[1].Nest!.Deleted);
    }

    [Fact]
    public void Releases_PageNewestFirstPerSetAndAcceptAnArchivedSet()
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
            var release = Release(1, AddNest(database));
            ids.Add(release.Id);
            database.InsertSetRelease(set.Id, release);
            Thread.Sleep(5);
        }
        database.InsertSetRelease(other.Id, Release(1, AddNest(database)));
        database.SetDrawingSetArchived(set.Id, true, 1);
        var afterArchive = Release(1, AddNest(database));
        Assert.NotNull(database.InsertSetRelease(set.Id, afterArchive));
        ids.Add(afterArchive.Id);

        var page = database.SetReleases(set.Id, 1, 2)!;
        Assert.Equal(4, page.Total);
        Assert.Equal(new[] { ids[2], ids[1] }, page.Items.Select(r => r.Id));
        Assert.Single(database.SetReleases(other.Id, 0, 100)!.Items);
        Assert.Null(database.SetReleases(Guid.NewGuid(), 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => database.SetReleases(set.Id, 0, 201));
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
        var owner = Release(1, taken);
        database.InsertSetRelease(set.Id, owner);

        Assert.Null(database.InsertSetRelease(Guid.NewGuid(), Release(1, free)));
        Assert.Contains("no revision 2", Assert.Throws<ArgumentException>(() => database.InsertSetRelease(set.Id, Release(2, free))).Message);
        Assert.Contains("does not exist", Assert.Throws<ArgumentException>(() => database.InsertSetRelease(set.Id, Release(1, free, Guid.NewGuid()))).Message);
        Assert.Contains("is deleted", Assert.Throws<ArgumentException>(() => database.InsertSetRelease(set.Id, Release(1, free, deleted))).Message);
        Assert.Throws<ArgumentException>(() => database.InsertSetRelease(set.Id, Release(1, free, free)));
        Assert.Throws<ArgumentException>(() => database.InsertSetRelease(set.Id, Release(1)));
        var noId = Release(1, free);
        noId.Id = Guid.Empty;
        Assert.Throws<ArgumentException>(() => database.InsertSetRelease(set.Id, noId));
        var tooMany = Release(1, free);
        tooMany.Multiplier = SetReleaseRules.MaxMultiplier + 1;
        Assert.Throws<ArgumentException>(() => database.InsertSetRelease(set.Id, tooMany));

        var claimed = Assert.Throws<SetReleaseConflictException>(() => database.InsertSetRelease(set.Id, Release(1, free, taken)));
        Assert.Equal(owner.Id, claimed.Current.Id);
        var reused = Release(1, free);
        reused.Id = owner.Id;
        Assert.Equal(owner.Id, Assert.Throws<SetReleaseConflictException>(() => database.InsertSetRelease(set.Id, reused)).Current.Id);

        Assert.Equal(1L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM set_releases"));
        Assert.Equal(1L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM set_release_nests"));
        Assert.Null(database.ReleaseForNest(free));
    }

    // Code-level ownership gate, as for the set operations.
    [Theory]
    [InlineData("InsertSetRelease")]
    [InlineData("SetReleases")]
    [InlineData("ReleaseForNest")]
    public async Task Concurrency_EveryReleaseOperationWaitsForTheSameMonitor(string operation)
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var set = Set();
        database.InsertDrawingSet(set.Id, set);
        var nest = AddNest(database);
        var next = AddNest(database);
        database.InsertSetRelease(set.Id, Release(1, nest));
        var sync = typeof(NestDatabase).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        var completed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() => completed.SetResult(Xunit.Record.Exception(() =>
        {
            switch (operation)
            {
                case "InsertSetRelease": database.InsertSetRelease(set.Id, Release(1, next)); break;
                case "SetReleases": database.SetReleases(set.Id, 0, 100); break;
                case "ReleaseForNest": database.ReleaseForNest(nest); break;
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
                () => database.InsertSetRelease(set.Id, Release(1, nest)),
                () => database.SetReleases(set.Id, 0, 100),
                () => database.ReleaseForNest(nest),
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
    public async Task Api_PostsAndReadsReleasesWithCamelCaseEnums()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = await Create(client, Set());
        var first = await AddNest(client, "Nest one");
        var second = await AddNest(client, "Nest two");
        var release = Release(1, first, second);
        release.Nests[1].Units = Units.Millimeters;
        release.Nests[1].CutType = CutType.Plasma;

        using var posted = await client.PostAsync($"/api/sets/{set.Id}/releases", Json(JsonSerializer.Serialize(release, Options)));

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var json = JsonNode.Parse(await posted.Content.ReadAsStringAsync())!;
        Assert.Equal(set.Id.ToString(), json["setId"]!.GetValue<string>());
        Assert.Equal("Synthetic kit", json["setName"]!.GetValue<string>());
        Assert.Equal("millimeters", json["nests"]![1]!["units"]!.GetValue<string>());
        Assert.Equal("plasma", json["nests"]![1]!["cutType"]!.GetValue<string>());
        Assert.Equal("draft", json["nests"]![0]!["nest"]!["status"]!.GetValue<string>());

        var page = (await client.GetFromJsonAsync<SetReleasePage>($"/api/sets/{set.Id}/releases", Options))!;
        Assert.Equal(release.Id, page.Items.Single().Id);
        Assert.Equal("Nest two", page.Items.Single().Nests[1].Nest!.Name);
        var fromNest = (await client.GetFromJsonAsync<SetReleaseRecord>($"/api/nests/{second}/release", Options))!;
        Assert.Equal((release.Id, 2), (fromNest.Id, fromNest.Multiplier));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/nests/{await AddNest(client)}/release")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sets/{Guid.NewGuid()}/releases")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/sets/{set.Id}/releases?limit=0")).StatusCode);
        using var unknownSet = await client.PostAsync($"/api/sets/{Guid.NewGuid()}/releases",
            Json(JsonSerializer.Serialize(Release(1, await AddNest(client)), Options)));
        Assert.Equal(HttpStatusCode.NotFound, unknownSet.StatusCode);
    }

    [Fact]
    public async Task Api_Conflicts_ReturnTheReleaseHoldingTheIdOrNest()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = await Create(client, Set());
        var taken = await AddNest(client);
        var owner = Release(1, taken);
        using (await client.PostAsync($"/api/sets/{set.Id}/releases", Json(JsonSerializer.Serialize(owner, Options)))) { }

        var reused = Release(1, await AddNest(client));
        reused.Id = owner.Id;
        foreach (var body in new[] { Release(1, await AddNest(client), taken), reused })
        {
            using var response = await client.PostAsync($"/api/sets/{set.Id}/releases", Json(JsonSerializer.Serialize(body, Options)));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(owner.Id, (await response.Content.ReadFromJsonAsync<SetReleaseRecord>(Options))!.Id);
        }

        Assert.Equal(1, (await client.GetFromJsonAsync<SetReleasePage>($"/api/sets/{set.Id}/releases", Options))!.Total);
    }

    public static TheoryData<string, string> RejectedReleases()
    {
        var valid = JsonSerializer.Serialize(Release(1, NestA), Options);
        string With(string property, JsonNode? value)
        {
            var node = JsonNode.Parse(valid)!.AsObject();
            node[property] = value;
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
            { "other set id", With("setId", Guid.NewGuid().ToString()) },
            { "zero set revision", With("setRevision", 0) },
            { "unknown set revision", With("setRevision", 2) },
            { "zero multiplier", With("multiplier", 0) },
            { "multiplier over the limit", With("multiplier", 1001) },
            { "long notes", With("notes", new string('x', 4001)) },
            { "null released by", With("releasedBy", null) },
            { "missing nests", Without("nests") },
            { "null nests", With("nests", null) },
            { "no nests", With("nests", new JsonArray()) },
            { "repeated nest", With("nests", new JsonArray(JsonNode.Parse(valid)!["nests"]![0]!.DeepClone(), JsonNode.Parse(valid)!["nests"]![0]!.DeepClone())) },
            { "unknown nest", WithNest("nestId", Guid.NewGuid().ToString()) },
            { "empty nest id", WithNest("nestId", Guid.Empty.ToString()) },
            { "integer units", WithNest("units", 0) },
            { "unknown units", WithNest("units", "Feet") },
            { "combined cut type", WithNest("cutType", "Laser, Plasma") },
            { "integer cut type", WithNest("cutType", 1) },
            { "negative thickness", WithNest("thickness", -1) },
            { "null material", WithNest("materialName", null) },
            { "long grade", WithNest("materialGrade", new string('g', 201)) },
            { "not an object", "[1]" },
            { "malformed", "{" },
        };
    }

    private static readonly Guid NestA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    [Theory]
    [MemberData(nameof(RejectedReleases))]
    public async Task Api_InvalidRelease_IsRejectedWithNoState(string row, string json)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = await Create(client, Set());
        await AddNest(client, id: NestA);

        using var response = await client.PostAsync($"/api/sets/{set.Id}/releases", Json(json));

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, row + ": " + response.StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<SetReleasePage>($"/api/sets/{set.Id}/releases", Options))!.Total);
    }

    [Fact]
    public async Task Api_JoinedNestInTheBody_IsIgnored()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var set = await Create(client, Set());
        var node = JsonNode.Parse(JsonSerializer.Serialize(Release(1, await AddNest(client)), Options))!;
        node["nests"]![0]!["nest"] = new JsonObject { ["status"] = 99, ["name"] = 5 };

        using var response = await client.PostAsync($"/api/sets/{set.Id}/releases", Json(node.ToJsonString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
