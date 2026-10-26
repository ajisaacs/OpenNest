using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

/// <summary>
/// The settings every workstation shares (<c>/api/settings</c>): revision-guarded documents,
/// the v13 upgrade, the HTTP contract, and the desktop's store with its offline copy.
/// </summary>
public sealed class SharedSettingsTests
{
    private const string PostKey = "Synthetic.Posts.Laser";

    [Fact]
    public void Database_RevisesConflictsAndContinuesRevisionsAfterADelete()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);

        var (created, isNew) = database.CreateSharedSetting("post", PostKey, Write("""{"feed":1}"""));
        var (same, sameIsNew) = database.CreateSharedSetting("post", PostKey, Write("""{"feed":1}"""));
        var updated = database.UpdateSharedSetting("post", PostKey, Write("""{"feed":2}"""), created.Revision);
        var unchanged = database.UpdateSharedSetting("post", PostKey, Write("""{"feed":2}"""), updated!.Revision);

        Assert.Equal((1, true, 1, false, 2, 2), (created.Revision, isNew, same.Revision, sameIsNew, updated.Revision, unchanged!.Revision));
        var stale = Assert.Throws<SharedSettingConflictException>(() =>
            database.UpdateSharedSetting("post", PostKey, Write("""{"feed":3}"""), created.Revision));
        Assert.Equal(2, stale.Current.Revision);
        Assert.Throws<SharedSettingConflictException>(() => database.CreateSharedSetting("post", PostKey, Write("""{"feed":3}""")));

        Assert.True(database.DeleteSharedSetting("post", PostKey, "tester", 2));
        Assert.Null(database.GetSharedSetting("post", PostKey));
        Assert.Empty(database.ListSharedSettings());
        Assert.Null(database.UpdateSharedSetting("post", PostKey, Write("""{"feed":4}"""), 3));

        var (recreated, _) = database.CreateSharedSetting("post", PostKey, Write("""{"feed":5}"""));
        Assert.Equal(4, recreated.Revision);
        Assert.Equal(4L, Scalar(factory.DatabasePath, "SELECT COUNT(*) FROM shared_setting_revisions"));
        Assert.Equal("tester", Assert.Single(database.ListSharedSettings()).SavedBy);
    }

    [Fact]
    public void Database_StoresMachinesUnderTheirLowerCaseId()
    {
        using var factory = new ServerFactory();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        var machine = new MachineConfig { Name = "Synthetic laser" };

        database.CreateSharedSetting("machine", machine.Id.ToString().ToUpperInvariant(), Write(SharedMachineProvider.Serialize(machine)));

        Assert.Equal(machine.Id.ToString("D"), Assert.Single(database.ListSharedSettings()).Key);
        Assert.NotNull(database.GetSharedSetting("machine", machine.Id.ToString().ToUpperInvariant()));
        Assert.Throws<ArgumentException>(() =>
            database.CreateSharedSetting("machine", Guid.NewGuid().ToString(), Write(SharedMachineProvider.Serialize(machine))));
    }

    [Fact]
    public void V12Upgrade_StartsWithNoSharedSettings()
    {
        using var factory = new ServerFactory();
        using (new NestDatabase(factory.DatabasePath, pooling: false)) { }
        Execute(factory.DatabasePath,
            "DROP TABLE shared_setting_revisions; DROP TABLE shared_settings; PRAGMA user_version=12;");

        using var upgraded = new NestDatabase(factory.DatabasePath, pooling: false);

        Assert.Equal(13L, Scalar(factory.DatabasePath, "PRAGMA user_version"));
        Assert.Empty(upgraded.ListSharedSettings());
    }

    [Fact]
    public async Task Api_FollowsTheCreateAndIfMatchContract()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var url = $"/api/settings/post/{PostKey}";

        var created = await client.PostAsJsonAsync(url, Write("""{"feed":1}"""));
        var again = await client.PostAsJsonAsync(url, Write("""{"feed":1}"""));
        var different = await client.PostAsJsonAsync(url, Write("""{"feed":2}"""));
        var unguarded = await client.PutAsJsonAsync(url, Write("""{"feed":2}"""));
        var stale = await Put(client, url, Write("""{"feed":2}"""), revision: 5);
        var updated = await Put(client, url, Write("""{"feed":2}"""), revision: 1);
        var unknownKind = await client.GetAsync($"/api/settings/printer/{PostKey}");
        var badKey = await client.PostAsJsonAsync("/api/settings/clamps/other", Write("{}"));
        var notObject = await client.PostAsJsonAsync("/api/settings/post/Other.Post", Write("[1]"));
        var extraField = await client.PostAsync("/api/settings/post/Other.Post",
            new StringContent("""{"json":"{}","savedBy":"","revision":1}""", MediaTypeHeaderValue.Parse("application/json")));

        Assert.Equal(
            new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict, (HttpStatusCode)428,
                HttpStatusCode.Conflict, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                HttpStatusCode.BadRequest, HttpStatusCode.BadRequest },
            new[] { created.StatusCode, again.StatusCode, different.StatusCode, unguarded.StatusCode, stale.StatusCode,
                updated.StatusCode, unknownKind.StatusCode, badKey.StatusCode, notObject.StatusCode, extraField.StatusCode });
        Assert.Equal(1, (await different.Content.ReadFromJsonAsync<SharedSettingRecord>())!.Revision);
        Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<SharedSettingRecord>())!.Revision);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, url + "?savedBy=tester");
        delete.Headers.IfMatch.Add(new EntityTagHeaderValue("\"2\""));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<SharedSettingRecord>>("/api/settings"))!);
    }

    [Fact]
    public void Store_SavesThroughTheServerAndTakesTheNewerDocumentOnAConflict()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var cache = Path.Combine(factory.DirectoryPath, "cache", "settings.json");
        using var mine = Store(client, cache, "me");
        using var theirs = Store(client, Path.Combine(factory.DirectoryPath, "theirs.json"), "them");
        var machine = new MachineConfig { Name = "Synthetic laser" };

        Assert.True(mine.Refresh());
        new SharedMachineProvider(mine).SaveMachine(machine);
        mine.Save("post", PostKey, """{"feed":1}""");
        Assert.True(theirs.Refresh());
        theirs.Save("post", PostKey, """{"feed":2}""");

        var conflict = Assert.Throws<SharedSettingSaveConflictException>(() => mine.Save("post", PostKey, """{"feed":3}"""));
        Assert.Equal(("them", """{"feed":2}"""), (conflict.Current.SavedBy, mine.Get("post", PostKey)!.Json));
        Assert.Equal("""{"feed":3}""", mine.Save("post", PostKey, """{"feed":3}""").Json);

        var provider = new SharedMachineProvider(theirs);
        Assert.Equal("Synthetic laser", Assert.Single(provider.GetMachines()).Name);
        Assert.Equal(machine.Id, provider.GetMachine(machine.Id)!.Id);
        provider.DeleteMachine(machine.Id);
        Assert.True(mine.Refresh());
        Assert.Empty(mine.List("machine"));
        Assert.True(File.Exists(cache));
    }

    [Fact]
    public void Store_ServesTheLastCopyReadOnlyWhileTheServerIsUnreachable()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var cache = Path.Combine(factory.DirectoryPath, "settings.json");
        using (var online = Store(client, cache, "me"))
        {
            online.Refresh();
            online.Save("post", PostKey, """{"feed":1}""");
        }

        // Nothing listens on the discard port, so every request fails as an unreachable server does.
        using var offline = new SharedSettingsStore(
            new RemoteSharedSettings("http://127.0.0.1:9/", TimeSpan.FromSeconds(2)), cache, "me");

        Assert.False(offline.Refresh());
        Assert.True(offline.IsOffline);
        Assert.NotNull(offline.ReadAt);
        Assert.Equal("""{"feed":1}""", offline.Get("post", PostKey)!.Json);
        Assert.Same(offline.Get("post", PostKey), offline.Save("post", PostKey, """{"feed":1}"""));
        var refused = Assert.ThrowsAny<IOException>(() => offline.Save("post", PostKey, """{"feed":2}"""));
        Assert.IsNotType<SharedSettingSaveConflictException>(refused);
        Assert.Equal("""{"feed":1}""", offline.Get("post", PostKey)!.Json);
    }

    private static SharedSettingsStore Store(HttpClient client, string cache, string savedBy) =>
        new(new RemoteSharedSettings(client, client.BaseAddress!.ToString()), cache, savedBy);

    private static SharedSettingWrite Write(string json) => new() { Json = json, SavedBy = "tester" };

    private static async Task<HttpResponseMessage> Put(HttpClient client, string url, SharedSettingWrite write, int revision)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(write) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{revision}\""));
        return await client.SendAsync(request);
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
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
