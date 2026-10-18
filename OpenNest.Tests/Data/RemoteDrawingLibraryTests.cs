using System.Net;
using System.Security.Cryptography;
using OpenNest.Data;

namespace OpenNest.Tests.Data;

public class RemoteDrawingLibraryTests
{
    private static readonly byte[] Package = { (byte)'P', (byte)'K', 3, 4, 9 };

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static DrawingLibraryRecord SampleRecord(Guid? id = null, int revision = 1) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Revision = revision,
        Name = "Synthetic bracket",
        Customer = "Synthetic customer",
        MaterialName = "Synthetic Steel",
        MaterialGrade = "Grade A",
        Thickness = 0.25,
        CutType = CutType.Waterjet,
        PackageSha256 = Sha(Package),
        PackageSize = Package.Length,
    };

    private static RemoteDrawingLibrary Library(StubHandler handler) =>
        new(new HttpClient(handler), "http://server:8090/base/");

    [Fact]
    public void Constructor_RejectsInvalidUrl()
    {
        Assert.Throws<ArgumentException>(() => new RemoteDrawingLibrary("not a url"));
        Assert.Throws<ArgumentException>(() => new RemoteDrawingLibrary("ftp://host"));
    }

    [Fact]
    public async Task QueryAsync_SendsEscapedFiltersAndReadsPage()
    {
        var record = SampleRecord();
        var handler = new StubHandler(_ => StubHandler.Json(new { items = new[] { record }, total = 7, offset = 5, limit = 5 }));
        using var library = Library(handler);

        var page = await library.QueryAsync(new DrawingLibraryQuery
        {
            Search = " 50%_\\ ",
            Customer = "A&B",
            MaterialName = "Carbon Steel",
            MaterialGrade = " ",
            Thickness = 0.123456,
            CutType = CutType.Plasma,
            IncludeArchived = true,
            Sort = DrawingSortField.MaterialGrade,
            Descending = false,
            Offset = 5,
            Limit = 5,
        });

        Assert.Equal(
            "http://server:8090/base/api/drawings/query?search=50%25_%5C&sort=materialGrade&order=asc&offset=5&limit=5"
            + "&customer=A%26B&materialName=Carbon%20Steel&thickness=0.1235&cutType=Plasma&includeArchived=true",
            handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal(record.Id, Assert.Single(page.Items).Id);
        Assert.Equal(CutType.Waterjet, page.Items[0].CutType);
        Assert.Equal(7, page.Total);
    }

    public static TheoryData<DrawingLibraryQuery> OutOfBoundsQueries() => new()
    {
        new DrawingLibraryQuery { Offset = -1 },
        new DrawingLibraryQuery { Limit = 0 },
        new DrawingLibraryQuery { Limit = DrawingLibraryQuery.MaxLimit + 1 },
        new DrawingLibraryQuery { Search = new string('x', DrawingLibraryQuery.MaxSearchLength + 1) },
        new DrawingLibraryQuery { Customer = "a\0b" },
        new DrawingLibraryQuery { Sort = (DrawingSortField)99 },
        new DrawingLibraryQuery { CutType = (CutType)9 },
        new DrawingLibraryQuery { Thickness = double.NaN },
        new DrawingLibraryQuery { Thickness = -0.1 },
    };

    [Theory]
    [MemberData(nameof(OutOfBoundsQueries))]
    public async Task QueryAsync_OutOfBounds_IsRefusedBeforeSending(DrawingLibraryQuery query)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not send"));
        using var library = Library(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => library.QueryAsync(query));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task QueryAsync_OversizedPage_IsRefused()
    {
        var handler = new StubHandler(_ => StubHandler.Json(new { items = new[] { SampleRecord(), SampleRecord() }, total = 2 }));
        using var library = Library(handler);

        await Assert.ThrowsAsync<IOException>(() => library.QueryAsync(new DrawingLibraryQuery { Limit = 1 }));
    }

    [Fact]
    public async Task OlderServer_IsReportedAndNeverFallsBack()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var library = Library(handler);
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<ServerTooOldException>(() => library.QueryAsync(new DrawingLibraryQuery()));
        await Assert.ThrowsAsync<ServerTooOldException>(() => library.UploadAsync(SampleRecord(), Package));
        await Assert.ThrowsAsync<ServerTooOldException>(() => library.GetAsync(id));
        await Assert.ThrowsAsync<ServerTooOldException>(() => library.GetPackageAsync(id));
        await Assert.ThrowsAsync<ServerTooOldException>(() => library.GetRevisionsAsync(id));
        await Assert.ThrowsAsync<ServerTooOldException>(() => library.UpdateMetadataAsync(id, SampleRecord(id), 1));
        Assert.All(handler.Requests, request => Assert.StartsWith("http://server:8090/base/api/drawings", request.RequestUri!.AbsoluteUri));
    }

    [Fact]
    public async Task UnknownDrawingOnCurrentServer_IsNullOrNamedFailure()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/query", StringComparison.Ordinal)
            ? StubHandler.Json(new { items = Array.Empty<DrawingLibraryRecord>() })
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var library = Library(handler);
        var id = Guid.NewGuid();

        Assert.Null(await library.GetAsync(id));
        Assert.Null(await library.GetPackageAsync(id));
        Assert.Null(await library.GetRevisionsAsync(id));
        Assert.Null(await library.GetRevisionPackageAsync(id, 2));
        var error = await Assert.ThrowsAsync<IOException>(() => library.ArchiveAsync(id, 1));
        Assert.IsNotType<ServerTooOldException>(error);
        Assert.Contains("no longer exists", error.Message);
    }

    [Fact]
    public async Task UploadAsync_InvalidInput_IsRefusedBeforeSending()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not send"));
        using var library = Library(handler);
        var unnamed = SampleRecord();
        unnamed.Name = " ";

        await Assert.ThrowsAsync<ArgumentException>(() => library.UploadAsync(SampleRecord(Guid.Empty), Package));
        await Assert.ThrowsAsync<ArgumentException>(() => library.UploadAsync(unnamed, Package));
        await Assert.ThrowsAsync<ArgumentException>(() => library.UploadAsync(SampleRecord(), new byte[] { 1, 2, 3, 4 }));
        await Assert.ThrowsAsync<ArgumentException>(() => library.UploadAsync(SampleRecord(), Array.Empty<byte>()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => library.ArchiveAsync(Guid.NewGuid(), 0));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UploadAsync_SendsMultipartAndVerifiesTheStoredPackage()
    {
        var record = SampleRecord();
        var handler = new StubHandler(_ => StubHandler.Json(record));
        using var library = Library(handler);

        var saved = await library.UploadAsync(record, Package);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("/base/api/drawings", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("name=metadata", handler.RequestBodies[0]);
        Assert.Contains("name=package", handler.RequestBodies[0]);
        Assert.Contains("\"cutType\":\"waterjet\"", handler.RequestBodies[0]);
        Assert.Equal(record.Id, saved.Id);
    }

    [Fact]
    public async Task UpdateMetadataAsync_SendsAndReadsNestingSettings()
    {
        var record = SampleRecord(revision: 2);
        record.Settings = new DrawingNestingSettings { CutFirst = true, Priority = 2, EndAngle = 1.5, ColorArgb = -16776961 };
        var handler = new StubHandler(_ => StubHandler.Json(record));
        using var library = Library(handler);

        var saved = await library.UpdateMetadataAsync(record.Id, record, 1);

        Assert.Equal(HttpMethod.Put, handler.Requests[0].Method);
        Assert.EndsWith($"/api/drawings/{record.Id}/metadata", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("\"settings\":{\"cutFirst\":true,\"priority\":2", handler.RequestBodies[0]);
        Assert.Equal(record.Settings, saved.Settings);
    }

    [Fact]
    public async Task QueryAsync_SendsTheCutFirstFilter()
    {
        var handler = new StubHandler(_ => StubHandler.Json(new { items = Array.Empty<DrawingLibraryRecord>(), total = 0 }));
        using var library = Library(handler);

        await library.QueryAsync(new DrawingLibraryQuery { CutFirst = true });

        Assert.Contains("&cutFirst=true", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task UploadAsync_DifferentStoredPackageOrId_IsRefused()
    {
        var record = SampleRecord();
        var otherPackage = SampleRecord(record.Id);
        otherPackage.PackageSha256 = "00";
        using var wrongHash = Library(new StubHandler(_ => StubHandler.Json(otherPackage)));
        using var wrongId = Library(new StubHandler(_ => StubHandler.Json(SampleRecord())));

        await Assert.ThrowsAsync<IOException>(() => wrongHash.UploadAsync(record, Package));
        await Assert.ThrowsAsync<IOException>(() => wrongId.UploadAsync(record, Package));
    }

    [Fact]
    public async Task Conflicts_CarryTheCurrentRecord()
    {
        var current = SampleRecord(revision: 4);
        var handler = new StubHandler(_ => StubHandler.Json(current, HttpStatusCode.Conflict));
        using var library = Library(handler);

        var upload = await Assert.ThrowsAsync<DrawingLibraryConflictException>(() => library.UploadAsync(SampleRecord(current.Id), Package));
        var update = await Assert.ThrowsAsync<DrawingLibraryConflictException>(() =>
            library.UpdatePackageAsync(current.Id, SampleRecord(current.Id), Package, 3));

        Assert.Equal(4, upload.Current.Revision);
        Assert.Equal(4, update.Current.Revision);
        Assert.Equal("\"3\"", Assert.Single(handler.Requests[1].Headers.IfMatch).Tag);
    }

    [Fact]
    public async Task GuardedWrites_SendIfMatchAndVerifyTheResult()
    {
        var id = Guid.NewGuid();
        var archived = SampleRecord(id, 6);
        archived.Archived = true;
        var handler = new StubHandler(_ => StubHandler.Json(archived));
        using var library = Library(handler);

        var result = await library.ArchiveAsync(id, 5);
        await Assert.ThrowsAsync<IOException>(() => library.RestoreAsync(id, 5));
        await Assert.ThrowsAsync<IOException>(() => library.UpdateMetadataAsync(id, SampleRecord(id), 7));

        Assert.True(result.Archived);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith($"/api/drawings/{id}/archive", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("\"5\"", Assert.Single(handler.Requests[0].Headers.IfMatch).Tag);
        Assert.EndsWith($"/api/drawings/{id}/restore", handler.Requests[1].RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Put, handler.Requests[2].Method);
        Assert.EndsWith($"/api/drawings/{id}/metadata", handler.Requests[2].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetRevisionsAsync_RefusesInvalidHistory()
    {
        var id = Guid.NewGuid();
        var unordered = new { items = new[] { SampleRecord(id, 1), SampleRecord(id, 2) }, total = 2, offset = 0, limit = 100 };
        var foreign = new { items = new[] { SampleRecord(Guid.NewGuid(), 1) }, total = 1, offset = 0, limit = 100 };
        using var first = Library(new StubHandler(_ => StubHandler.Json(unordered)));
        using var second = Library(new StubHandler(_ => StubHandler.Json(foreign)));

        await Assert.ThrowsAsync<IOException>(() => first.GetRevisionsAsync(id));
        await Assert.ThrowsAsync<IOException>(() => second.GetRevisionsAsync(id));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => first.GetRevisionsAsync(id, limit: 201));
    }
}
