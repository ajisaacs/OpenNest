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

    [Fact]
    public async Task Delete_HidesAllOrdinaryReadsAndRecoveryPreservesVersions()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var saved = await repository.UploadAsync([0, 255], new NestRecord { Name = "Fixture" });
        var deleted = await repository.DeleteAsync(saved.Id, 1);
        Assert.True(deleted.Deleted);
        Assert.Equal(2, deleted.Revision);
        foreach (var suffix in new[] { "", "/file", "/revisions", "/revisions/1/file" })
        {
            using var hidden = await client.GetAsync($"/api/nests/{saved.Id}{suffix}");
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        Assert.Empty(await repository.ListAsync());
        Assert.Empty((await repository.QueryAsync(new NestQuery())).Items);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Null(reopened.Get(saved.Id));
        // Recovery is explicit and bounded, never ordinary search.
        Assert.Equal(saved.Id, Assert.Single((await repository.QueryDeletedAsync(new NestQuery { Limit = 1 })).Items).Id);
        Assert.True((await repository.GetDeletedMetadataAsync(saved.Id))!.Deleted);
        Assert.Equal(new byte[] { 0, 255 }, await repository.GetDeletedFileAsync(saved.Id));
        var history = await repository.GetDeletedRevisionsAsync(saved.Id);
        Assert.Equal(new[] { true, false }, history.Items.Select(r => r.Deleted));
        foreach (var item in history.Items)
            Assert.Equal(new byte[] { 0, 255 }, await repository.GetDeletedRevisionFileAsync(saved.Id, item.Revision));
        var recovered = await repository.RecoverAsync(saved.Id, 2);
        Assert.False(recovered.Deleted);
        Assert.Empty((await repository.QueryDeletedAsync(new NestQuery())).Items);
        Assert.Equal(3, (await repository.GetMetadataAsync(saved.Id))!.Revision);
        Assert.Equal(new byte[] { 0, 255 }, await repository.GetFileAsync(saved.Id));
        Assert.Equal(3, (await repository.GetRevisionsAsync(saved.Id)).Total);
    }

    [Theory]
    [InlineData(false, null, 428)]
    [InlineData(false, "1", 400)]
    [InlineData(false, "\"2\"", 409)]
    [InlineData(true, null, 428)]
    [InlineData(true, "1", 400)]
    [InlineData(true, "\"1\"", 409)]
    public async Task Visibility_RejectedWriteLeavesBytesAndHistory(bool recover, string? header, int status)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var saved = await repository.UploadAsync([1], new NestRecord { Name = "Fixture" });
        if (recover) await repository.DeleteAsync(saved.Id, 1);
        using var request = new HttpRequestMessage(recover ? HttpMethod.Post : HttpMethod.Delete,
            recover ? $"/api/nests/deleted/{saved.Id}/recover" : $"/api/nests/{saved.Id}");
        if (header is not null) request.Headers.TryAddWithoutValidation("If-Match", header);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(recover ? 2 : 1, reopened.History(saved.Id, 0, 100, recover)!.Total);
        Assert.Equal(new byte[] { 1 }, reopened.GetFile(saved.Id, recover));
        Assert.Equal(recover, reopened.Get(saved.Id, recover)!.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Visibility_FailedSnapshotRollsBack(bool recover)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var saved = await repository.UploadAsync([1], new NestRecord { Name = "Fixture" });
        if (recover) await repository.DeleteAsync(saved.Id, 1);
        FailSnapshots(factory.DatabasePath);
        Assert.Contains("500", (await Assert.ThrowsAsync<IOException>(() => recover
            ? repository.RecoverAsync(saved.Id, 2) : repository.DeleteAsync(saved.Id, 1))).Message);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(recover ? 2 : 1, reopened.History(saved.Id, 0, 100, recover)!.Total);
        Assert.Equal(new byte[] { 1 }, reopened.GetFile(saved.Id, recover));
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
