using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.IO;
using CncProgram = OpenNest.CNC.Program;

namespace OpenNest.Server.Tests;

/// <summary>
/// Production HTTP routes + real SQLite, driven through the desktop app's own
/// <see cref="RemoteNestRepository"/> and <see cref="NestSaveSession"/>.
/// Every test owns a fresh server and database.
/// </summary>
public sealed class NestApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Host_UsesPrivateDatabase_NotDefaultPath()
    {
        var defaultPath = Path.GetFullPath(Path.Combine("data", "nests.db"));
        Assert.False(File.Exists(defaultPath), "Precondition: no default database in the test directory.");

        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(factory.DatabasePath));
        Assert.False(File.Exists(defaultPath));
    }

    [Fact]
    public async Task Create_ThenListAndDownload_ReturnsExactRecordAndArchive()
    {
        using var server = new TestServerSession();
        var nest = SyntheticNest();
        var archive = Archive(nest);
        var session = new NestSaveSession();

        var created = await session.SaveAsync(server.Repository, server.Url, nest, archive);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotEqual(default, created.SavedAt);
        Assert.Equal(archive.LongLength, created.FileSize);
        AssertSameMetadata(Expected(nest, created, archive), created);

        var listed = Assert.Single(await server.Repository.ListAsync());
        AssertSameMetadata(created, listed);
        AssertSameMetadata(created, (await server.Repository.GetMetadataAsync(created.Id))!);

        var downloaded = await server.Repository.GetFileAsync(created.Id);
        Assert.Equal(archive, downloaded);
        AssertArchive(downloaded!, nest);
    }

    [Fact]
    public async Task Save_AfterCreate_UpdatesSameRecordArchiveAndMetadata()
    {
        using var server = new TestServerSession();
        var nest = SyntheticNest();
        var session = new NestSaveSession();
        var created = await session.SaveAsync(server.Repository, server.Url, nest, Archive(nest));

        nest.Name = "Synthetic integration nest updated";
        nest.Status = NestStatus.ToBeCut;
        nest.Plates[0].Parts[0].Location = new Vector(4, 5);
        var archive = Archive(nest);
        var updated = await session.SaveAsync(server.Repository, server.Url, nest, archive);

        Assert.Equal(created.Id, updated.Id);
        AssertSameMetadata(Expected(nest, updated, archive), updated);
        var listed = Assert.Single(await server.Repository.ListAsync());
        AssertSameMetadata(updated, listed);
        var downloaded = await server.Repository.GetFileAsync(created.Id);
        Assert.Equal(archive, downloaded);
        AssertArchive(downloaded!, nest);
    }

    [Fact]
    public async Task SaveCopy_CreatesDistinctRecordAndLeavesOriginal()
    {
        using var server = new TestServerSession();
        var nest = SyntheticNest();
        var session = new NestSaveSession();
        var originalArchive = Archive(nest);
        var original = await session.SaveAsync(server.Repository, server.Url, nest, originalArchive);

        nest.Name = "Synthetic integration nest copy";
        var copyArchive = Archive(nest);
        var copy = await session.SaveAsync(server.Repository, server.Url, nest, copyArchive, saveCopy: true);

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(copy.Id, session.RemoteId);
        var ids = (await server.Repository.ListAsync()).Select(r => r.Id).Order().ToArray();
        Assert.Equal(new[] { original.Id, copy.Id }.Order().ToArray(), ids);
        AssertSameMetadata(original, (await server.Repository.GetMetadataAsync(original.Id))!);
        Assert.Equal(originalArchive, await server.Repository.GetFileAsync(original.Id));
        Assert.Equal(copyArchive, await server.Repository.GetFileAsync(copy.Id));
    }

    [Fact]
    public async Task MetadataUpdate_ChangesFieldsWithoutTouchingArchiveOrFileSize()
    {
        using var server = new TestServerSession();
        var nest = SyntheticNest();
        var archive = Archive(nest);
        var created = await new NestSaveSession().SaveAsync(server.Repository, server.Url, nest, archive);

        var change = Clone(created);
        change.Name = "Renamed by metadata update";
        change.Status = NestStatus.HasBeenCut;
        change.Comments = "Metadata-only change";
        change.FileSize = 1; // Server-computed; must be ignored.
        var updated = await server.Repository.UpdateMetadataAsync(created.Id, change);

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Renamed by metadata update", updated.Name);
        Assert.Equal(NestStatus.HasBeenCut, updated.Status);
        Assert.Equal("Metadata-only change", updated.Comments);
        Assert.Equal(archive.LongLength, updated.FileSize);
        AssertSameMetadata(updated, (await server.Repository.GetMetadataAsync(created.Id))!);
        Assert.Equal(archive, await server.Repository.GetFileAsync(created.Id));
    }

    [Fact]
    public async Task Delete_RemovesRecordAndArchive()
    {
        using var server = new TestServerSession();
        var nest = SyntheticNest();
        var created = await new NestSaveSession().SaveAsync(server.Repository, server.Url, nest, Archive(nest));

        await server.Repository.DeleteAsync(created.Id);

        Assert.Empty(await server.Repository.ListAsync());
        Assert.Null(await server.Repository.GetMetadataAsync(created.Id));
        Assert.Null(await server.Repository.GetFileAsync(created.Id));
    }

    public static TheoryData<string, string> InvalidUploads()
    {
        var data = new TheoryData<string, string>();
        foreach (var route in new[] { "create", "update" })
        {
            foreach (var body in new[]
                     {
                         "not multipart", "malformed multipart", "missing boundary", "missing metadata",
                         "invalid metadata JSON", "null metadata", "missing file", "empty file",
                     })
            {
                data.Add(route, body);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidUploads))]
    public async Task Upload_WithMissingOrMalformedParts_Returns400AndLeavesStoreUnchanged(string route, string body)
    {
        using var server = new TestServerSession();
        var existing = await server.SeedAsync();
        var before = await server.SnapshotAsync();

        var method = route == "create" ? HttpMethod.Post : HttpMethod.Put;
        var path = route == "create" ? "/api/nests" : $"/api/nests/{existing.Id}/file";
        using var request = new HttpRequestMessage(method, path) { Content = InvalidUploadContent(body) };
        using var response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await server.SnapshotAsync());
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("null")]
    public async Task MetadataUpdate_WithInvalidJson_Returns400AndLeavesStoreUnchanged(string json)
    {
        using var server = new TestServerSession();
        var existing = await server.SeedAsync();
        var before = await server.SnapshotAsync();

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await server.Client.PutAsync($"/api/nests/{existing.Id}/metadata", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await server.SnapshotAsync());
    }

    [Fact]
    public async Task UnknownId_Returns404OnEveryRouteWithoutCreatingRecords()
    {
        using var server = new TestServerSession();
        var existing = await server.SeedAsync();
        var before = await server.SnapshotAsync();
        var unknown = Guid.NewGuid();
        var record = Clone(existing);
        record.Id = unknown;

        var requests = new Func<HttpRequestMessage>[]
        {
            () => new HttpRequestMessage(HttpMethod.Get, $"/api/nests/{unknown}"),
            () => new HttpRequestMessage(HttpMethod.Get, $"/api/nests/{unknown}/file"),
            () => new HttpRequestMessage(HttpMethod.Put, $"/api/nests/{unknown}/file")
            {
                Content = Multipart(JsonSerializer.Serialize(record, JsonOptions), new byte[] { 1, 2, 3 }),
            },
            () => new HttpRequestMessage(HttpMethod.Put, $"/api/nests/{unknown}/metadata")
            {
                Content = new StringContent(JsonSerializer.Serialize(record, JsonOptions), Encoding.UTF8,
                    "application/json"),
            },
            () => new HttpRequestMessage(HttpMethod.Delete, $"/api/nests/{unknown}"),
        };

        foreach (var create in requests)
        {
            using var request = create();
            using var response = await server.Client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                $"{request.Method} {request.RequestUri} returned {(int)response.StatusCode}, expected 404.");
        }

        Assert.Equal(before, await server.SnapshotAsync());
    }

    [Fact]
    public void Startup_WithUnusableDatabasePath_Fails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennest-server-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // A regular file where the data directory should be: the database cannot be created.
            var blocker = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blocker, "");
            using var factory = new ServerFactory(Path.Combine(blocker, "nests.db"));

            var error = Record.Exception(() => factory.CreateClient());

            Assert.NotNull(error);
            Assert.False(File.Exists(Path.Combine(blocker, "nests.db")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Dispose_RemovesTemporaryDatabaseDirectory()
    {
        string directory;
        using (var server = new TestServerSession())
        {
            await server.SeedAsync();
            directory = server.Factory.DirectoryPath;
            Assert.True(File.Exists(server.Factory.DatabasePath));
        }

        Assert.False(Directory.Exists(directory));
    }

    private static HttpContent InvalidUploadContent(string body)
    {
        var metadata = JsonSerializer.Serialize(new NestRecord { Name = "Synthetic rejected upload" }, JsonOptions);
        switch (body)
        {
            case "not multipart":
                return new StringContent(metadata, Encoding.UTF8, "application/json");
            case "malformed multipart":
                var content = new StringContent("--other-boundary\r\nnot a multipart body");
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=expected-boundary");
                return content;
            case "missing boundary":
                var unbounded = Multipart(metadata, new byte[] { 1 });
                unbounded.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
                return unbounded;
            case "missing metadata":
                return Multipart(null, new byte[] { 1 });
            case "invalid metadata JSON":
                return Multipart("{not-json", new byte[] { 1 });
            case "null metadata":
                return Multipart("null", new byte[] { 1 });
            case "missing file":
                return Multipart(metadata, null);
            case "empty file":
                return Multipart(metadata, Array.Empty<byte>());
            default:
                throw new ArgumentOutOfRangeException(nameof(body), body, null);
        }
    }

    private static MultipartFormDataContent Multipart(string? metadata, byte[]? file)
    {
        var content = new MultipartFormDataContent();
        if (metadata is not null)
            content.Add(new StringContent(metadata, Encoding.UTF8, "application/json"), "metadata");
        if (file is not null)
            content.Add(new ByteArrayContent(file), "file", "synthetic.nest");
        return content;
    }

    private static NestRecord Expected(Nest nest, NestRecord saved, byte[] archive)
    {
        var expected = NestRecordFactory.FromNest(nest, saved.Id, archive.LongLength);
        expected.SavedAt = saved.SavedAt;
        return expected;
    }

    private static NestRecord Clone(NestRecord record) =>
        JsonSerializer.Deserialize<NestRecord>(JsonSerializer.Serialize(record, JsonOptions), JsonOptions)!;

    private static void AssertSameMetadata(NestRecord expected, NestRecord actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected, JsonOptions), JsonSerializer.Serialize(actual, JsonOptions));

    private static void AssertArchive(byte[] archive, Nest expected)
    {
        using var stream = new MemoryStream(archive, writable: false);
        var reader = new NestReader(stream);
        var nest = reader.Read();

        Assert.Empty(reader.Warnings);
        Assert.Equal(expected.Name, nest.Name);
        Assert.Equal(expected.Status, nest.Status);
        var part = Assert.Single(Assert.Single(nest.Plates).Parts);
        Assert.Equal(expected.Plates[0].Parts[0].Location.X, part.Location.X);
        Assert.Equal(expected.Plates[0].Parts[0].Location.Y, part.Location.Y);
        Assert.Equal(18, Assert.Single(nest.Drawings).Area);
    }

    internal static Nest SyntheticNest()
    {
        var program = new CncProgram();
        program.MoveTo(0, 0);
        program.LineTo(6, 0);
        program.LineTo(6, 3);
        program.LineTo(0, 3);
        program.LineTo(0, 0);
        var drawing = new Drawing("Synthetic rectangle", program);
        drawing.Quantity.Required = 1;
        var nest = new Nest("Synthetic integration nest")
        {
            Customer = "Synthetic test customer",
            DateCreated = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified),
            Material = new Material("Synthetic alloy", "test-grade", 0.25),
            Thickness = 0.25,
            Status = NestStatus.Quote,
            Notes = "Neutral generated fixture; no customer data",
            MadeBy = "Synthetic test author",
            Units = Units.Inches,
        };
        nest.Drawings.Add(drawing);
        var plate = new Plate(new Size(20, 30)) { Quantity = 1 };
        plate.Parts.Add(new Part(drawing, new Vector(2, 3)));
        nest.Plates.Add(plate);
        return nest;
    }

    internal static byte[] Archive(Nest nest)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    /// <summary>One in-memory server, its HTTP client, and the real storage client over it.</summary>
    private sealed class TestServerSession : IDisposable
    {
        public TestServerSession()
        {
            Factory = new ServerFactory();
            Client = Factory.CreateClient();
            Url = Client.BaseAddress!.ToString();
            Repository = new RemoteNestRepository(Client, Url);
        }

        public ServerFactory Factory { get; }
        public HttpClient Client { get; }
        public string Url { get; }
        public RemoteNestRepository Repository { get; }

        public async Task<NestRecord> SeedAsync()
        {
            var nest = SyntheticNest();
            return await new NestSaveSession().SaveAsync(Repository, Url, nest, Archive(nest));
        }

        /// <summary>Every record's metadata plus its archive hash, in id order.</summary>
        public async Task<string> SnapshotAsync()
        {
            var lines = new List<string>();
            foreach (var record in (await Repository.ListAsync()).OrderBy(r => r.Id))
            {
                var file = await Repository.GetFileAsync(record.Id);
                Assert.NotNull(file);
                lines.Add(JsonSerializer.Serialize(record, JsonOptions) + " " + Convert.ToHexString(SHA256.HashData(file!)));
            }

            return string.Join("\n", lines);
        }

        public void Dispose()
        {
            Repository.Dispose();
            Client.Dispose();
            Factory.Dispose();
        }
    }
}
