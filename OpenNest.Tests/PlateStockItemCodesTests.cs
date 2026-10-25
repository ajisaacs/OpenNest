using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests;

/// <summary>
/// <see cref="Plate.StockItemCodes"/> name the inventory item of each physical sheet, and Core
/// drops them as soon as they stop describing the sheet.
/// </summary>
public class PlateStockItemCodesTests
{
    private static Plate Coded(int quantity, params string[] codes)
    {
        var plate = new Plate(30, 52.5) { Quantity = quantity };
        plate.StockItemCodes = codes;
        return plate;
    }

    [Fact]
    public void NewPlate_HasNoCodes()
    {
        Assert.Empty(new Plate().StockItemCodes);
        Assert.Empty(new Nest().CreatePlate().StockItemCodes);
    }

    [Fact]
    public void Resize_ClearsCodes_ButTurningTheSheetKeepsThem()
    {
        var plate = Coded(1, "DROP-1182");

        plate.Size = new Size(52.5, 30);
        Assert.Equal(["DROP-1182"], plate.StockItemCodes);
        plate.Rotate90(RotationType.CW);
        Assert.Equal(["DROP-1182"], plate.StockItemCodes);

        plate.Size = new Size(30, 60);
        Assert.Empty(plate.StockItemCodes);
    }

    [Fact]
    public void LowerQuantity_TruncatesCodes_AndHigherQuantityLeavesTheExtraSheetsUncoded()
    {
        var plate = Coded(3, "SHEET-456", "SHEET-456", "SHEET-789");

        plate.Quantity = 2;
        Assert.Equal(["SHEET-456", "SHEET-456"], plate.StockItemCodes);

        plate.Quantity = 5;
        Assert.Equal(["SHEET-456", "SHEET-456"], plate.StockItemCodes);

        plate.Quantity = 0;
        Assert.Empty(plate.StockItemCodes);
    }

    [Fact]
    public void Codes_BeyondQuantityAreDropped_AndBlankCodesAreRefused()
    {
        Assert.Equal(["A"], Coded(1, "A", "B").StockItemCodes);
        Assert.Throws<ArgumentException>(() => Coded(2, "A", " "));
        Assert.Throws<ArgumentNullException>(() => new Plate().StockItemCodes = null);
    }

    [Fact]
    public void NestMaterialOrThicknessChange_ClearsEveryPlatesCodes()
    {
        var nest = new Nest { Material = new Material("Mild Steel", "A36"), Thickness = 0.25 };
        var first = Coded(1, "DROP-1");
        var second = Coded(2, "SHEET-2", "SHEET-2");
        nest.Plates.Add(first);
        nest.Plates.Add(second);

        // Same material written differently, and the same thickness, are the same stock.
        nest.Material = new Material(" mild  steel ", "a36", 0.284);
        nest.Thickness = 0.25;
        Assert.Equal(["DROP-1"], first.StockItemCodes);
        Assert.Equal(["SHEET-2", "SHEET-2"], second.StockItemCodes);

        nest.Material = new Material("Mild Steel", "A572");
        Assert.Empty(first.StockItemCodes);
        Assert.Empty(second.StockItemCodes);

        first.StockItemCodes = ["DROP-1"];
        nest.Thickness = 0.1875;
        Assert.Empty(first.StockItemCodes);

        first.StockItemCodes = ["DROP-1"];
        nest.Material = new Material("Stainless");
        Assert.Empty(first.StockItemCodes);
    }

    [Fact]
    public void PlateFromAnExistingPlate_CopiesNoCodes()
    {
        var nest = new Nest();
        var coded = Coded(1, "DROP-1182");
        nest.Plates.Add(coded);

        nest.PlateDefaults.SetFromExisting(coded);
        var copy = nest.CreatePlate();

        Assert.Equal(coded.Size, copy.Size);
        Assert.Empty(copy.StockItemCodes);
    }

    [Fact]
    public void ClearAll_DropsTheRemainingPlatesCodes()
    {
        var nest = new Nest();
        var drawing = new RectangleShape { Name = "SYN", Length = 4, Width = 2 }.GetDrawing();
        nest.Drawings.Add(drawing);
        var plate = Coded(2, "SHEET-1", "SHEET-1");
        plate.Parts.Add(new Part(drawing));
        nest.Plates.Add(plate);
        using var manager = new PlateManager(nest);

        manager.ClearAll();

        Assert.Empty(nest.Plates[0].StockItemCodes);
    }

    [Fact]
    public void Codes_AreNotQuantityAccounting()
    {
        var nest = new Nest();
        var drawing = new RectangleShape { Name = "SYN", Length = 4, Width = 2 }.GetDrawing();
        nest.Drawings.Add(drawing);
        var plate = new Plate(48, 96) { Quantity = 2 };
        nest.Plates.Add(plate);
        plate.Parts.Add(new Part(drawing));

        plate.StockItemCodes = ["SHEET-1", "SHEET-1"];
        plate.StockItemCodes = [];

        Assert.Equal(2, drawing.Quantity.Nested);
    }
}
