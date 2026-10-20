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
        var original = await repository.UploadAsync(TestNests.Archive(0, 255, 1), new NestRecord { Name = "Fixture" });
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
            Assert.Equal(TestNests.Archive(0, 255, 1), await repository.GetRevisionFileAsync(saved.Id, revision.Revision));
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(2, reopened.History(saved.Id, 0, 100)!.Total);
        Assert.Equal(TestNests.Archive(0, 255, 1), reopened.GetFile(saved.Id));
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
        var saved = await repository.UploadAsync(TestNests.Archive(1), new NestRecord { Name = "Fixture" });
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
        var saved = await repository.UploadAsync(TestNests.Archive(1), new NestRecord { Name = "Fixture" });
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
        var saved = await repository.UploadAsync(TestNests.Archive(0, 255), new NestRecord { Name = "Fixture" });
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
        Assert.Equal(TestNests.Archive(0, 255), await repository.GetDeletedFileAsync(saved.Id));
        var history = await repository.GetDeletedRevisionsAsync(saved.Id);
        Assert.Equal(new[] { true, false }, history.Items.Select(r => r.Deleted));
        foreach (var item in history.Items)
            Assert.Equal(TestNests.Archive(0, 255), await repository.GetDeletedRevisionFileAsync(saved.Id, item.Revision));
        var recovered = await repository.RecoverAsync(saved.Id, 2);
        Assert.False(recovered.Deleted);
        Assert.Empty((await repository.QueryDeletedAsync(new NestQuery())).Items);
        Assert.Equal(3, (await repository.GetMetadataAsync(saved.Id))!.Revision);
        Assert.Equal(TestNests.Archive(0, 255), await repository.GetFileAsync(saved.Id));
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
        var saved = await repository.UploadAsync(TestNests.Archive(1), new NestRecord { Name = "Fixture" });
        if (recover) await repository.DeleteAsync(saved.Id, 1);
        using var request = new HttpRequestMessage(recover ? HttpMethod.Post : HttpMethod.Delete,
            recover ? $"/api/nests/deleted/{saved.Id}/recover" : $"/api/nests/{saved.Id}");
        if (header is not null) request.Headers.TryAddWithoutValidation("If-Match", header);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(recover ? 2 : 1, reopened.History(saved.Id, 0, 100, recover)!.Total);
        Assert.Equal(TestNests.Archive(1), reopened.GetFile(saved.Id, recover));
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
        var saved = await repository.UploadAsync(TestNests.Archive(1), new NestRecord { Name = "Fixture" });
        if (recover) await repository.DeleteAsync(saved.Id, 1);
        FailSnapshots(factory.DatabasePath);
        Assert.Contains("500", (await Assert.ThrowsAsync<IOException>(() => recover
            ? repository.RecoverAsync(saved.Id, 2) : repository.DeleteAsync(saved.Id, 1))).Message);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(recover ? 2 : 1, reopened.History(saved.Id, 0, 100, recover)!.Total);
        Assert.Equal(TestNests.Archive(1), reopened.GetFile(saved.Id, recover));
    }

    [Fact]
    public async Task Restore_AppendsSelectedSnapshotWithoutChangingInterveningHistory()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var first = await repository.UploadAsync(TestNests.Archive(0, 255), new NestRecord { Name = "Original", Comments = "Before" });
        var second = await repository.UpdateFileAsync(first.Id, TestNests.Archive(3, 4), new NestRecord { Name = "Changed" }, 1);
        var before = await repository.GetRevisionsAsync(first.Id);
        var restored = await repository.RestoreAsync(first.Id, 1, 2);
        Assert.Equal(1, restored.RestoredFromRevision);
        var current = (await repository.GetMetadataAsync(first.Id))!;
        Assert.Equal(3, current.Revision);
        Assert.Equal(first.Id, current.Id);
        Assert.Equal("Original", current.Name);
        Assert.Equal("Before", current.Comments);
        Assert.Equal(TestNests.Archive(0, 255), await repository.GetFileAsync(first.Id));
        Assert.Equal(TestNests.Archive(3, 4), await repository.GetRevisionFileAsync(first.Id, 2));
        var after = await repository.GetRevisionsAsync(first.Id);
        Assert.Equal(3, after.Total);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before.Items), System.Text.Json.JsonSerializer.Serialize(after.Items.Skip(1)));
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(3, reopened.History(first.Id, 0, 100)!.Total);
        Assert.Equal(TestNests.Archive(0, 255), reopened.GetFile(first.Id));
    }

    [Theory]
    [InlineData(null, 1, 428)]
    [InlineData("1", 1, 400)]
    [InlineData("\"2\"", 1, 409)]
    [InlineData("\"1\"", 0, 400)]
    [InlineData("\"1\"", 99, 404)]
    public async Task Restore_RejectedWriteLeavesHistory(string? header, int source, int status)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var saved = await repository.UploadAsync(TestNests.Archive(1), new NestRecord { Name = "Fixture" });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/nests/{saved.Id}/revisions/{source}/restore");
        if (header is not null) request.Headers.TryAddWithoutValidation("If-Match", header);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(1, reopened.History(saved.Id, 0, 100)!.Total);
        Assert.Equal(TestNests.Archive(1), reopened.GetFile(saved.Id));
        Assert.Null(reopened.Get(saved.Id)!.RestoredFromRevision);
    }

    [Fact]
    public async Task Restore_FailedSnapshotRollsBackAndSameContentStillAppends()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var saved = await repository.UploadAsync(TestNests.Archive(1), new NestRecord { Name = "Fixture" });
        var restored = await repository.RestoreAsync(saved.Id, 1, 1);
        Assert.Equal(2, restored.Revision);
        Assert.Equal(1, restored.RestoredFromRevision);
        FailSnapshots(factory.DatabasePath);
        Assert.Contains("500", (await Assert.ThrowsAsync<IOException>(() => repository.RestoreAsync(saved.Id, 1, 2))).Message);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(2, reopened.History(saved.Id, 0, 100)!.Total);
        Assert.Equal(TestNests.Archive(1), reopened.GetFile(saved.Id));
        Assert.Equal(1, reopened.Get(saved.Id)!.RestoredFromRevision);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("delete")]
    [InlineData("restore")]
    [InlineData("recover")]
    public async Task Mutations_UnknownOrWrongVisibilityIs404(string operation)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var saved = await repository.UploadAsync(TestNests.Archive(1), new NestRecord { Name = "Fixture" });
        if (operation != "recover") await repository.DeleteAsync(saved.Id, 1);
        foreach (var id in new[] { saved.Id, Guid.NewGuid() })
        {
            using var request = new HttpRequestMessage(operation == "metadata" ? HttpMethod.Put :
                operation == "delete" ? HttpMethod.Delete : HttpMethod.Post,
                operation switch
                {
                    "metadata" => $"/api/nests/{id}/metadata",
                    "delete" => $"/api/nests/{id}",
                    "restore" => $"/api/nests/{id}/revisions/1/restore",
                    _ => $"/api/nests/deleted/{id}/recover"
                });
            request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            if (operation == "metadata") request.Content = JsonContent.Create(new NestRecord());
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(operation == "recover" ? 1 : 2, reopened.History(saved.Id, 0, 100, operation != "recover")!.Total);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("delete")]
    [InlineData("restore")]
    [InlineData("recover")]
    public async Task ConcurrentMutation_OnlyOneExpectedRevisionWins(string operation)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var repository = new RemoteNestRepository(client, client.BaseAddress!.ToString());
        var saved = await repository.UploadAsync(TestNests.Archive(1), new NestRecord { Name = "Fixture" });
        var expected = 1;
        if (operation == "recover") { await repository.DeleteAsync(saved.Id, 1); expected = 2; }
        var attempts = Enumerable.Range(0, 2).Select(async i =>
        {
            using var request = new HttpRequestMessage(operation == "metadata" ? HttpMethod.Put :
                operation == "delete" ? HttpMethod.Delete : HttpMethod.Post,
                operation switch
                {
                    "metadata" => $"/api/nests/{saved.Id}/metadata",
                    "delete" => $"/api/nests/{saved.Id}",
                    "restore" => $"/api/nests/{saved.Id}/revisions/1/restore",
                    _ => $"/api/nests/deleted/{saved.Id}/recover"
                });
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{expected}\"");
            if (operation == "metadata") request.Content = JsonContent.Create(new NestRecord { Name = $"Writer {i}" });
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        });
        var statuses = await Task.WhenAll(attempts);
        Assert.Single(statuses.Where(s => s == HttpStatusCode.OK));
        Assert.Single(statuses.Where(s => s == (operation is "delete" or "recover" ? HttpStatusCode.NotFound : HttpStatusCode.Conflict)));
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(expected + 1, reopened.History(saved.Id, 0, 100, operation == "delete")!.Total);
        Assert.Equal(TestNests.Archive(1), reopened.GetRevisionFile(saved.Id, 1, operation == "delete"));
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
