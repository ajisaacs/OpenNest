using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bom;

namespace OpenNest.Tests.Bom;

/// <summary>
/// BOM items to rows to an operator edit to groups to the created nest,
/// the same path the import dialog takes.
/// </summary>
public sealed class BomImportFlowTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "bom-flow-" + Guid.NewGuid().ToString("N")
    );

    public BomImportFlowTests()
    {
        Directory.CreateDirectory(folder);
        WriteSquare("PT01");
        WriteSquare("PT02");
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void EditedQuantity_ReachesTheGroupTotalAndTheNestDrawing()
    {
        var rows = BomImportRows.Build(new List<BomItem> { Item("PT01", 2), Item("PT02", null) }, folder);

        Assert.True(rows[0].TrySetQuantity("5"));
        Assert.False(rows[1].TrySetQuantity("0"));

        var group = Assert.Single(BomImportGroups.Build(rows));
        Assert.Equal(6, group.TotalQty);

        var nest = Assert.IsType<Nest>(BomNestBuilder.Build(group, Plate(), "Job", null).Nest);
        Assert.Equal(
            new[] { ("PT01", 5), ("PT02", 1) },
            nest.Drawings.OrderBy(d => d.Name).Select(d => (d.Name, d.Quantity.Required))
        );
    }

    [Fact]
    public void BomQuantityBelowOne_KeepsThePartOutUntilTheOperatorFixesIt()
    {
        var rows = BomImportRows.Build(new List<BomItem> { Item("PT01", 0), Item("PT02", 3) }, folder);

        Assert.Equal(new[] { "PT02" }, Assert.Single(BomImportGroups.Build(rows)).Parts.Select(p => p.FileName));

        Assert.True(rows[0].TrySetQuantity("4"));

        var group = Assert.Single(BomImportGroups.Build(rows));
        Assert.Equal(7, group.TotalQty);
    }

    private static BomItem Item(string fileName, int? qty) =>
        new()
        {
            FileName = fileName,
            Qty = qty,
            Material = "Stainless",
            Thickness = 0.25,
        };

    private static BomGroupPlateSettings Plate() => new() { PlateWidth = 60, PlateLength = 120 };

    private void WriteSquare(string name)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(2, 0)));
        shape.Entities.Add(new Line(new Vector(2, 0), new Vector(2, 2)));
        shape.Entities.Add(new Line(new Vector(2, 2), new Vector(0, 2)));
        shape.Entities.Add(new Line(new Vector(0, 2), new Vector(0, 0)));
        Dxf.ExportProgram(ConvertGeometry.ToProgram(shape), Path.Combine(folder, name + ".dxf"));
    }
}
