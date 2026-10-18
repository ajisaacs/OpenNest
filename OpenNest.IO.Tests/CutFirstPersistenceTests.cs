using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenNest.IO.Tests;

public class CutFirstPersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CutFirst_RoundTrips_AndIsOmittedWhenFalse(bool cutFirst)
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.CutFirst = cutFirst;
        var nest = new Nest(drawing.Name);
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();

        Assert.True(new NestWriter(nest).Write(stream));
        var bytes = stream.ToArray();
        var reader = new NestReader(new MemoryStream(bytes));
        var copy = Assert.Single(reader.Read().Drawings);

        Assert.Empty(reader.Warnings);
        Assert.Equal(cutFirst, copy.CutFirst);
        var json = DrawingJson(bytes);
        Assert.Equal(cutFirst, json.ContainsKey("cutFirst"));
    }

    private static JsonObject DrawingJson(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        var nest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        return nest["drawings"]!.AsArray().Single()!.AsObject();
    }
}
