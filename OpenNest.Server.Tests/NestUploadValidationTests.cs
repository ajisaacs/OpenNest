using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

/// <summary>
/// Client-allocated ids and uploaded nest metadata: conflicts are 409 and invalid
/// metadata is 400, never a 500, and neither changes stored records.
/// </summary>
public sealed class NestUploadValidationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Create_WithExistingId_Returns409WithCurrentRecordAndKeepsIt()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        using var created = await client.PostAsync("/api/nests", NestRevisionTests.Body(new NestRecord { Id = id, Name = "Original" }, [1]));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var duplicate = await client.PostAsync("/api/nests", NestRevisionTests.Body(new NestRecord { Id = id, Name = "Replacement" }, [2]));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var current = (await duplicate.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        Assert.Equal(id, current.Id);
        Assert.Equal("Original", current.Name);
        Assert.Equal(new byte[] { 1 }, await client.GetByteArrayAsync($"/api/nests/{id}/file"));
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(1, reopened.History(id, 0, 100)!.Total);
    }

    [Fact]
    public async Task Create_WithIdOfDeletedNest_Returns409AndKeepsItRecoverable()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        using var created = await client.PostAsync("/api/nests", NestRevisionTests.Body(new NestRecord { Id = id, Name = "Original" }, [1]));
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/nests/{id}");
        delete.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var deleted = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using var duplicate = await client.PostAsync("/api/nests", NestRevisionTests.Body(new NestRecord { Id = id, Name = "Replacement" }, [2]));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var current = (await duplicate.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        Assert.True(current.Deleted);
        Assert.Equal(2, current.Revision);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/nests/{id}")).StatusCode);
        Assert.Equal(new byte[] { 1 }, await client.GetByteArrayAsync($"/api/nests/deleted/{id}/file"));
    }

    [Theory]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":"A","customer":null}""")]
    [InlineData("""{"name":"A","material":null}""")]
    [InlineData("""{"name":"A","comments":null}""")]
    [InlineData("""{"name":"A","madeBy":null}""")]
    [InlineData("""{"name":"A","status":99}""")]
    [InlineData("""{"name":"A","status":"cancelled"}""")]
    [InlineData("""{"name":"A","status":4}""")]
    public async Task Create_WithInvalidMetadata_Returns400AndStoresNothing(string metadata)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/nests", RawBody(metadata, [1]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Empty(reopened.List());
    }

    [Theory]
    [InlineData("""{"name":"A","status":"toBeCut"}""", NestStatus.ToBeCut)]
    [InlineData("""{"name":"A","status":2}""", NestStatus.HasBeenCut)]
    [InlineData("""{"name":"A","status":"draft"}""", NestStatus.Draft)]
    [InlineData("""{"name":"A","status":3}""", NestStatus.Draft)]
    public async Task Create_WithDefinedStatus_StoresIt(string metadata, NestStatus expected)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/nests", RawBody(metadata, [1]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, (await response.Content.ReadFromJsonAsync<NestRecord>(Options))!.Status);
    }

    [Theory]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":"A","status":99}""")]
    public async Task UpdateFile_WithInvalidMetadata_Returns400AndLeavesRecordUnchanged(string metadata)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var id = await Create(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");

        using var response = await client.PutAsync($"/api/nests/{id}/file", RawBody(metadata, [2]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchanged(factory, client, id);
    }

    [Theory]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":"A","comments":null}""")]
    [InlineData("""{"name":"A","status":99}""")]
    public async Task UpdateMetadata_WithInvalidMetadata_Returns400AndLeavesRecordUnchanged(string metadata)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var id = await Create(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");

        using var response = await client.PutAsync($"/api/nests/{id}/metadata",
            new StringContent(metadata, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchanged(factory, client, id);
    }

    private static async Task<Guid> Create(HttpClient client)
    {
        using var created = await client.PostAsync("/api/nests", NestRevisionTests.Body(new NestRecord { Name = "Original" }, [1]));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<NestRecord>(Options))!.Id;
    }

    private static async Task AssertUnchanged(ServerFactory factory, HttpClient client, Guid id)
    {
        Assert.Equal(new byte[] { 1 }, await client.GetByteArrayAsync($"/api/nests/{id}/file"));
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        var current = reopened.Get(id)!;
        Assert.Equal("Original", current.Name);
        Assert.Equal(1, current.Revision);
        Assert.Equal(1, reopened.History(id, 0, 100)!.Total);
    }

    private static MultipartFormDataContent RawBody(string metadata, byte[] bytes)
    {
        var body = new MultipartFormDataContent();
        body.Add(new StringContent(metadata), "metadata");
        body.Add(new ByteArrayContent(bytes), "file", "fixture.nest");
        return body;
    }
}
