using System.Text.Json.Nodes;
using OpenNest.Geometry;
using OpenNest.Shapes;
using static OpenNest.IO.Tests.NestArchiveJson;

namespace OpenNest.IO.Tests;

/// <summary>
/// A plate's stock item codes survive the .nest round trip as an optional
/// <c>stockItemCodes</c> array; files without it load with no codes.
/// </summary>
public class StockItemCodesPersistenceTests
{
    private static Nest NestWithPlate(int quantity, params string[] codes)
    {
        var nest = new Nest("coded") { Material = new Material("Mild Steel", "A36"), Thickness = 0.25 };
        var drawing = new RectangleShape { Name = "SYN", Length = 4, Width = 2 }.GetDrawing();
        nest.Drawings.Add(drawing);
        var plate = new Plate(30, 52.5) { Quantity = quantity };
        plate.Parts.Add(new Part(drawing, new Vector(1, 1)));
        nest.Plates.Add(plate);
        plate.StockItemCodes = codes;
        return nest;
    }

    private static byte[] Write(Nest nest)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    [Fact]
    public void CodesRoundTripInSheetOrder()
    {
        var (reopened, _) = RoundTrip(Write(NestWithPlate(3, "SHEET-456", "SHEET-456")));

        var plate = Assert.Single(reopened.Plates);
        Assert.Equal(3, plate.Quantity);
        Assert.Equal(["SHEET-456", "SHEET-456"], plate.StockItemCodes);
        Assert.Equal("Mild Steel", reopened.Material.Name);
    }

    [Fact]
    public void UncodedPlate_WritesNoKey()
    {
        var json = NestJson(Write(NestWithPlate(1)));

        Assert.False(json["plates"]![0]!.AsObject().ContainsKey("stockItemCodes"));
    }

    [Fact]
    public void OldFileWithoutTheKey_LoadsWithNoCodes()
    {
        var archive = Write(NestWithPlate(1, "DROP-1182"));
        var json = NestJson(archive);
        Assert.True(json["plates"]![0]!.AsObject().Remove("stockItemCodes"));

        var (reopened, _) = RoundTrip(ReplaceNestJson(archive, json));

        Assert.Empty(Assert.Single(reopened.Plates).StockItemCodes);
    }

    [Theory]
    [InlineData("\"DROP-1\"", new string[0])]
    [InlineData("[\"DROP-1\", 7, \"DROP-2\"]", new[] { "DROP-1", "DROP-2" })]
    [InlineData("[\"DROP-1\", \"\", \"DROP-2\"]", new[] { "DROP-1" })]
    [InlineData("[\"A\", \"B\", \"C\", \"D\"]", new[] { "A", "B", "C" })]
    public void MalformedCodes_ReadLeniently(string value, string[] expected)
    {
        var archive = Write(NestWithPlate(3, "X"));
        var json = NestJson(archive);
        json["plates"]![0]!["stockItemCodes"] = JsonNode.Parse(value);

        var (reopened, _) = RoundTrip(ReplaceNestJson(archive, json));

        Assert.Equal(expected, Assert.Single(reopened.Plates).StockItemCodes);
    }
}
