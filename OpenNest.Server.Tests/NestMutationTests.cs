using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

public sealed class NestMutationTests
{
    [Fact]
    public async Task Metadata_AppendsExactArchiveAndNoOpStillChecksPrecondition()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var original = await repository.UploadAsync([0, 255, 1], new NestRecord { Name = "Fixture" });
        original.Comments = "Edited";
        var saved = await repository.UpdateMetadataAsync(original.Id, original);
        Assert.Equal(2, saved.Revision);
        var retry = await repository.UpdateMetadataAsync(saved.Id, saved);
        Assert.Equal(saved.SavedAt, retry.SavedAt);
        Assert.Equal(2, retry.Revision);
        Assert.Contains("409", (await Assert.ThrowsAsync<IOException>(() => repository.UpdateMetadataAsync(original.Id, original))).Message);
        var history = await repository.GetRevisionsAsync(saved.Id);
        Assert.Equal(new[] { "Edited", "" }, history.Items.Select(r => r.Comments));
        foreach (var revision in history.Items)
            Assert.Equal(new byte[] { 0, 255, 1 }, await repository.GetRevisionFileAsync(saved.Id, revision.Revision));
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(2, reopened.History(saved.Id, 0, 100)!.Total);
        Assert.Equal(new byte[] { 0, 255, 1 }, reopened.GetFile(saved.Id));
    }

    [Theory]
    [InlineData(null, 428)]
    [InlineData("1", 400)]
    [InlineData("\"2\"", 409)]
    public async Task Metadata_RejectedWriteLeavesHistory(string? header, int status)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var saved = await repository.UploadAsync([1], new NestRecord { Name = "Fixture" });
        if (header is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", header);
        using var response = await client.PutAsJsonAsync($"/api/nests/{saved.Id}/metadata", new NestRecord { Name = "Rejected" });
        Assert.Equal(status, (int)response.StatusCode);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal("Fixture", reopened.Get(saved.Id)!.Name);
        Assert.Equal(1, reopened.History(saved.Id, 0, 100)!.Total);
    }

    [Fact]
    public async Task Metadata_FailedSnapshotRollsBack()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var saved = await repository.UploadAsync([1], new NestRecord { Name = "Fixture" });
        FailSnapshots(factory.DatabasePath);
        saved.Comments = "Rejected";
        Assert.Contains("500", (await Assert.ThrowsAsync<IOException>(() => repository.UpdateMetadataAsync(saved.Id, saved))).Message);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal("", reopened.Get(saved.Id)!.Comments);
        Assert.Equal(1, reopened.History(saved.Id, 0, 100)!.Total);
    }

    private static void FailSnapshots(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER fail_snapshot BEFORE INSERT ON nest_revisions BEGIN SELECT RAISE(ABORT, 'fixture failure'); END";
        command.ExecuteNonQuery();
    }
}
