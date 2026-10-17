using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenNest.IO.Tests;

public class DrawingLibraryLinkPersistenceTests
{
    private static readonly Guid LibraryId = Guid.Parse("6d0f1c52-9a43-4f7e-8b8e-2f6f3c1d4a10");

    private static DrawingLibraryLink Link() => new(
        "http://library.test:8090", LibraryId, 3, CutType.Waterjet, 0.1875,
        "Synthetic Steel", "Grade A", Units.Inches, "Deburr both sides", new string('a', 64));

    [Fact]
    public void Link_RoundTripsEveryField()
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.Library = Link();

        var (restored, warnings) = RoundTrip(drawing);

        Assert.Empty(warnings);
        var link = Assert.Single(restored.Drawings).Library;
        Assert.NotNull(link);
        Assert.Equal(
            ("http://library.test:8090", LibraryId, 3, CutType.Waterjet, 0.1875, "Synthetic Steel", "Grade A",
                Units.Inches, "Deburr both sides", new string('a', 64)),
            (link.ServerKey, link.Id, link.Revision, link.CutType, link.Thickness, link.MaterialName,
                link.MaterialGrade, link.Units, link.Notes, link.PackageSha256));
    }

    [Fact]
    public void NoLink_IsOmittedFromTheFile()
    {
        var drawing = DrawingPackageFixture.Build();

        var json = NestJson(Write(drawing));
        var (restored, warnings) = RoundTrip(drawing);

        Assert.False(json["drawings"]![0]!.AsObject().ContainsKey("library"));
        Assert.Empty(warnings);
        Assert.Null(Assert.Single(restored.Drawings).Library);
    }

    public static TheoryData<string> MalformedLinks => new()
    {
        "\"not an object\"",
        "[]",
        "{\"serverKey\":\"http://library.test\",\"id\":\"not-a-guid\",\"revision\":1,\"cutType\":\"Laser\",\"units\":\"Inches\"}",
        "{\"serverKey\":\"http://library.test\",\"id\":\"6d0f1c52-9a43-4f7e-8b8e-2f6f3c1d4a10\",\"revision\":\"one\",\"cutType\":\"Laser\",\"units\":\"Inches\"}",
        "{\"serverKey\":\"http://library.test\",\"id\":\"6d0f1c52-9a43-4f7e-8b8e-2f6f3c1d4a10\",\"revision\":0,\"cutType\":\"Laser\",\"units\":\"Inches\"}",
        "{\"serverKey\":\"\",\"id\":\"6d0f1c52-9a43-4f7e-8b8e-2f6f3c1d4a10\",\"revision\":1,\"cutType\":\"Laser\",\"units\":\"Inches\"}",
        "{\"serverKey\":\"http://library.test\",\"id\":\"00000000-0000-0000-0000-000000000000\",\"revision\":1,\"cutType\":\"Laser\",\"units\":\"Inches\"}",
        "{\"serverKey\":\"http://library.test\",\"id\":\"6d0f1c52-9a43-4f7e-8b8e-2f6f3c1d4a10\",\"revision\":1,\"cutType\":\"1\",\"units\":\"Inches\"}",
        "{\"serverKey\":\"http://library.test\",\"id\":\"6d0f1c52-9a43-4f7e-8b8e-2f6f3c1d4a10\",\"revision\":1,\"cutType\":\"Laser,Plasma\",\"units\":\"Inches\"}",
        "{\"serverKey\":\"http://library.test\",\"id\":\"6d0f1c52-9a43-4f7e-8b8e-2f6f3c1d4a10\",\"revision\":1,\"cutType\":\"Oxyfuel\",\"units\":\"Inches\"}",
        "{\"serverKey\":\"http://library.test\",\"id\":\"6d0f1c52-9a43-4f7e-8b8e-2f6f3c1d4a10\",\"revision\":1,\"cutType\":\"Laser\",\"units\":\"Furlongs\"}",
        "{\"serverKey\":\"http://library.test\",\"id\":\"6d0f1c52-9a43-4f7e-8b8e-2f6f3c1d4a10\",\"revision\":1,\"cutType\":\"Laser\",\"units\":\"Inches\",\"thickness\":-0.25}",
    };

    [Theory]
    [MemberData(nameof(MalformedLinks))]
    public void MalformedLink_LoadsTheDrawingWithoutItAndWarns(string linkJson)
    {
        var drawing = DrawingPackageFixture.Build();
        var archive = ReplaceLibrary(Write(drawing), JsonNode.Parse(linkJson));

        var reader = new NestReader(new MemoryStream(archive));
        var restored = reader.Read();

        var copy = Assert.Single(restored.Drawings);
        Assert.Null(copy.Library);
        Assert.Equal(NestWriter.GetProgramText(drawing.Program), NestWriter.GetProgramText(copy.Program));
        var warning = Assert.Single(reader.Warnings);
        Assert.Contains("'Synthetic bracket'", warning);
        Assert.Contains("library link", warning);
    }

    [Fact]
    public void NullLink_ReadsAsNoLinkWithoutWarning()
    {
        var archive = ReplaceLibrary(Write(DrawingPackageFixture.Build()), null);

        var reader = new NestReader(new MemoryStream(archive));
        var restored = reader.Read();

        Assert.Null(Assert.Single(restored.Drawings).Library);
        Assert.Empty(reader.Warnings);
    }

    [Fact]
    public void Package_LeavesTheLinkOutAndKeepsItsBytes()
    {
        var drawing = DrawingPackageFixture.Build();
        var plainPackage = DrawingPackage.Write(drawing, Units.Inches);
        var link = Link();
        drawing.Library = link;

        var linkedPackage = DrawingPackage.Write(drawing, Units.Inches);

        Assert.Equal(SHA256.HashData(plainPackage), SHA256.HashData(linkedPackage));
        Assert.Null(DrawingPackage.Read(linkedPackage).Drawing.Library);
        Assert.Same(link, drawing.Library);
    }

    private static byte[] Write(Drawing drawing)
    {
        var nest = new Nest(drawing.Name) { Units = Units.Inches };
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static (Nest Nest, IReadOnlyList<string> Warnings) RoundTrip(Drawing drawing)
    {
        var reader = new NestReader(new MemoryStream(Write(drawing)));
        return (reader.Read(), reader.Warnings);
    }

    private static JsonObject NestJson(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }

    private static byte[] ReplaceLibrary(byte[] archive, JsonNode? library)
    {
        var json = NestJson(archive);
        json["drawings"]![0]!["library"] = library;

        using var output = new MemoryStream();
        output.Write(archive);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry("nest.json")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("nest.json").Open(), new UTF8Encoding(false));
            writer.Write(json.ToJsonString());
        }

        return output.ToArray();
    }
}
