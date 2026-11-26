using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.IO;

public class NestFormedSerializationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FormedOverride_SurvivesNestRoundtrip(bool formed)
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        drawing.FormedOverride = formed;

        var (loaded, json) = RoundTrip(drawing);

        Assert.Equal(formed, loaded.FormedOverride);
        Assert.Equal(formed, loaded.IsFormed);
        Assert.Equal(formed, (bool)json!["formed"]!);
    }

    [Fact]
    public void AutomaticFormed_IsOmittedAndStillFollowsBendLines()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        drawing.Bends.Add(new Bend
        {
            StartPoint = new Vector(0, 5),
            EndPoint = new Vector(10, 5),
            Direction = BendDirection.Up,
        });

        var (loaded, json) = RoundTrip(drawing);

        Assert.Null(loaded.FormedOverride);
        Assert.True(loaded.IsFormed);
        Assert.Null(json!["formed"]);
    }

    [Fact]
    public void IsFormed_OverrideWinsOverBendLines()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        Assert.False(drawing.IsFormed);

        drawing.Bends.Add(new Bend { StartPoint = new Vector(0, 5), EndPoint = new Vector(10, 5) });
        Assert.True(drawing.IsFormed);

        drawing.FormedOverride = false;
        Assert.False(drawing.IsFormed);

        drawing.Bends.Clear();
        drawing.FormedOverride = true;
        Assert.True(drawing.IsFormed);
    }

    [Fact]
    public void SetFormed_KeepsAnOverrideOnlyWhenItDisagreesWithBendLines()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        drawing.SetFormed(false);
        Assert.Null(drawing.FormedOverride);
        drawing.SetFormed(true);
        Assert.True(drawing.FormedOverride);

        drawing.Bends.Add(new Bend { StartPoint = new Vector(0, 5), EndPoint = new Vector(10, 5) });
        drawing.SetFormed(true);
        Assert.Null(drawing.FormedOverride);
        drawing.SetFormed(false);
        Assert.False(drawing.FormedOverride);
        Assert.False(drawing.IsFormed);
    }

    private static (Drawing Drawing, JsonNode Json) RoundTrip(Drawing drawing)
    {
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var plate = new Plate(60, 120);
        plate.Parts.Add(new Part(drawing, new Vector(0, 0)));
        nest.Plates.Add(plate);

        using var ms = new MemoryStream();
        new NestWriter(nest).Write(ms);

        ms.Position = 0;
        JsonNode json;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        using (var reader = new StreamReader(zip.GetEntry("nest.json")!.Open()))
            json = JsonNode.Parse(reader.ReadToEnd())!["drawings"]![0]!;

        ms.Position = 0;
        var loaded = new NestReader(ms).Read();
        return (loaded.Drawings.First(), json);
    }
}
