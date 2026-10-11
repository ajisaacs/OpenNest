using System.Net;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

public sealed class NestHistoryClientTests
{
    [Fact]
    public async Task RealClient_CreatesAndUpdatesOneNestWithExactHistoricalArchives()
    {
        using var factory = new ServerFactory();
        using var http = factory.CreateClient();
        using var repository = new RemoteNestRepository(http, http.BaseAddress!.ToString());
        var id = Guid.NewGuid();
        var first = new byte[] { 80, 75, 0, 1, 255 };
        var second = new byte[] { 80, 75, 0, 2, 254 };
        var created = await repository.UploadAsync(first, new NestRecord { Id = id, Name = "Synthetic" });
        Assert.Equal(1, created.Revision);

        var updated = await repository.UpdateFileAsync(id, second, new NestRecord { Id = id, Name = "Synthetic two" }, 1);
        Assert.Equal(2, updated.Revision);
        var page = await repository.GetRevisionsAsync(id, limit: 1);
        Assert.Equal(2, page.Total);
        Assert.Equal(2, Assert.Single(page.Items).Revision);
        Assert.Equal("Synthetic", Assert.Single((await repository.GetRevisionsAsync(id, offset: 1, limit: 1)).Items).Name);
        Assert.Equal(first, await repository.GetRevisionFileAsync(id, 1));
        Assert.Equal(second, await repository.GetRevisionFileAsync(id, 2));
        Assert.Equal(second, await repository.GetFileAsync(id));

        var conflict = await Assert.ThrowsAsync<IOException>(() =>
            repository.UpdateFileAsync(id, first, created, 1));
        Assert.Contains("409", conflict.Message);
        Assert.Equal(2, (await repository.GetRevisionsAsync(id, limit: 1)).Total);
        Assert.Equal(second, await repository.GetFileAsync(id));
    }

    [Fact]
    public async Task LegacyUnguardedDelete_IsRefusedWithoutMutation()
    {
        using var factory = new ServerFactory();
        using var http = factory.CreateClient();
        using var repository = new RemoteNestRepository(http, http.BaseAddress!.ToString());
        var created = await repository.UploadAsync([1, 2, 3], new NestRecord { Name = "Synthetic" });
        using var response = await http.DeleteAsync($"/api/nests/{created.Id}");
        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 3 }, await repository.GetRevisionFileAsync(created.Id, 1));
    }
}
