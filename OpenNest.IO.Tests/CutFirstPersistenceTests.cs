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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CustomColor_RoundTrips_AndIsOmittedWhenFalse(bool custom)
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.Color = System.Drawing.Color.FromArgb(255, 10, 20, 30);
        drawing.HasCustomColor = custom;
        var nest = new Nest(drawing.Name);
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();

        Assert.True(new NestWriter(nest).Write(stream));
        var bytes = stream.ToArray();
        var copy = Assert.Single(new NestReader(new MemoryStream(bytes)).Read().Drawings);

        Assert.Equal(custom, copy.HasCustomColor);
        Assert.Equal(drawing.Color.ToArgb(), copy.Color.ToArgb());
        Assert.Equal(custom, DrawingJson(bytes).ContainsKey("customColor"));
    }

    private static JsonObject DrawingJson(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        var nest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        return nest["drawings"]!.AsArray().Single()!.AsObject();
    }
}
