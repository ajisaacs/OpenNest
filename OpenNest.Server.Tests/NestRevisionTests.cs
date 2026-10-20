using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

public sealed class NestRevisionTests
{
    private const string LegacySchema = """
                CREATE TABLE nests (
                    id TEXT PRIMARY KEY, name TEXT NOT NULL, customer TEXT NOT NULL DEFAULT '',
                    dateCreated TEXT NOT NULL, dateModified TEXT NOT NULL, material TEXT NOT NULL DEFAULT '',
                    thickness REAL NOT NULL DEFAULT 0, status TEXT NOT NULL DEFAULT 'Quote',
                    plateCount INTEGER NOT NULL DEFAULT 0, partCount INTEGER NOT NULL DEFAULT 0,
                    comments TEXT NOT NULL DEFAULT '', madeBy TEXT NOT NULL DEFAULT '',
                    fileSize INTEGER NOT NULL DEFAULT 0, savedAt TEXT NOT NULL, file BLOB NOT NULL);
                """;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    internal static MultipartFormDataContent Body(NestRecord record, byte[] bytes)
    {
        var body = new MultipartFormDataContent();
        body.Add(new StringContent(JsonSerializer.Serialize(record)), "metadata");
        body.Add(new ByteArrayContent(bytes), "file", "fixture.nest");
        return body;
    }

    [Fact]
    public async Task ChangedSave_PreservesExactHistoryAndNoOp()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var record = new NestRecord { Name = "Initial" };
        using var created = await client.PostAsync("/api/nests", Body(record, TestNests.Archive(1, 2)));
        var current = (await created.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        Assert.Equal(1, current.Revision);
        record.Name = "Changed";
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        using var updated = await client.PutAsync($"/api/nests/{current.Id}/file", Body(record, TestNests.Archive(3, 4)));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var latest = (await updated.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        Assert.Equal(2, latest.Revision);
        Assert.Equal(TestNests.Archive(1, 2), await client.GetByteArrayAsync($"/api/nests/{current.Id}/revisions/1/file"));
        Assert.Equal(TestNests.Archive(3, 4), await client.GetByteArrayAsync($"/api/nests/{current.Id}/file"));
        client.DefaultRequestHeaders.Remove("If-Match");
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"2\"");
        using var retry = await client.PutAsync($"/api/nests/{current.Id}/file", Body(record, TestNests.Archive(3, 4)));
        Assert.Equal(latest.SavedAt, (await retry.Content.ReadFromJsonAsync<NestRecord>(Options))!.SavedAt);
        var page = await client.GetFromJsonAsync<NestPage>($"/api/nests/{current.Id}/revisions?limit=1", Options);
        Assert.Equal(2, page!.Total);
        Assert.Equal(2, Assert.Single(page.Items).Revision);
    }

    [Theory]
    [InlineData(null, 428)]
    [InlineData("1", 400)]
    [InlineData("\"0\"", 400)]
    [InlineData("\"2\"", 409)]
    public async Task RejectedWrite_LeavesCurrentAndHistoryUnchanged(string? header, int status)
    {
        using var factory = new ServerFactory();
        Guid id;
        using (var client = factory.CreateClient())
        {
            using var created = await client.PostAsync("/api/nests", Body(new NestRecord { Name = "Initial" }, TestNests.Archive(1)));
            id = (await created.Content.ReadFromJsonAsync<NestRecord>(Options))!.Id;
            if (header is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", header);
            using var response = await client.PutAsync($"/api/nests/{id}/file", Body(new NestRecord { Name = "Rejected" }, TestNests.Archive(2)));
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal(TestNests.Archive(1), await client.GetByteArrayAsync($"/api/nests/{id}/file"));
        }
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal("Initial", reopened.Get(id)!.Name);
        Assert.Equal(1, reopened.Get(id)!.Revision);
    }

    [Fact]
    public async Task FailedRevisionInsert_RollsBackCurrentProjection()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsync("/api/nests", Body(new NestRecord { Name = "Initial" }, TestNests.Archive(1)));
        var current = (await created.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        using (var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_revision BEFORE INSERT ON nest_revisions BEGIN SELECT RAISE(ABORT, 'fixture failure'); END";
            command.ExecuteNonQuery();
        }
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        using var failed = await client.PutAsync($"/api/nests/{current.Id}/file", Body(new NestRecord { Name = "Rejected" }, TestNests.Archive(2)));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(TestNests.Archive(1), await client.GetByteArrayAsync($"/api/nests/{current.Id}/file"));
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal("Initial", reopened.Get(current.Id)!.Name);
        Assert.Equal(1, reopened.History(current.Id, 0, 100)!.Total);
    }
    [Fact]
    public void OldSchema_BackfillsExactRowsAndIsIdempotent()
    {
        using var factory = new ServerFactory();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var bytes = new[] { TestNests.Archive(0, 1, 255), TestNests.Archive(7, 8, 9, 0) };
        var timestamp = new DateTime(2026, 1, 2, 3, 4, 5);
        using (var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = LegacySchema;
            command.ExecuteNonQuery();
            for (var i = 0; i < ids.Length; i++)
            {
                command.Parameters.Clear();
                command.CommandText = "INSERT INTO nests VALUES ($id, $name, 'Fixture', $date, $date, 'Alloy', 0.25, 'ToBeCut', 2, 3, 'Notes', 'Author', $size, $date, $file)";
                command.Parameters.AddWithValue("$id", ids[i].ToString());
                command.Parameters.AddWithValue("$name", $"Legacy {i}");
                command.Parameters.AddWithValue("$date", timestamp.ToString("o"));
                command.Parameters.AddWithValue("$size", bytes[i].Length);
                command.Parameters.AddWithValue("$file", bytes[i]);
                command.ExecuteNonQuery();
            }
        }
        for (var open = 0; open < 2; open++)
        {
            using var database = new NestDatabase(factory.DatabasePath, pooling: false);
            Assert.Equal(2, database.List().Count);
            for (var i = 0; i < ids.Length; i++)
            {
                var current = database.Get(ids[i])!;
                Assert.Equal(1, current.Revision);
                Assert.Equal(timestamp, current.SavedAt);
                Assert.Equal($"Legacy {i}", current.Name);
                Assert.Equal("Fixture", current.Customer);
                Assert.Equal("Notes", current.Comments);
                Assert.Equal(bytes[i], database.GetFile(ids[i]));
                Assert.Equal(bytes[i], database.GetRevisionFile(ids[i], 1));
                var historical = Assert.Single(database.History(ids[i], 0, 100)!.Items);
                Assert.Equal(JsonSerializer.Serialize(current), JsonSerializer.Serialize(historical));
            }
        }
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
        using var response = await client.GetAsync($"/api/nests/{Guid.NewGuid()}/revisions?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentGuardedSaves_OneWinsAndOneConflicts()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsync("/api/nests", Body(new NestRecord { Name = "Initial" }, TestNests.Archive(1)));
        var current = (await created.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        var responses = await Task.WhenAll(Enumerable.Range(2, 2).Select(i =>
            client.PutAsync($"/api/nests/{current.Id}/file", Body(new NestRecord { Name = $"Writer {i}" }, TestNests.Archive((byte)i)))));
        try
        {
            Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.OK));
            var conflict = Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(2, (await conflict.Content.ReadFromJsonAsync<NestRecord>(Options))!.Revision);
            var history = await client.GetFromJsonAsync<NestPage>($"/api/nests/{current.Id}/revisions", Options);
            Assert.Equal(new[] { 2, 1 }, history!.Items.Select(r => r.Revision));
            Assert.Equal(TestNests.Archive(1), await client.GetByteArrayAsync($"/api/nests/{current.Id}/revisions/1/file"));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("metadata", null, 428)]
    [InlineData("delete", null, 428)]
    [InlineData("metadata", "\"1\"", 400)]
    [InlineData("file", "\"1\"", 400)]
    public async Task BlockedOrMalformedWrite_PreservesAllSnapshots(string route, string? header, int status)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsync("/api/nests", Body(new NestRecord { Name = "Initial" }, TestNests.Archive(1)));
        var current = (await created.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        if (header is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", header);
        using var response = route == "delete"
            ? await client.DeleteAsync($"/api/nests/{current.Id}")
            : await client.PutAsync($"/api/nests/{current.Id}/{route}", new StringContent("Malformed"));
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(TestNests.Archive(1), await client.GetByteArrayAsync($"/api/nests/{current.Id}/file"));
        var history = await client.GetFromJsonAsync<NestPage>($"/api/nests/{current.Id}/revisions", Options);
        Assert.Equal(1, history!.Total);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal("Initial", reopened.Get(current.Id)!.Name);
        Assert.Equal(TestNests.Archive(1), reopened.GetRevisionFile(current.Id, 1));
    }

    [Fact]
    public async Task UnknownHistoryOrVersion_Is404_AndOffsetIsBounded()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var unknown = await client.GetAsync($"/api/nests/{Guid.NewGuid()}/revisions");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var created = await client.PostAsync("/api/nests", Body(new NestRecord { Name = "Initial" }, TestNests.Archive(1)));
        var current = (await created.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        using var missing = await client.GetAsync($"/api/nests/{current.Id}/revisions/2/file");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var page = await client.GetFromJsonAsync<NestPage>($"/api/nests/{current.Id}/revisions?offset=2147483647", Options);
        Assert.Empty(page!.Items);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public void FailedMigration_RollsBackSchemaAndCanRestart()
    {
        using var factory = new ServerFactory();
        using var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = LegacySchema + "CREATE TABLE nest_revisions (fixture TEXT);";
        command.ExecuteNonQuery();
        Assert.Throws<SqliteException>(() => new NestDatabase(factory.DatabasePath, pooling: false));
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('nests') WHERE name='revision'";
        Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "DROP TABLE nest_revisions";
        command.ExecuteNonQuery();
        using var database = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Empty(database.List());
    }

}
