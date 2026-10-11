using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class RemoteNestHistoryTests
{
    [Fact]
    public async Task LegacyServerIgnoringHeaders_IsNeverSentAnUpdate()
    {
        var mutations = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                mutations++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new NestRecord()) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        using var repository = new RemoteNestRepository(http, "http://legacy/");
        await Assert.ThrowsAsync<IOException>(() => repository.UpdateFileAsync(Guid.NewGuid(), [1], new NestRecord()));
        Assert.Equal(0, mutations);
    }

    [Fact]
    public async Task GuardedUpdate_PreflightsHistoryAndSendsQuotedRevision()
    {
        var id = Guid.NewGuid();
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Get)
                return History(id);
            Assert.Equal("\"4\"", Assert.Single(request.Headers.IfMatch).Tag);
            return Json(new NestRecord { Id = id, Revision = 5 });
        }));
        using var repository = new RemoteNestRepository(http, "http://shop/");
        var saved = await repository.UpdateFileAsync(id, [0, 255], new NestRecord(), 4);
        Assert.Equal(5, saved.Revision);
        Assert.Equal(new[] { $"/api/nests/{id}/revisions?offset=0&limit=1", $"/api/nests/{id}/file" }, paths);
    }

    [Fact]
    public async Task KnownRevisionAgainstLegacyServer_PreflightRefusesBeforeMutation()
    {
        var mutations = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Get) mutations++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        using var repository = new RemoteNestRepository(http, "http://legacy/");
        var error = await Assert.ThrowsAsync<IOException>(() => repository.UpdateFileAsync(Guid.NewGuid(), [1], new NestRecord(), 4));
        Assert.Contains("Upgrade the nest server", error.Message);
        Assert.Equal(0, mutations);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 201)]
    public async Task InvalidBounds_AreRefusedBeforeHttp(int offset, int limit)
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("HTTP must not run")));
        using var repository = new RemoteNestRepository(http, "http://shop/");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetRevisionsAsync(Guid.NewGuid(), offset, limit));
    }

    [Fact]
    public async Task HistoryAndDownload_AreBoundedAndByteExact_UnknownVersionIsNull()
    {
        var id = Guid.NewGuid();
        byte[] bytes = [0, 255, 13, 10, 128];
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Query.Length > 0)
                return Json(new NestRevisionPage { Items = [new NestRecord { Id = id, Revision = 3, Comments = "earlier" }], Total = 5, Offset = 2, Limit = 1 });
            if (request.RequestUri.AbsolutePath.EndsWith("/999/file")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            Assert.EndsWith("/3/file", request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        using var repository = new RemoteNestRepository(http, "http://shop/");
        var page = await repository.GetRevisionsAsync(id, 2, 1);
        Assert.Equal("earlier", Assert.Single(page.Items).Comments);
        Assert.Equal(bytes, await repository.GetRevisionFileAsync(id, 3));
        Assert.Null(await repository.GetRevisionFileAsync(id, 999));
    }

    [Fact]
    public async Task UnboundedOrInvalidHistoryResponse_IsNotCapabilityProof()
    {
        var id = Guid.NewGuid();
        var writes = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return Json(new NestRevisionPage { Items = [new NestRecord { Id = id, Revision = 1 }, new NestRecord { Id = id, Revision = 1 }], Total = 2, Limit = 1 });
        }));
        using var repository = new RemoteNestRepository(http, "http://shop/");
        await Assert.ThrowsAsync<IOException>(() => repository.UpdateFileAsync(id, [1], new NestRecord(), 1));
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(428)]
    public async Task FailedGuardedUpdate_IsNotRetried(int status)
    {
        var id = Guid.NewGuid();
        var writes = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Get) return History(id);
            writes++;
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("current revision 5") };
        }));
        using var repository = new RemoteNestRepository(http, "http://shop/");
        var error = await Assert.ThrowsAsync<IOException>(() => repository.UpdateFileAsync(id, [1], new NestRecord(), 4));
        Assert.Contains(status.ToString(), error.Message);
        Assert.Contains("current revision 5", error.Message);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task CancelledHistoryAndDownload_PropagateCancellation()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Cancelled HTTP must not run")));
        using var repository = new RemoteNestRepository(http, "http://shop/");
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetRevisionsAsync(Guid.NewGuid(), cancellationToken: source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetRevisionFileAsync(Guid.NewGuid(), 1, source.Token));
    }

    [Fact]
    public async Task LegacyMetadataAndDelete_AreNeverSentToAnOldServer()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Unsafe mutation")));
        using var repository = new RemoteNestRepository(http, "http://legacy/");
        await Assert.ThrowsAsync<IOException>(() => repository.UpdateMetadataAsync(Guid.NewGuid(), new NestRecord()));
        await Assert.ThrowsAsync<IOException>(() => repository.DeleteAsync(Guid.NewGuid()));
    }

    private static HttpResponseMessage History(Guid id) => Json(new NestRevisionPage
    {
        Items = [new NestRecord { Id = id, Revision = 4 }],
        Total = 4,
        Limit = 1,
    });

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(reply(request));
        }
    }
}
