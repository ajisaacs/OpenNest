using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;

namespace OpenNest.Server.Tests;

/// <summary>Content-addressed packages and structured nesting settings (schema v5).</summary>
public sealed class DrawingLibraryStorageTests
{
    private static readonly DrawingNestingSettings TestSquare = new()
    {
        CutFirst = true,
        Priority = 4,
        StepAngle = System.Math.PI / 2,
        StartAngle = 0,
        EndAngle = System.Math.PI,
        Allow180Equivalent = true,
        ColorArgb = unchecked((int)0xFF336699),
    };

    [Fact]
    public async Task IdenticalPackages_AreStoredOnce()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var package = Package("shared");

        var first = await Upload(client, Record("First"), package);
        var second = await Upload(client, Record("Second"), package);

        Assert.Equal(first.PackageSha256, second.PackageSha256);
        Assert.Equal(1L, Count(factory, "drawing_packages"));
        Assert.Equal(package, await client.GetByteArrayAsync($"/api/drawings/{second.Id}/file"));
    }

    [Fact]
    public async Task MetadataSettingsAndArchiveRevisions_AddNoPackage()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var package = Package();
        await Upload(client, record, package);

        record.Notes = "Edited notes";
        var notes = await PutMetadata(client, record, 1);
        record.Settings = TestSquare;
        var settings = await PutMetadata(client, record, 2);
        using (var archive = await client.SendAsync(Guarded(HttpMethod.Post, $"/api/drawings/{record.Id}/archive", "\"3\"")))
            Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        Assert.Equal((2, 3), (notes.Revision, settings.Revision));
        Assert.Equal(1L, Count(factory, "drawing_packages"));
        Assert.Equal(4L, Count(factory, "drawing_revisions"));
        for (var revision = 1; revision <= 4; revision++)
            Assert.Equal(package, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/revisions/{revision}/file"));
    }

    [Fact]
    public async Task NewGeometry_AddsAPackage_AndEarlierRevisionsKeepTheirs()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var original = Package("original");
        var changed = Package("changed");
        await Upload(client, record, original);

        using var response = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/file", "\"1\"", Body(record, changed)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2L, Count(factory, "drawing_packages"));
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/revisions/1/file"));
        Assert.Equal(changed, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/revisions/2/file"));
        Assert.Equal(changed, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/file"));
    }

    [Fact]
    public async Task Settings_RoundTripThroughEveryRead()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        record.Settings = TestSquare;

        var saved = await Upload(client, record, Package());
        var current = await client.GetFromJsonAsync<DrawingLibraryRecord>($"/api/drawings/{record.Id}", Options);
        var listed = Assert.Single((await client.GetFromJsonAsync<DrawingLibraryPage>("/api/drawings/query", Options))!.Items);
        var history = Assert.Single((await client.GetFromJsonAsync<DrawingLibraryPage>($"/api/drawings/{record.Id}/revisions", Options))!.Items);

        Assert.Equal(TestSquare, saved.Settings);
        Assert.Equal(TestSquare, current!.Settings);
        Assert.Equal(TestSquare, listed.Settings);
        Assert.Equal(TestSquare, history.Settings);
    }

    [Fact]
    public async Task SettingsOnlyEdit_IsARevision_AndRepeatingItIsANoOp()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var saved = await Upload(client, record, Package());

        record.Settings = TestSquare;
        var edited = await PutMetadata(client, record, 1);
        var repeated = await PutMetadata(client, record, 2);

        Assert.Equal(2, edited.Revision);
        Assert.Equal(TestSquare, edited.Settings);
        Assert.Equal(saved.PackageSha256, edited.PackageSha256);
        Assert.Equal(2, repeated.Revision);
        var history = (await client.GetFromJsonAsync<DrawingLibraryPage>($"/api/drawings/{record.Id}/revisions", Options))!;
        Assert.Equal(new[] { true, false }, history.Items.Select(r => r.Settings.CutFirst));
    }

    [Fact]
    public async Task Query_FiltersByCutFirst()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var square = Record("Test square");
        square.Settings = new DrawingNestingSettings { CutFirst = true };
        await Upload(client, square, Package("square"));
        await Upload(client, Record("Bracket"), Package("bracket"));

        var cutFirst = (await client.GetFromJsonAsync<DrawingLibraryPage>("/api/drawings/query?cutFirst=true", Options))!;
        var others = (await client.GetFromJsonAsync<DrawingLibraryPage>("/api/drawings/query?cutFirst=false", Options))!;
        using var invalid = await client.GetAsync("/api/drawings/query?cutFirst=yes");

        Assert.Equal("Test square", Assert.Single(cutFirst.Items).Name);
        Assert.Equal("Bracket", Assert.Single(others.Items).Name);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Theory]
    [InlineData("""{"name":"x","thickness":0,"units":"Inches","cutType":"Laser","settings":{"stepAngle":-1}}""")]
    [InlineData("""{"name":"x","thickness":0,"units":"Inches","cutType":"Laser","settings":null}""")]
    [InlineData("""{"name":"x","thickness":0,"units":"Inches","cutType":"Laser","settings":{"priority":"high"}}""")]
    public async Task InvalidSettings_AreRejectedWithNoState(string metadata)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        using var response = await client.PostAsync("/api/drawings", Body(metadata.Replace("{\"name\"", $"{{\"id\":\"{id}\",\"name\""), Package()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0L, Count(factory, "drawing_packages"));
        Assert.Equal(0L, Count(factory, "drawings"));
    }

    private static async Task<DrawingLibraryRecord> PutMetadata(HttpClient client, DrawingLibraryRecord record, int expected)
    {
        using var response = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/metadata", $"\"{expected}\"",
            JsonContent.Create(record, options: Options)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!;
    }

    private static long Count(ServerFactory factory, string table)
    {
        using var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }
}
