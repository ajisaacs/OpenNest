using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OpenNest.Data;
using static OpenNest.Server.Tests.DrawingLibraryFixtures;

namespace OpenNest.Server.Tests;

public sealed class DrawingLibraryApiTests
{
    [Fact]
    public async Task Upload_StoresRevisionOneWithServerComputedFields()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        record.Thickness = 0.123456;
        record.Revision = 9;
        record.PackageSha256 = "client value";
        record.PackageSize = 1;
        var package = Package();

        var saved = await Upload(client, record, package);

        Assert.Equal(record.Id, saved.Id);
        Assert.Equal(1, saved.Revision);
        Assert.Equal(0.1235, saved.Thickness);
        Assert.Equal(Sha256(package), saved.PackageSha256);
        Assert.Equal(package.LongLength, saved.PackageSize);
        Assert.NotEqual(default, saved.SavedAt);
        Assert.False(saved.Archived);
        Assert.Equal(CutType.Laser, saved.CutType);
        Assert.Equal("Synthetic operator", saved.SavedBy);
        Assert.Equal(package, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/file"));
        Assert.Equal(package, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/revisions/1/file"));
        var fetched = await client.GetFromJsonAsync<DrawingLibraryRecord>($"/api/drawings/{record.Id}", Options);
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(fetched));
    }

    [Fact]
    public async Task Upload_SerializesEnumsAsCamelCaseNames()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        record.CutType = CutType.Waterjet;
        await Upload(client, record, Package());

        var json = await client.GetStringAsync($"/api/drawings/{record.Id}");

        var node = JsonNode.Parse(json)!;
        Assert.Equal("waterjet", node["cutType"]!.GetValue<string>());
        Assert.Equal("inches", node["units"]!.GetValue<string>());
    }

    [Fact]
    public async Task Upload_ExistingId_ConflictsAndChangesNothing()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var package = Package("first");
        await Upload(client, record, package);

        record.Name = "Second writer";
        using var response = await client.PostAsync("/api/drawings", Body(record, Package("second")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var current = (await response.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!;
        Assert.Equal("Synthetic bracket", current.Name);
        Assert.Equal(1, current.Revision);
        Assert.Equal(package, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/file"));
    }

    public static TheoryData<string, string, int> RejectedUploads()
    {
        var valid = JsonSerializer.Serialize(Record(), Options);
        string With(string property, JsonNode? value)
        {
            var node = JsonNode.Parse(valid)!.AsObject();
            node[property] = value;
            return node.ToJsonString();
        }

        return new TheoryData<string, string, int>
        {
            { "empty id", With("id", Guid.Empty.ToString()), 400 },
            { "unknown cut type", With("cutType", "Oxyfuel"), 400 },
            { "integer cut type", With("cutType", 1), 400 },
            { "combined cut type", With("cutType", "Laser, Plasma"), 400 },
            { "null cut type", With("cutType", null), 400 },
            { "unknown units", With("units", "Feet"), 400 },
            { "integer units", With("units", 0), 400 },
            { "negative thickness", With("thickness", -0.25), 400 },
            { "blank name", With("name", "  "), 400 },
            { "null name", With("name", null), 400 },
            { "long name", With("name", new string('n', 201)), 400 },
            { "NUL name", With("name", "a\0b"), 400 },
            { "null customer", With("customer", null), 400 },
            { "long customer", With("customer", new string('c', 201)), 400 },
            { "long material", With("materialName", new string('m', 201)), 400 },
            { "long grade", With("materialGrade", new string('g', 201)), 400 },
            { "long notes", With("notes", new string('x', 4001)), 400 },
            { "not an object", "[1]", 400 },
            { "malformed", "{", 400 },
        };
    }

    [Theory]
    [MemberData(nameof(RejectedUploads))]
    public async Task Upload_InvalidMetadata_IsRejectedWithNoState(string row, string metadata, int status)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/drawings", Body(metadata, Package()));

        Assert.True(status == (int)response.StatusCode, row);
        await AssertEmptyLibrary(client);
    }

    [Fact]
    public async Task Upload_BoundaryLengths_AreAccepted()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record(new string('n', 200));
        record.Notes = new string('x', 4000);
        record.Customer = new string('c', 200);

        var saved = await Upload(client, record, Package());

        Assert.Equal(200, saved.Name.Length);
        Assert.Equal(4000, saved.Notes.Length);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("not a zip")]
    [InlineData("too large")]
    public async Task Upload_InvalidPackage_IsRejectedWithNoState(string row)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var package = row switch
        {
            "missing" => null,
            "empty" => Array.Empty<byte>(),
            "not a zip" => Encoding.UTF8.GetBytes("nest.json"),
            _ => Enumerable.Repeat((byte)'P', DrawingLibraryRules.MaxPackageBytes + 1).ToArray(),
        };

        using var response = await client.PostAsync("/api/drawings", Body(Record(), package));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEmptyLibrary(client);
    }

    [Fact]
    public async Task Upload_PackageAtLimit_IsAccepted()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var package = new byte[DrawingLibraryRules.MaxPackageBytes];
        package[0] = (byte)'P';
        package[1] = (byte)'K';

        var saved = await Upload(client, Record(), package);

        Assert.Equal(DrawingLibraryRules.MaxPackageBytes, saved.PackageSize);
    }

    [Fact]
    public async Task Upload_NotMultipartOrMissingMetadata_IsRejected()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var json = await client.PostAsJsonAsync("/api/drawings", Record(), Options);
        var withoutMetadata = new MultipartFormDataContent { { new ByteArrayContent(Package()), "package", "d.nest" } };
        using var missing = await client.PostAsync("/api/drawings", withoutMetadata);

        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        await AssertEmptyLibrary(client);
    }

    [Fact]
    public async Task Reads_UnknownDrawingOrRevision_Are404()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var saved = await Upload(client, Record(), Package());
        var unknown = Guid.NewGuid();

        foreach (var url in new[]
        {
            $"/api/drawings/{unknown}", $"/api/drawings/{unknown}/file", $"/api/drawings/{unknown}/revisions",
            $"/api/drawings/{unknown}/revisions/1/file", $"/api/drawings/{saved.Id}/revisions/2/file",
        })
        {
            using var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, url);
        }

        using var zero = await client.GetAsync($"/api/drawings/{saved.Id}/revisions/0/file");
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
    }

    [Fact]
    public async Task UpdateFile_AppendsRevisionAndKeepsHistoryExact()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var first = Package("first");
        await Upload(client, record, first);
        var second = Package("second");
        record.Notes = "Changed geometry";

        using var response = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/file", "\"1\"", Body(record, second)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!;
        Assert.Equal(2, saved.Revision);
        Assert.Equal(Sha256(second), saved.PackageSha256);
        Assert.Equal(second, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/file"));
        Assert.Equal(first, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/revisions/1/file"));
        Assert.Equal(second, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/revisions/2/file"));
        var history = await client.GetFromJsonAsync<DrawingLibraryPage>($"/api/drawings/{record.Id}/revisions", Options);
        Assert.Equal(new[] { 2, 1 }, history!.Items.Select(r => r.Revision));
        Assert.Equal(new[] { Sha256(second), Sha256(first) }, history.Items.Select(r => r.PackageSha256));
        Assert.Equal(new[] { "Changed geometry", "Synthetic notes" }, history.Items.Select(r => r.Notes));
    }

    [Fact]
    public async Task UpdateFile_IdenticalSave_IsNoOp()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var package = Package();
        var saved = await Upload(client, record, package);

        using var response = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/file", "\"1\"", Body(record, package)));

        var current = (await response.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!;
        Assert.Equal(1, current.Revision);
        Assert.Equal(saved.SavedAt, current.SavedAt);
        var history = await client.GetFromJsonAsync<DrawingLibraryPage>($"/api/drawings/{record.Id}/revisions", Options);
        Assert.Equal(1, history!.Total);
    }

    [Fact]
    public async Task UpdateMetadata_AppendsRevisionAndKeepsPackage()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var package = Package();
        await Upload(client, record, package);
        record.CutType = CutType.Plasma;
        record.MaterialGrade = "Grade B";

        using var response = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/metadata", "\"1\"",
            JsonContent.Create(record, options: Options)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!;
        Assert.Equal(2, saved.Revision);
        Assert.Equal(CutType.Plasma, saved.CutType);
        Assert.Equal("Grade B", saved.MaterialGrade);
        Assert.Equal(Sha256(package), saved.PackageSha256);
        Assert.Equal(package, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/revisions/2/file"));
    }

    [Theory]
    [InlineData("file", null, 428)]
    [InlineData("file", "1", 400)]
    [InlineData("file", "\"0\"", 400)]
    [InlineData("file", "\"1\", \"2\"", 400)]
    [InlineData("file", "\"2\"", 409)]
    [InlineData("metadata", null, 428)]
    [InlineData("metadata", "\"x\"", 400)]
    [InlineData("metadata", "\"2\"", 409)]
    [InlineData("archive", null, 428)]
    [InlineData("archive", "\"2\"", 409)]
    [InlineData("restore", "\"1\"", 409)]
    public async Task GuardedWrite_Refusals_LeaveCurrentAndHistoryUnchanged(string route, string? ifMatch, int status)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var package = Package();
        var saved = await Upload(client, record, package);
        record.Name = "Rejected";
        HttpContent? content = route switch
        {
            "file" => Body(record, Package("rejected")),
            "metadata" => JsonContent.Create(record, options: Options),
            _ => null,
        };
        var method = route is "archive" or "restore" ? HttpMethod.Post : HttpMethod.Put;

        using var response = await client.SendAsync(Guarded(method, $"/api/drawings/{record.Id}/{route}", ifMatch, content));

        Assert.Equal(status, (int)response.StatusCode);
        if (status == 409)
            Assert.Equal(1, (await response.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!.Revision);
        await AssertUnchanged(factory, client, saved, package);
    }

    [Fact]
    public async Task GuardedWrite_UnknownDrawing_Is404()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();

        using var file = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/file", "\"1\"", Body(record, Package())));
        using var metadata = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/metadata", "\"1\"",
            JsonContent.Create(record, options: Options)));
        using var archive = await client.SendAsync(Guarded(HttpMethod.Post, $"/api/drawings/{record.Id}/archive", "\"1\""));

        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, archive.StatusCode);
        await AssertEmptyLibrary(client);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("metadata")]
    public async Task GuardedWrite_InvalidBodyOrMismatchedId_IsRejected(string route)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var package = Package();
        var saved = await Upload(client, record, package);
        var other = Record("Other id");
        HttpContent invalid = route == "file"
            ? Body("{\"name\":\"\"}", Package())
            : new StringContent("{\"name\":\"x\",\"cutType\":3}", Encoding.UTF8, "application/json");
        HttpContent mismatched = route == "file" ? Body(other, Package()) : JsonContent.Create(other, options: Options);

        using var invalidResponse = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/{route}", "\"1\"", invalid));
        using var mismatchedResponse = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/{route}", "\"1\"", mismatched));

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, mismatchedResponse.StatusCode);
        await AssertUnchanged(factory, client, saved, package);
    }

    [Fact]
    public async Task UpdateMetadata_NonJsonBody_IsRejected()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var package = Package();
        var saved = await Upload(client, record, package);

        using var response = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/metadata", "\"1\"",
            new StringContent(JsonSerializer.Serialize(record, Options), Encoding.UTF8, "text/plain")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchanged(factory, client, saved, package);
    }

    [Fact]
    public async Task ArchiveAndRestore_HideFromBrowsingAndBlockEdits()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var package = Package();
        await Upload(client, record, package);

        using var archived = await client.SendAsync(Guarded(HttpMethod.Post, $"/api/drawings/{record.Id}/archive", "\"1\""));
        var archivedRecord = (await archived.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!;
        Assert.True(archivedRecord.Archived);
        Assert.Equal(2, archivedRecord.Revision);
        Assert.Equal(Sha256(package), archivedRecord.PackageSha256);
        Assert.Equal(0, (await Query(client, "")).Total);
        Assert.Equal(1, (await Query(client, "includeArchived=true")).Total);
        Assert.True((await client.GetFromJsonAsync<DrawingLibraryRecord>($"/api/drawings/{record.Id}", Options))!.Archived);

        using var again = await client.SendAsync(Guarded(HttpMethod.Post, $"/api/drawings/{record.Id}/archive", "\"2\""));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        record.Name = "Edited while archived";
        using var edit = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/file", "\"2\"", Body(record, Package("x"))));
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);

        using var restored = await client.SendAsync(Guarded(HttpMethod.Post, $"/api/drawings/{record.Id}/restore", "\"2\""));
        var restoredRecord = (await restored.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!;
        Assert.False(restoredRecord.Archived);
        Assert.Equal(3, restoredRecord.Revision);
        Assert.Equal("Synthetic bracket", restoredRecord.Name);
        Assert.Equal(1, (await Query(client, "")).Total);
        var history = await client.GetFromJsonAsync<DrawingLibraryPage>($"/api/drawings/{record.Id}/revisions", Options);
        Assert.Equal(new[] { false, true, false }, history!.Items.Select(r => r.Archived));
        Assert.Equal(package, await client.GetByteArrayAsync($"/api/drawings/{record.Id}/revisions/2/file"));
    }

    [Fact]
    public async Task FailedRevisionInsert_RollsBackCurrentRow()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        var package = Package();
        var saved = await Upload(client, record, package);
        using (var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_revision BEFORE INSERT ON drawing_revisions BEGIN SELECT RAISE(ABORT, 'fixture failure'); END";
            command.ExecuteNonQuery();
        }
        record.Name = "Rejected";

        using var failed = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/file", "\"1\"", Body(record, Package("x"))));
        using var insert = await client.PostAsync("/api/drawings", Body(Record("New"), Package()));

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, insert.StatusCode);
        await AssertUnchanged(factory, client, saved, package);
        Assert.Equal(1, (await Query(client, "")).Total);
    }

    [Fact]
    public async Task ConcurrentGuardedSaves_OneWinsAndOneConflicts()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        await Upload(client, record, Package());

        var responses = await Task.WhenAll(Enumerable.Range(2, 2).Select(i =>
        {
            var writer = Record($"Writer {i}", record.Id);
            return client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/file", "\"1\"", Body(writer, Package($"w{i}"))));
        }));
        try
        {
            Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.OK));
            var conflict = Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(2, (await conflict.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!.Revision);
            var history = await client.GetFromJsonAsync<DrawingLibraryPage>($"/api/drawings/{record.Id}/revisions", Options);
            Assert.Equal(new[] { 2, 1 }, history!.Items.Select(r => r.Revision));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=201")]
    [InlineData("offset=-1")]
    [InlineData("limit=1&limit=2")]
    [InlineData("limit=no")]
    [InlineData("unknown=1")]
    public async Task History_InvalidPagination_IsRejected(string query)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var saved = await Upload(client, Record(), Package());

        using var response = await client.GetAsync($"/api/drawings/{saved.Id}/revisions?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_PagesNewestFirst()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = Record();
        await Upload(client, record, Package());
        for (var revision = 1; revision <= 3; revision++)
        {
            record.Notes = $"Revision {revision + 1}";
            using var response = await client.SendAsync(Guarded(HttpMethod.Put, $"/api/drawings/{record.Id}/metadata",
                $"\"{revision}\"", JsonContent.Create(record, options: Options)));
            response.EnsureSuccessStatusCode();
        }

        var page = await client.GetFromJsonAsync<DrawingLibraryPage>($"/api/drawings/{record.Id}/revisions?offset=1&limit=2", Options);

        Assert.Equal(4, page!.Total);
        Assert.Equal(new[] { 3, 2 }, page.Items.Select(r => r.Revision));
    }

    internal static async Task<DrawingLibraryPage> Query(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<DrawingLibraryPage>("/api/drawings/query?" + query, Options))!;

    private static async Task AssertEmptyLibrary(HttpClient client) =>
        Assert.Equal(0, (await Query(client, "includeArchived=true")).Total);

    private static async Task AssertUnchanged(ServerFactory factory, HttpClient client, DrawingLibraryRecord saved, byte[] package)
    {
        var current = await client.GetFromJsonAsync<DrawingLibraryRecord>($"/api/drawings/{saved.Id}", Options);
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(current));
        Assert.Equal(package, await client.GetByteArrayAsync($"/api/drawings/{saved.Id}/file"));
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(1, reopened.DrawingHistory(saved.Id, 0, 100)!.Total);
        Assert.Equal(package, reopened.GetDrawingRevisionPackage(saved.Id, 1));
    }
}
