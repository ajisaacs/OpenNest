using System.Text.Json.Nodes;
using OpenNest.Geometry;
using static OpenNest.IO.Tests.NestArchiveJson;

namespace OpenNest.IO.Tests;

/// <summary>
/// A plate's <see cref="SheetOutline"/> survives the .nest round trip under an optional
/// <c>outline</c> key; files without it load rectangles unchanged, an empty outlined plate is
/// kept, and a malformed value loads as a rectangle with a reader warning.
/// </summary>
public class SheetOutlinePersistenceTests
{
    /// <summary>A 96 x 48 sheet missing its upper-right 48 x 24 corner, with a burned 4 x 4 patch.</summary>
    private static SheetOutline Ell() => new(
        new[] { new Vector(0, 0), new Vector(96, 0), new Vector(96, 24), new Vector(48, 24), new Vector(48, 48), new Vector(0, 48) },
        new[] { new[] { new Vector(10, 10), new Vector(14, 10), new Vector(14, 14), new Vector(10, 14) } });

    private static byte[] Write(Nest nest)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static Nest OutlinedNest()
    {
        var nest = new Nest("outlined");
        nest.Plates.Add(new Plate { Outline = Ell(), Quadrant = 3, EdgeSpacing = new Spacing(1, 2, 3, 4) });
        return nest;
    }

    [Fact]
    public void OutlineRoundTripsWithObstaclesAndKeepsAnEmptyPlate()
    {
        var (reopened, warnings) = RoundTrip(Write(OutlinedNest()));

        Assert.Empty(warnings);
        var plate = Assert.Single(reopened.Plates);
        Assert.Equal(Ell(), plate.Outline);
        Assert.Equal(48, plate.Size.Width);
        Assert.Equal(96, plate.Size.Length);
        Assert.Equal(3, plate.Quadrant);
        Assert.Equal(96 * 48 - 48 * 24 - 16, plate.Area(), 6);
    }

    [Fact]
    public void FileShapeIsPointsAndObstaclesInPlateCoordinates()
    {
        var plates = NestJson(Write(OutlinedNest()))["plates"]!.AsArray();
        var outline = Assert.Single(plates)!["outline"]!.AsObject();

        var points = outline["points"]!.AsArray();
        Assert.Equal(6, points.Count);
        Assert.Equal(0, (double)points[0]!["x"]!);
        Assert.Equal(0, (double)points[0]!["y"]!);
        Assert.Equal(96, (double)points[1]!["x"]!);
        var obstacle = Assert.Single(outline["obstacles"]!.AsArray())!.AsArray();
        Assert.Equal(4, obstacle.Count);
        Assert.Equal(10, (double)obstacle[0]!["x"]!);
    }

    [Fact]
    public void RectangularPlatesWriteNoOutlineKeyAndLoadWithout()
    {
        var nest = new Nest("plain");
        nest.Plates.Add(new Plate(48, 96));
        nest.Plates[0].CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical));
        var archive = Write(nest);

        var json = NestJson(archive);
        Assert.Null(Assert.Single(json["plates"]!.AsArray())!["outline"]);
        var (reopened, warnings) = RoundTrip(archive);
        Assert.Empty(warnings);
        Assert.Null(Assert.Single(reopened.Plates).Outline);
    }

    [Theory]
    [InlineData("\"not an object\"", "not an object")]
    [InlineData("{}", "'points' is missing")]
    [InlineData("{\"points\": 5}", "not an array")]
    [InlineData("{\"points\": [{\"x\": 0, \"y\": 0}, {\"x\": \"ten\", \"y\": 0}, {\"x\": 0, \"y\": 10}]}", "vertex 2")]
    [InlineData("{\"points\": [{\"x\": 0, \"y\": 0}, {\"x\": 10, \"y\": 0}]}", "three distinct vertices")]
    [InlineData("{\"points\": [{\"x\": 0, \"y\": 0}, {\"x\": 10, \"y\": 10}, {\"x\": 10, \"y\": 0}, {\"x\": 0, \"y\": 10}]}", "no area")]
    [InlineData("{\"points\": [{\"x\": 0, \"y\": 0}, {\"x\": 10, \"y\": 0}, {\"x\": 10, \"y\": 10}, {\"x\": 2, \"y\": -2}]}", "simple polygon")]
    [InlineData("{\"points\": [{\"x\": 0, \"y\": 0}, {\"x\": 10, \"y\": 0}, {\"x\": 10, \"y\": 10}], \"obstacles\": 3}", "'obstacles' is not an array")]
    public void MalformedOutlineLoadsARectangleWithAWarning(string value, string problem)
    {
        var archive = Write(OutlinedNest());
        var json = NestJson(archive);
        var plate = Assert.Single(json["plates"]!.AsArray())!.AsObject();
        plate["outline"] = JsonNode.Parse(value);

        var (reopened, warnings) = RoundTrip(ReplaceNestJson(archive, json));

        var loaded = Assert.Single(reopened.Plates);
        Assert.Null(loaded.Outline);
        Assert.Equal(48, loaded.Size.Width);
        Assert.Equal(96, loaded.Size.Length);
        var warning = Assert.Single(warnings);
        Assert.Contains("Plate 1", warning);
        Assert.Contains(problem, warning);
    }

    [Fact]
    public void SnapshotRestoresTheOutline()
    {
        var nest = OutlinedNest();
        var snapshot = NestSnapshot.Capture(nest);
        nest.Plates[0].Outline = null;
        nest.Plates[0].Size = new Size(60, 120);

        var restored = snapshot.Restore(nest);

        Assert.Equal(Ell(), Assert.Single(restored.Plates).Outline);
        Assert.Equal(96, restored.Plates[0].Size.Length);
    }
}
