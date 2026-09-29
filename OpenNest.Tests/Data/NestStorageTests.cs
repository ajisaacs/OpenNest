using System.Net;
using System.Text;
using System.Text.Json;
using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class NestStorageSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"opennest-storage-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_directory, "storage.json");

    [Fact]
    public void Load_MissingFile_DefaultsToFileMode()
    {
        var settings = NestStorageSettings.Load(FilePath);

        Assert.Equal(NestStorageMode.File, settings.Mode);
        Assert.Equal("", settings.ServerUrl);
        Assert.False(settings.IsDatabaseMode);
    }

    [Fact]
    public void Load_CorruptFile_DefaultsToFileModeWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ not json");

        var settings = NestStorageSettings.Load(FilePath);

        Assert.Equal(NestStorageMode.File, settings.Mode);
    }

    [Fact]
    public void SaveThenLoad_DatabaseMode_RoundTrips()
    {
        NestStorageSettings.Load(FilePath)
            .Save(FilePath); // ensure directory exists pattern
        var settings = new NestStorageSettings
        {
            Mode = NestStorageMode.Database,
            ServerUrl = "http://barge.lan:8090/",
        };

        settings.Save(FilePath);
        var loaded = NestStorageSettings.Load(FilePath);

        Assert.Equal(NestStorageMode.Database, loaded.Mode);
        Assert.Equal("http://barge.lan:8090", loaded.ServerUrl);
        Assert.True(loaded.IsDatabaseMode);
    }

    [Fact]
    public void DatabaseMode_WithoutServerUrl_IsNotActive()
    {
        var settings = new NestStorageSettings { Mode = NestStorageMode.Database };
        Assert.False(settings.IsDatabaseMode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}

/// <summary>Records requests and replies with canned responses.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> RequestBodies { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responder(request);
    }

    public static HttpResponseMessage Json(object payload, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
}

public class RemoteNestRepositoryTests
{
    private static NestRecord SampleRecord(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Job 1",
        Customer = "Cozy Cat",
        Material = "Aluminum",
        Thickness = 0.25,
        Status = NestStatus.ToBeCut,
        PlateCount = 2,
        PartCount = 17,
        Comments = "rush",
        MadeBy = "AJ",
        FileSize = 1234,
        SavedAt = new DateTime(2026, 9, 29, 12, 0, 0),
    };

    [Fact]
    public void Constructor_RejectsInvalidUrl()
    {
        Assert.Throws<ArgumentException>(() => new RemoteNestRepository("not a url"));
        Assert.Throws<ArgumentException>(() => new RemoteNestRepository("ftp://host"));
    }

    [Fact]
    public async Task ListAsync_DeserializesCamelCaseRecords()
    {
        var handler = new StubHandler(_ => StubHandler.Json(new[] { SampleRecord() }));
        using var repo = new RemoteNestRepository(new HttpClient(handler), "http://server:8090/");

        var records = await repo.ListAsync();

        Assert.Single(records);
        Assert.Equal("Cozy Cat", records[0].Customer);
        Assert.Equal(NestStatus.ToBeCut, records[0].Status);
        Assert.Contains("/api/nests", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task UploadAsync_PostsMultipartWithMetadataAndFile()
    {
        var record = SampleRecord();
        var handler = new StubHandler(_ => StubHandler.Json(record));
        using var repo = new RemoteNestRepository(new HttpClient(handler), "http://server:8090");

        var result = await repo.UploadAsync(new byte[] { 1, 2, 3 }, record);

        Assert.Equal(record.Id, result.Id);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/api/nests", request.RequestUri!.ToString());
        Assert.StartsWith("multipart/form-data", request.Content!.Headers.ContentType!.ToString());
        Assert.Contains("metadata", handler.RequestBodies[0]);
        Assert.Contains("toBeCut", handler.RequestBodies[0]);
        Assert.Contains("filename=\"Job 1.nest\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task UpdateFileAsync_PutsMultipartToIdFileRoute()
    {
        var id = Guid.NewGuid();
        var record = SampleRecord(id);
        var handler = new StubHandler(_ => StubHandler.Json(record));
        using var repo = new RemoteNestRepository(new HttpClient(handler), "http://server:8090");

        await repo.UpdateFileAsync(id, new byte[] { 9 }, record);

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith($"/api/nests/{id}/file", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetFileAsync_NullOnNotFound_BytesOtherwise()
    {
        var missing = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var missingRepo = new RemoteNestRepository(new HttpClient(missing), "http://s");
        Assert.Null(await missingRepo.GetFileAsync(Guid.NewGuid()));

        var found = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 4, 5, 6 }),
        });
        using var foundRepo = new RemoteNestRepository(new HttpClient(found), "http://s");
        Assert.Equal(new byte[] { 4, 5, 6 }, await foundRepo.GetFileAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetMetadataAsync_NullOnNotFound()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var repo = new RemoteNestRepository(new HttpClient(handler), "http://s");

        Assert.Null(await repo.GetMetadataAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateMetadataAsync_PutsJsonToMetadataRoute()
    {
        var id = Guid.NewGuid();
        var record = SampleRecord(id);
        var handler = new StubHandler(_ => StubHandler.Json(record));
        using var repo = new RemoteNestRepository(new HttpClient(handler), "http://s");

        await repo.UpdateMetadataAsync(id, record);

        Assert.Equal(HttpMethod.Put, handler.Requests[0].Method);
        Assert.EndsWith($"/api/nests/{id}/metadata", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("application/json", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWithStatusDetailOnError()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("disk on fire"),
        });
        using var repo = new RemoteNestRepository(new HttpClient(handler), "http://s");

        var ex = await Assert.ThrowsAsync<IOException>(() => repo.DeleteAsync(Guid.NewGuid()));
        Assert.Contains("500", ex.Message);
        Assert.Contains("disk on fire", ex.Message);
    }
}
