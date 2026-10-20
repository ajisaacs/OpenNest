using System.Net;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

public sealed class NestHistoryClientTests
{
    [Fact]
    public async Task LostCreateReply_BlocksReplayAndReconcilesSameId()
    {
        using var factory = new ServerFactory();
        using var http = factory.CreateDefaultClient(new LostReplyHandler());
        using var repository = new RemoteNestRepository(http, http.BaseAddress!.ToString());
        var session = new NestSaveSession();
        var nest = NestApiTests.SyntheticNest();
        var archive = NestApiTests.Archive(nest);
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(repository, http.BaseAddress.ToString(), nest, archive));
        var pendingId = session.PendingCreateId;
        Assert.NotEqual(Guid.Empty, pendingId);
        Assert.True(session.RequiresReload);
        Assert.Single(await repository.ListAsync());
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(repository, http.BaseAddress.ToString(), nest, archive));
        Assert.Single(await repository.ListAsync());
        var reconciled = await session.ReconcilePendingCreateAsync(repository, http.BaseAddress.ToString());
        Assert.Equal(pendingId, reconciled!.Id);
        Assert.Equal(pendingId, session.RemoteId);
        Assert.Equal(Guid.Empty, session.PendingCreateId);
        Assert.Single(await repository.ListAsync());
        Assert.Equal(archive, await repository.GetRevisionFileAsync(pendingId, 1));
    }

    private sealed class LostReplyHandler : DelegatingHandler
    {
        private bool lost;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.Method == HttpMethod.Post && !lost)
            {
                lost = true;
                response.Dispose();
                throw new IOException("Response lost after commit.");
            }
            return response;
        }
    }

    [Fact]
    public async Task LostCreateReply_ChangedServerMetadataCannotBindWithoutReload()
    {
        using var factory = new ServerFactory();
        using var http = factory.CreateDefaultClient(new LostReplyHandler());
        using var repository = new RemoteNestRepository(http, http.BaseAddress!.ToString());
        var session = new NestSaveSession();
        var nest = NestApiTests.SyntheticNest();
        var archive = NestApiTests.Archive(nest);
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(repository, http.BaseAddress.ToString(), nest, archive));
        var id = session.PendingCreateId;
        var current = (await repository.GetMetadataAsync(id))!;
        current.Comments = "Changed by another editor";
        await repository.UpdateMetadataAsync(id, current, current.Revision);
        await Assert.ThrowsAsync<IOException>(() => session.ReconcilePendingCreateAsync(repository, http.BaseAddress.ToString()));
        Assert.Equal(Guid.Empty, session.RemoteId);
        Assert.True(session.RequiresReload);
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(repository, http.BaseAddress.ToString(), nest, archive));
        Assert.Single(await repository.ListAsync());
    }

    private sealed class FirstPostFailsBeforeSend : DelegatingHandler
    {
        private bool failed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post && !failed)
            {
                failed = true;
                throw new IOException("Upload was not sent.");
            }
            return base.SendAsync(request, ct);
        }
    }

    [Fact]
    public async Task AbsentPendingUpload_RetriesSameIdOnlyAfterExplicitReconciliation()
    {
        using var factory = new ServerFactory();
        using var http = factory.CreateDefaultClient(new FirstPostFailsBeforeSend());
        using var repository = new RemoteNestRepository(http, http.BaseAddress!.ToString());
        var session = new NestSaveSession();
        var nest = NestApiTests.SyntheticNest();
        var archive = NestApiTests.Archive(nest);
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(repository, http.BaseAddress.ToString(), nest, archive));
        var pending = session.PendingCreateId;
        Assert.Empty(await repository.ListAsync());
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(repository, http.BaseAddress.ToString(), nest, archive));
        Assert.Null(await session.ReconcilePendingCreateAsync(repository, http.BaseAddress.ToString()));
        Assert.False(session.RequiresReload);
        var created = await session.SaveAsync(repository, http.BaseAddress.ToString(), nest, archive);
        Assert.Equal(pending, created.Id);
        Assert.Equal(1, created.Revision);
        Assert.Single(await repository.ListAsync());
    }

    [Fact]
    public async Task AbsentPendingSaveAs_NeverUpdatesBoundOriginalOnRetry()
    {
        using var factory = new ServerFactory();
        using var seedHttp = factory.CreateClient();
        using var seed = new RemoteNestRepository(seedHttp, seedHttp.BaseAddress!.ToString());
        var nest = NestApiTests.SyntheticNest();
        var originalBytes = NestApiTests.Archive(nest);
        var original = await seed.UploadAsync(originalBytes, new NestRecord { Name = "Original" });
        using var http = factory.CreateDefaultClient(new FirstPostFailsBeforeSend());
        using var repository = new RemoteNestRepository(http, http.BaseAddress!.ToString());
        var session = new NestSaveSession();
        session.Bind(original.Id, http.BaseAddress.ToString(), original.Revision);
        nest.Name = "Copied nest";
        var copyBytes = NestApiTests.Archive(nest);
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(repository, http.BaseAddress.ToString(), nest, copyBytes, saveCopy: true));
        var pending = session.PendingCreateId;
        Assert.Null(await session.ReconcilePendingCreateAsync(repository, http.BaseAddress.ToString()));
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(repository, http.BaseAddress.ToString(), nest, copyBytes));
        var copy = await session.SaveAsync(repository, http.BaseAddress.ToString(), nest, copyBytes, saveCopy: true);
        Assert.Equal(pending, copy.Id);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(1, (await repository.GetMetadataAsync(original.Id))!.Revision);
        Assert.Equal(originalBytes, await repository.GetFileAsync(original.Id));
        Assert.Equal(copyBytes, await repository.GetFileAsync(copy.Id));
    }

    [Fact]
    public async Task RealClient_CreatesAndUpdatesOneNestWithExactHistoricalArchives()
    {
        using var factory = new ServerFactory();
        using var http = factory.CreateClient();
        using var repository = new RemoteNestRepository(http, http.BaseAddress!.ToString());
        var id = Guid.NewGuid();
        var first = TestNests.Archive(80, 75, 0, 1, 255);
        var second = TestNests.Archive(80, 75, 0, 2, 254);
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
        var created = await repository.UploadAsync(TestNests.Archive(1, 2, 3), new NestRecord { Name = "Synthetic" });
        using var response = await http.DeleteAsync($"/api/nests/{created.Id}");
        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.Equal(TestNests.Archive(1, 2, 3), await repository.GetRevisionFileAsync(created.Id, 1));
    }
}
