using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bom;

namespace OpenNest.Tests.Bom;

public sealed class BomNestBuilderTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "bom-nests-" + Guid.NewGuid().ToString("N")
    );

    public BomNestBuilderTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Build_AppliesSavedDefaultsThenGroupSettings()
    {
        var group = Group(Row("PT01", qty: 5), Row("PT02", qty: null));
        var plate = Plate();

        var result = BomNestBuilder.Build(
            group,
            plate,
            "Job 42",
            nest =>
            {
                nest.Units = Units.Millimeters;
                nest.PlateDefaults.Quadrant = 3;
                nest.PlateDefaults.Size = new Size(1, 1);
                nest.PlateDefaults.PartSpacing = 9;
            }
        );

        Assert.Empty(result.Errors);
        var nest = Assert.IsType<Nest>(result.Nest);
        Assert.Equal("Job 42 - 0.25 Stainless", nest.Name);
        Assert.Equal(Units.Millimeters, nest.Units);
        Assert.Equal(3, nest.PlateDefaults.Quadrant);
        Assert.Equal(60, nest.PlateDefaults.Size.Width);
        Assert.Equal(120, nest.PlateDefaults.Size.Length);
        Assert.Equal(0.2, nest.PlateDefaults.PartSpacing);
        Assert.Equal(new[] { 0.1, 0.2, 0.3, 0.4 }, Edges(nest.PlateDefaults.EdgeSpacing));
        Assert.Equal(0.25, nest.Thickness);
        Assert.Equal("Stainless", nest.Material.Name);
        Assert.Single(nest.Plates);

        var drawings = nest.Drawings.OrderBy(d => d.Name).ToList();
        Assert.Equal(new[] { "PT01", "PT02" }, drawings.Select(d => d.Name));
        Assert.Equal(new[] { 5, 1 }, drawings.Select(d => d.Quantity.Required));
        Assert.All(drawings, d => Assert.Equal("Stainless", d.Material.Name));
    }

    [Fact]
    public void Build_ReportsMissingDrawingAndKeepsTheOthers()
    {
        var missing = Row("PT09");
        File.Delete(missing.DxfPath);

        var result = BomNestBuilder.Build(Group(Row("PT01"), missing), Plate(), "Job", null);

        Assert.Equal("PT09: DXF file not found", Assert.Single(result.Errors));
        var nest = Assert.IsType<Nest>(result.Nest);
        Assert.Equal("PT01", Assert.Single(nest.Drawings).Name);
    }

    [Fact]
    public void Build_WithNoImportedDrawing_ReturnsNoNest()
    {
        var unreadable = Row("PT01");
        File.WriteAllText(unreadable.DxfPath, "not a drawing");

        var result = BomNestBuilder.Build(Group(unreadable), Plate(), "Job", null);

        Assert.Null(result.Nest);
        Assert.StartsWith("PT01: ", Assert.Single(result.Errors));
    }

    private BomPartRow Row(string name, int? qty = 1) =>
        new()
        {
            FileName = name,
            Qty = qty,
            Material = "Stainless",
            Thickness = 0.25,
            DxfPath = WriteSquare(name),
        };

    private static BomImportGroup Group(params BomPartRow[] rows) => new("Stainless", 0.25, rows);

    private static BomGroupPlateSettings Plate() =>
        new()
        {
            PlateWidth = 60,
            PlateLength = 120,
            PartSpacing = 0.2,
            EdgeLeft = 0.1,
            EdgeBottom = 0.2,
            EdgeRight = 0.3,
            EdgeTop = 0.4,
        };

    private static double[] Edges(Spacing spacing) =>
        new[] { spacing.Left, spacing.Bottom, spacing.Right, spacing.Top };

    private string WriteSquare(string name)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(2, 0)));
        shape.Entities.Add(new Line(new Vector(2, 0), new Vector(2, 2)));
        shape.Entities.Add(new Line(new Vector(2, 2), new Vector(0, 2)));
        shape.Entities.Add(new Line(new Vector(0, 2), new Vector(0, 0)));

        var path = Path.Combine(folder, name + ".dxf");
        Dxf.ExportProgram(ConvertGeometry.ToProgram(shape), path);
        return path;
    }
}
