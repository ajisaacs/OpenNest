using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Server.Tests;

/// <summary>
/// Plate count, part count, material and thickness describe the uploaded archive: the server
/// reads them from its nest.json, whatever the client claims, and refuses files it cannot read.
/// </summary>
public sealed class NestArchiveFieldsTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Upload_TakesArchiveFieldsFromTheFileNotTheClient()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var archive = NestApiTests.Archive(TwoPlateNest());

        using var response = await client.PostAsync("/api/nests", NestRevisionTests.Body(Claimed(), archive));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = (await response.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        Assert.Equal(2, stored.PlateCount);
        Assert.Equal(3, stored.PartCount);
        Assert.Equal("Synthetic alloy", stored.Material);
        Assert.Equal(0.25, stored.Thickness);
        // Same values the desktop computed by reading the archive back in full.
        using var stream = new MemoryStream(archive, writable: false);
        var expected = NestRecordFactory.FromNest(new NestReader(stream).Read(), stored.Id, archive.LongLength);
        Assert.Equal(
            (expected.PlateCount, expected.PartCount, expected.Material, expected.Thickness),
            (stored.PlateCount, stored.PartCount, stored.Material, stored.Thickness));
        // Client-owned fields are still the client's.
        Assert.Equal("Claimed name", stored.Name);
    }

    [Fact]
    public async Task UpdateFile_TakesArchiveFieldsFromTheNewFile()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var id = await Create(client, NestApiTests.Archive(TwoPlateNest()));
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");

        using var response = await client.PutAsync($"/api/nests/{id}/file",
            NestRevisionTests.Body(Claimed(), NestApiTests.Archive(NestApiTests.SyntheticNest())));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = (await response.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        Assert.Equal((1, 1), (stored.PlateCount, stored.PartCount));
    }

    [Fact]
    public async Task MetadataOnlySave_KeepsArchiveCountsAndAcceptsEditedMaterialAndThickness()
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var id = await Create(client, NestApiTests.Archive(TwoPlateNest()));
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        var edit = Claimed();
        edit.Material = "Edited alloy";
        edit.Thickness = 0.5;

        using var response = await client.PutAsJsonAsync($"/api/nests/{id}/metadata", edit, Options);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = (await response.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        // The archive is unchanged, so its counts are too; material and thickness are
        // editable record fields the desktop applies when it opens the nest.
        Assert.Equal((2, 3), (stored.PlateCount, stored.PartCount));
        Assert.Equal(("Edited alloy", 0.5), (stored.Material, stored.Thickness));
        Assert.Equal(2, stored.Revision);
    }

    [Theory]
    [InlineData("not a zip")]
    [InlineData("no nest.json")]
    [InlineData("nest.json not JSON")]
    [InlineData("nest.json null")]
    public async Task Upload_FileThatIsNotANest_Returns400AndStoresNothing(string row)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/nests", NestRevisionTests.Body(Claimed(), NotANest(row)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Empty(reopened.List());
    }

    [Theory]
    [InlineData("not a zip")]
    [InlineData("no nest.json")]
    public async Task UpdateFile_FileThatIsNotANest_Returns400AndLeavesTheRecord(string row)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();
        var original = TestNests.Archive(1);
        var id = await Create(client, original);
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");

        using var response = await client.PutAsync($"/api/nests/{id}/file", NestRevisionTests.Body(Claimed(), NotANest(row)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/nests/{id}/file"));
        using var reopened = new NestDatabase(factory.DatabasePath, pooling: false);
        Assert.Equal(1, reopened.Get(id)!.Revision);
    }

    // Hand-written nest.json for the reader fallbacks NestWriter never produces.
    [Theory]
    // Thickness that is not positive falls back to the plate defaults'.
    [InlineData("""{"thickness":0,"plateDefaults":{"thickness":0.375}}""", "", 0.375, 0, 0)]
    // An explicit null material falls back to the plate defaults'; an absent one is empty.
    [InlineData("""{"material":null,"plateDefaults":{"material":{"name":"Default"}}}""", "Default", 0, 0, 0)]
    [InlineData("""{"plateDefaults":{"material":{"name":"Default"}}}""", "", 0, 0, 0)]
    // A part whose drawing is missing is dropped, as NestReader drops it.
    [InlineData("""{"drawings":[{"id":1}],"plates":[{"id":1,"parts":[{"drawingId":1},{"drawingId":7}]}]}""", "", 0, 1, 1)]
    // A trailing plate with nothing on it is the new-plate slot; one with only cut-offs counts.
    [InlineData("""{"drawings":[{"id":1}],"plates":[{"id":1,"parts":[{"drawingId":1}]},{"id":2}]}""", "", 0, 1, 1)]
    [InlineData("""{"drawings":[{"id":1}],"plates":[{"id":1,"parts":[{"drawingId":1}]},{"id":2,"cutOffs":[{}]}]}""", "", 0, 2, 1)]
    // Plates are ordered by id, as NestReader orders them, before finding the last one.
    [InlineData("""{"drawings":[{"id":1}],"plates":[{"id":2},{"id":1,"parts":[{"drawingId":1}]}]}""", "", 0, 1, 1)]
    public async Task Upload_FollowsNestReaderFallbacks(string nestJson, string material, double thickness,
        int plateCount, int partCount)
    {
        using var factory = new ServerFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/nests",
            NestRevisionTests.Body(Claimed(), TestNests.Zip("nest.json", nestJson)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = (await response.Content.ReadFromJsonAsync<NestRecord>(Options))!;
        Assert.Equal((material, thickness, plateCount, partCount),
            (stored.Material, stored.Thickness, stored.PlateCount, stored.PartCount));
    }

    /// <summary>A client record whose archive-derived fields are all wrong.</summary>
    private static NestRecord Claimed() => new()
    {
        Name = "Claimed name",
        Material = "Claimed alloy",
        Thickness = 9,
        PlateCount = 99,
        PartCount = 99,
    };

    private static Nest TwoPlateNest()
    {
        var nest = NestApiTests.SyntheticNest();
        var drawing = nest.Drawings.Single();
        var plate = new Plate(new Size(20, 30)) { Quantity = 1 };
        plate.Parts.Add(new Part(drawing, new Vector(2, 3)));
        plate.Parts.Add(new Part(drawing, new Vector(10, 3)));
        nest.Plates.Add(plate);
        return nest;
    }

    private static byte[] NotANest(string row) => row switch
    {
        "not a zip" => Encoding.UTF8.GetBytes("nest.json"),
        "no nest.json" => TestNests.Zip("readme.txt", "not a nest"),
        "nest.json not JSON" => TestNests.Zip("nest.json", "{not json"),
        "nest.json null" => TestNests.Zip("nest.json", "null"),
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
    };

    private static async Task<Guid> Create(HttpClient client, byte[] archive)
    {
        using var created = await client.PostAsync("/api/nests", NestRevisionTests.Body(Claimed(), archive));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<NestRecord>(Options))!.Id;
    }
}
