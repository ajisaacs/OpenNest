using System.Text.Json.Nodes;
using OpenNest.Geometry;
using OpenNest.Shapes;
using static OpenNest.IO.Tests.NestArchiveJson;

namespace OpenNest.IO.Tests;

/// <summary>
/// Each saved plate carries a derived <c>expectedRemnant</c> computed from its layout at save;
/// readers never load it, and the nest's minimum remnant side round-trips.
/// </summary>
public class ExpectedRemnantPersistenceTests
{
    // A 48 x 96 sheet with one 10 x 10 part at the origin: an 86 x 48 offcut to its right.
    private static Nest NestWithPart()
    {
        var nest = new Nest("remnant") { Material = new Material("Mild Steel", "A36"), Thickness = 0.25 };
        var drawing = new RectangleShape { Name = "SQ", Length = 10, Width = 10 }.GetDrawing();
        nest.Drawings.Add(drawing);
        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(drawing, new Vector(0, 0)));
        nest.Plates.Add(plate);
        return nest;
    }

    private static byte[] Write(Nest nest)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static JsonNode Remnant(byte[] archive) => NestJson(archive)["plates"]![0]!["expectedRemnant"];

    private static (double, double, string) Shape(JsonNode remnant) =>
        (remnant!["width"]!.GetValue<double>(), remnant["length"]!.GetValue<double>(), remnant["source"]!.GetValue<string>());

    [Fact]
    public void Offcut_IsWrittenWithWidthTheShortSide()
    {
        Assert.Equal((48.0, 86.0, "offcut"), Shape(Remnant(Write(NestWithPart()))));
    }

    [Fact]
    public void CutOffDrop_IsWrittenAsCutoff()
    {
        var nest = NestWithPart();
        nest.Plates[0].CutOffs.Add(new CutOff(new Vector(60, 0), CutOffAxis.Vertical));
        nest.Plates[0].RegenerateCutOffs(new CutOffSettings());

        Assert.Equal((36.0, 48.0, "cutoff"), Shape(Remnant(Write(nest))));
    }

    [Fact]
    public void NoRemnant_WritesNoKey()
    {
        var nest = NestWithPart();
        nest.MinimumRemnantDimension = 50;

        Assert.False(NestJson(Write(nest))["plates"]![0]!.AsObject().ContainsKey("expectedRemnant"));
    }

    [Fact]
    public void MinimumRemnantDimension_RoundTrips_AndOldFilesLoadTheDefault()
    {
        var nest = NestWithPart();
        nest.MinimumRemnantDimension = 6;
        var archive = Write(nest);

        Assert.Equal(6, RoundTrip(archive).Nest.MinimumRemnantDimension);

        var json = NestJson(archive);
        Assert.True(json.Remove("minimumRemnantDimension"));
        Assert.Equal(Nest.DefaultMinimumRemnantDimension,
            RoundTrip(ReplaceNestJson(archive, json)).Nest.MinimumRemnantDimension);
    }

    [Theory]
    [InlineData("{\"width\": 1, \"length\": 2, \"source\": \"cutoff\"}")]
    [InlineData("\"junk\"")]
    [InlineData("[1, 2]")]
    public void StoredValue_IsIgnoredOnRead_AndRecomputedOnSave(string stored)
    {
        var archive = Write(NestWithPart());
        var json = NestJson(archive);
        json["plates"]![0]!["expectedRemnant"] = JsonNode.Parse(stored);

        var (reopened, _) = RoundTrip(ReplaceNestJson(archive, json));

        Assert.Single(reopened.Plates);
        Assert.Equal((48.0, 86.0, "offcut"), Shape(Remnant(Write(reopened))));
    }

    [Fact]
    public void PartMove_IsReflectedOnTheNextSave()
    {
        var (reopened, _) = RoundTrip(Write(NestWithPart()));
        reopened.Plates[0].Parts[0].Location = new Vector(40, 0);

        // Strips: 40 x 48 left of the part, 46 x 48 right of it, 96 x 38 above it.
        Assert.Equal((38.0, 96.0, "offcut"), Shape(Remnant(Write(reopened))));
    }
}
