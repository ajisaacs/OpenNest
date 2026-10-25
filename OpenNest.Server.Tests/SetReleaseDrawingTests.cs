using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OpenNest.Data;
using OpenNest.IO;
using OpenNest.Shapes;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;
using static OpenNest.Server.Tests.DrawingSetFixtures;

namespace OpenNest.Server.Tests;

/// <summary>
/// Library drawings released on their own (schema v11): stored beside or instead of sets,
/// items without a set, the drawingId query filter, the v10 upgrade and the whole path
/// through the real clients, including undo.
/// </summary>
public sealed class SetReleaseDrawingTests
{
    // A release of drawings only: one nest per id, each holding the first drawing's quantity.
    private static SetReleaseRecord DrawingRelease(Guid drawing, int quantity, params Guid[] nests)
    {
        var release = Release(Guid.Empty, 1, nests);
        release.Sets = Array.Empty<SetReleaseLine>();
        release.Drawings = new[] { new SetReleaseDrawing { DrawingId = drawing, Quantity = quantity } };
        foreach (var nest in release.Nests)
            nest.Items = new[] { new SetReleaseNestItem { DrawingId = drawing, DrawingRevision = 1, Quantity = quantity } };
        return release;
    }

    [Fact]
    public void DrawingsOnTheirOwn_AreStoredWithSetsAndItemsWithoutASet()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var bracket = AddDrawing(database, "Synthetic bracket");
        var plate = AddDrawing(database, "Synthetic plate");
        var kit = Set(members: (bracket, 1));
        database.InsertDrawingSet(kit.Id, kit);
        var nest = AddNest(database);
        var release = Release(kit.Id, 1, nest);
        release.Drawings = new[]
        {
            new SetReleaseDrawing { DrawingId = plate, Quantity = 40, DrawingName = "ignored" },
            new SetReleaseDrawing { DrawingId = bracket, Quantity = 5 },
        };
        release.Nests[0].Items = new[]
        {
            new SetReleaseNestItem { SetId = kit.Id, DrawingId = bracket, DrawingRevision = 1, Quantity = 2 },
            new SetReleaseNestItem { DrawingId = bracket, DrawingRevision = 1, Quantity = 5 },
            new SetReleaseNestItem { DrawingId = plate, DrawingRevision = 1, Quantity = 40 },
        };

        var stored = database.InsertRelease(release);

        Assert.Equal(new[] { (plate, 40, "Synthetic plate"), (bracket, 5, "Synthetic bracket") },
            stored.Drawings.Select(d => (d.DrawingId, d.Quantity, d.DrawingName)));
        Assert.Equal(new[] { ((Guid?)kit.Id, bracket, 2), (null, bracket, 5), (null, plate, 40) },
            stored.Nests[0].Items.Select(i => (i.SetId, i.DrawingId, i.Quantity)));
        Assert.Equal(release.Id, database.QueryReleases(null, null, 0, 100, drawingId: plate).Items.Single().Id);
        Assert.Equal(release.Id, database.QueryReleases(kit.Id, null, 0, 100, drawingId: bracket).Items.Single().Id);
        Assert.Empty(database.QueryReleases(null, null, 0, 100, drawingId: AddDrawing(database, "Never released")).Items);
        Assert.Equal(2L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM release_drawings"));
    }

    [Fact]
    public void ReleaseOfDrawingsOnly_IsStored_AndUndoDeletesItsNest()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var drawing = AddDrawing(database);
        var created = AddNest(database, "Created nest");
        var draft = AddNest(database, "Draft nest");
        var release = DrawingRelease(drawing, 12, created, draft).Merged(1, 1, Resave(database, draft));

        var stored = database.InsertRelease(release);
        var undone = database.UndoRelease(release.Id, new SetReleaseUndoRequest { UndoneBy = "Synthetic undoer" })!;

        Assert.Empty(stored.Sets);
        Assert.Equal((drawing, 12), (stored.Drawings.Single().DrawingId, stored.Drawings.Single().Quantity));
        Assert.Equal(new SetReleaseNestUndo?[] { SetReleaseNestUndo.Deleted, SetReleaseNestUndo.Restored }, undone.Nests.Select(n => n.Undo));
        Assert.Null(database.Get(created));
        Assert.Equal(1, database.Get(draft)!.RestoredFromRevision);
        Assert.Equal("Released from drawing 'Synthetic bracket' ×12 by Synthetic operator (undone)",
            SetReleaseText.Origin(new SetReleaseRecord
            {
                Drawings = undone.Drawings,
                Nests = undone.Nests,
                ReleasedBy = undone.ReleasedBy,
                UndoneAt = undone.UndoneAt,
            }));
    }

    [Fact]
    public void Refusals_ForDrawingsLeaveNoState()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var drawing = AddDrawing(database);
        var nest = AddNest(database);

        Assert.Contains("does not exist", Assert.Throws<ArgumentException>(() =>
            database.InsertRelease(DrawingRelease(Guid.NewGuid(), 1, nest))).Message);
        var twice = DrawingRelease(drawing, 1, nest);
        twice.Drawings = twice.Drawings.Concat(twice.Drawings).ToList();
        Assert.Contains("repeats drawing", Assert.Throws<ArgumentException>(() => database.InsertRelease(twice)).Message);
        var stranger = DrawingRelease(drawing, 1, nest);
        stranger.Nests[0].Items = new[] { new SetReleaseNestItem { DrawingId = Guid.NewGuid(), DrawingRevision = 1, Quantity = 1 } };
        Assert.Contains("one of the release's drawings", Assert.Throws<ArgumentException>(() => database.InsertRelease(stranger)).Message);
        var tooMany = DrawingRelease(drawing, SetReleaseRules.MaxDrawingQuantity + 1, nest);
        Assert.Throws<ArgumentException>(() => database.InsertRelease(tooMany));
        var nothing = DrawingRelease(drawing, 1, nest);
        nothing.Drawings = Array.Empty<SetReleaseDrawing>();
        nothing.Nests[0].Items = Array.Empty<SetReleaseNestItem>();
        Assert.Contains("at least one set or drawing", Assert.Throws<ArgumentException>(() => database.InsertRelease(nothing)).Message);

        Assert.Equal(0L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM releases"));
        Assert.Equal(0L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM release_drawings"));
    }

    [Fact]
    public void Version10Items_SurviveTheUpgrade_AndMayThenGoWithoutASet()
    {
        using var factory = new ServerFactory();
        var drawing = Guid.Empty;
        var kit = Set();
        var releaseId = Guid.Empty;
        using (var database = new NestDatabase(factory.DatabasePath, pooling: false))
        {
            drawing = AddDrawing(database);
            kit = Set(members: (drawing, 1));
            database.InsertDrawingSet(kit.Id, kit);
            var release = Release(kit.Id, 1, AddNest(database));
            release.Nests[0].Items = new[] { new SetReleaseNestItem { SetId = kit.Id, DrawingId = drawing, DrawingRevision = 1, Quantity = 2 } };
            releaseId = database.InsertRelease(release).Id;
        }
        // The v10 layout: no release_drawings, and every item names a set.
        Execute(factory.DatabasePath, """
            DROP TABLE release_drawings;
            CREATE TABLE items_v10 (
                releaseId TEXT NOT NULL, nestId TEXT NOT NULL, position INTEGER NOT NULL, setId TEXT NOT NULL,
                drawingId TEXT NOT NULL, drawingRevision INTEGER NOT NULL, quantity INTEGER NOT NULL,
                PRIMARY KEY (releaseId, nestId, position), UNIQUE (releaseId, nestId, setId, drawingId),
                FOREIGN KEY (releaseId, nestId) REFERENCES release_nests(releaseId, nestId));
            INSERT INTO items_v10 SELECT * FROM release_nest_items;
            DROP TABLE release_nest_items;
            ALTER TABLE items_v10 RENAME TO release_nest_items;
            PRAGMA user_version=10;
            """);

        using var upgraded = new NestDatabase(factory.DatabasePath, pooling: false);

        Assert.Equal(12L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
        var kept = upgraded.GetRelease(releaseId)!;
        Assert.Empty(kept.Drawings);
        Assert.Equal(((Guid?)kit.Id, drawing, 2), (kept.Nests[0].Items.Single().SetId, kept.Nests[0].Items.Single().DrawingId, kept.Nests[0].Items.Single().Quantity));
        Assert.Equal(1L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM pragma_table_info('release_nest_items') WHERE name='setId' AND \"notnull\"=0"));
        Assert.Single(upgraded.InsertRelease(DrawingRelease(drawing, 3, AddNest(upgraded))).Drawings);
    }

    [Fact]
    public async Task Api_PostsDrawingsWithANullItemSet_AndFiltersByDrawing()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var drawing = await AddDrawing(client, "Synthetic gusset");
        var release = DrawingRelease(drawing, 7, await AddNest(client));

        using var posted = await client.PostAsync("/api/releases", Json(JsonSerializer.Serialize(release, Options)));

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var json = JsonNode.Parse(await posted.Content.ReadAsStringAsync())!;
        Assert.Empty(json["sets"]!.AsArray());
        Assert.Equal("Synthetic gusset", json["drawings"]![0]!["drawingName"]!.GetValue<string>());
        Assert.Null(json["nests"]![0]!["items"]![0]!["setId"]);
        var page = (await client.GetFromJsonAsync<SetReleasePage>($"/api/releases/query?drawingId={drawing}", Options))!;
        Assert.Equal(release.Id, page.Items.Single().Id);
        Assert.Equal(0, (await client.GetFromJsonAsync<SetReleasePage>($"/api/releases/query?drawingId={Guid.NewGuid()}", Options))!.Total);
        foreach (var query in new[] { "drawingId=x", $"drawingId={Guid.Empty}", $"drawingId={drawing}&drawingId={drawing}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/releases/query?{query}")).StatusCode);

        // A client from before drawings on their own sends no drawings at all.
        var set = await Create(client, Set());
        var legacy = JsonNode.Parse(JsonSerializer.Serialize(Release(set.Id, 1, await AddNest(client)), Options))!.AsObject();
        legacy.Remove("drawings");
        using var old = await client.PostAsync("/api/releases", Json(legacy.ToJsonString()));
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
    }

    public static TheoryData<string, string> RejectedDrawingReleases()
    {
        var valid = JsonSerializer.Serialize(DrawingRelease(DrawingA, 2, NestA), Options);
        string With(Action<JsonObject> edit)
        {
            var node = JsonNode.Parse(valid)!.AsObject();
            edit(node);
            return node.ToJsonString();
        }

        return new TheoryData<string, string>
        {
            { "no sets and no drawings", With(n => { n["drawings"] = new JsonArray(); n["nests"]![0]!["items"] = new JsonArray(); }) },
            { "null drawings", With(n => n["drawings"] = null) },
            { "unknown drawing", With(n => n["drawings"]![0]!["drawingId"] = Guid.NewGuid().ToString()) },
            { "empty drawing id", With(n => n["drawings"]![0]!["drawingId"] = Guid.Empty.ToString()) },
            { "zero quantity", With(n => n["drawings"]![0]!["quantity"] = 0) },
            { "quantity over the limit", With(n => n["drawings"]![0]!["quantity"] = SetReleaseRules.MaxDrawingQuantity + 1) },
            { "repeated drawing", With(n => n["drawings"]!.AsArray().Add(n["drawings"]![0]!.DeepClone())) },
            { "item of a drawing not released", With(n => n["nests"]![0]!["items"]![0]!["drawingId"] = Guid.NewGuid().ToString()) },
            { "repeated item", With(n => n["nests"]![0]!["items"]!.AsArray().Add(n["nests"]![0]!["items"]![0]!.DeepClone())) },
        };
    }

    private static readonly Guid NestA = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid DrawingA = Guid.Parse("00000000-0000-0000-0000-0000000000c2");

    [Theory]
    [MemberData(nameof(RejectedDrawingReleases))]
    public async Task Api_InvalidDrawingRelease_IsRejectedWithNoState(string row, string json)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        await AddDrawing(client, edit: r => r.Id = DrawingA);
        await AddNest(client, id: NestA);

        using var response = await client.PostAsync("/api/releases", Json(json));

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, row + ": " + response.StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<SetReleasePage>("/api/releases/query", Options))!.Total);
    }

    [Fact]
    public async Task Client_ConfirmsSupport_PostsAndQueriesByDrawing()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var store = new RemoteDrawingSetStore(client, client.BaseAddress!.ToString());
        var drawing = await AddDrawing(client, "Synthetic gusset");
        var release = DrawingRelease(drawing, 9, await AddNest(client));

        await store.EnsureDrawingReleasesSupportedAsync();
        var stored = await store.PostReleaseAsync(release);

        Assert.Equal(("Synthetic gusset", 9), (stored.Drawings.Single().DrawingName, stored.Drawings.Single().Quantity));
        Assert.Equal(release.Id, (await store.QueryReleasesAsync(drawingId: drawing)).Items.Single().Id);
        Assert.Empty((await store.QueryReleasesAsync(drawingId: Guid.NewGuid())).Items);
        var unknown = DrawingRelease(Guid.NewGuid(), 1, await AddNest(client));
        Assert.DoesNotContain("does not support", (await Assert.ThrowsAsync<IOException>(() => store.PostReleaseAsync(unknown))).Message);
    }

    [Fact]
    public async Task Release_OfASetAndDrawingsOnTheirOwn_SharesNests_RecordsBoth_AndUndoes()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var url = client.BaseAddress!.ToString();
        using var library = new RemoteDrawingLibrary(client, url);
        using var sets = new RemoteDrawingSetStore(client, url);
        using var nests = new RemoteNestRepository(client, url);
        async Task<DrawingLibraryRecord> Store(string name, double length, string material)
        {
            var record = new DrawingLibraryRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                Customer = "Synthetic customer",
                MaterialName = material,
                Thickness = 0.25,
                Units = Units.Inches,
                CutType = CutType.Laser,
                SavedBy = "Synthetic operator",
            };
            var drawing = new RectangleShape { Name = name, Length = length, Width = 2 }.GetDrawing();
            return await library.UploadAsync(record, DrawingPackage.Write(drawing, Units.Inches));
        }
        var a = await Store("SYN-A", 4, "Synthetic Steel");
        var b = await Store("SYN-B", 5, "Synthetic Steel");
        var c = await Store("SYN-C", 6, "Synthetic Aluminum");
        var kit = await sets.GetAsync((await sets.CreateAsync(new DrawingSetRecord
        {
            Id = Guid.NewGuid(),
            Name = "Synthetic kit",
            Customer = "Synthetic customer",
            Members = new[] { new DrawingSetMember { DrawingId = a.Id, Quantity = 2 } },
        })).Id);
        var plan = SetReleasePlan.Build(new[] { new SetReleasePlanLine(kit!, 3) },
            new[] { new SetReleaseDrawingLine(a, 1), new SetReleaseDrawingLine(b, 4), new SetReleaseDrawingLine(c, 10) },
            includeArchived: false);
        var number = 0;
        var session = new SetReleaseSession(sets, library, nests, url,
            bytes =>
            {
                var contents = DrawingPackage.Read(bytes);
                return new LibraryPackage(bytes, contents.Drawing, contents.Units, contents.Warnings);
            },
            nest =>
            {
                using var stream = new MemoryStream();
                new NestWriter(nest).Write(stream);
                return stream.ToArray();
            },
            (group, _) => Task.FromResult(new Nest($"Synthetic loose {++number}") { Units = group.Units }));

        var result = await session.RunAsync(plan, plan.Groups, "Synthetic operator", "");

        Assert.Equal("", result.ReleaseError);
        var release = result.Release!;
        Assert.Equal(new[] { (a.Id, 1, "SYN-A"), (b.Id, 4, "SYN-B"), (c.Id, 10, "SYN-C") },
            release.Drawings.Select(d => (d.DrawingId, d.Quantity, d.DrawingName)));
        var steel = release.Nests.Single(n => n.MaterialName == "Synthetic Steel");
        Assert.Equal(new[] { ((Guid?)kit!.Id, a.Id, 6), (null, a.Id, 1), (null, b.Id, 4) },
            steel.Items.Select(i => (i.SetId, i.DrawingId, i.Quantity)));
        Assert.Equal(new[] { release.Id }, (await sets.QueryReleasesAsync(drawingId: c.Id)).Items.Select(r => r.Id));
        Assert.Equal(new[] { release.Id }, (await sets.QueryReleasesAsync(setId: kit.Id)).Items.Select(r => r.Id));
        Assert.Equal("Released from set 'Synthetic kit' revision 1 ×3 and drawings 'SYN-A' ×1, 'SYN-B' ×4 and 'SYN-C' ×10",
            SetReleaseText.Origin(new SetReleaseRecord { Sets = release.Sets, Drawings = release.Drawings }));

        var undone = await sets.UndoReleaseAsync(release.Id, new SetReleaseUndoRequest { UndoneBy = "Synthetic operator" });
        Assert.All(undone.Nests, n => Assert.Equal(SetReleaseNestUndo.Deleted, n.Undo));
        foreach (var group in result.Groups)
            Assert.Null(await nests.GetMetadataAsync(group.Record!.Id));
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
