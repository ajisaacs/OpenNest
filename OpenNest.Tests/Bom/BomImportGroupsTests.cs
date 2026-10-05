using OpenNest.IO.Bom;

namespace OpenNest.Tests.Bom;

public class BomImportGroupsTests
{
    [Fact]
    public void Build_GroupsByMaterialIgnoringCaseThenThickness()
    {
        var rows = new List<BomPartRow>
        {
            Row("PT01", "Stainless", 0.25, qty: 2),
            Row("PT02", "Aluminum", 0.125, qty: 1),
            Row("PT03", "STAINLESS", 0.25, qty: null),
            Row("PT04", "Stainless", 0.125, qty: 4),
            Row("PT05", "Aluminum", 0.0625, qty: 3),
        };

        var groups = BomImportGroups.Build(rows);

        Assert.Equal(
            new[] { "Aluminum 0.0625", "Aluminum 0.125", "Stainless 0.125", "Stainless 0.25" },
            groups.Select(g => $"{g.Material} {g.Thickness}")
        );
        var stainless = groups[3];
        Assert.Equal(new[] { "PT01", "PT03" }, stainless.Parts.Select(p => p.FileName));
        Assert.Equal(2, stainless.TotalQty);
        Assert.Equal(BomImportGroups.Key("stainless", 0.25), stainless.Key);
    }

    [Fact]
    public void Build_LeavesOutRowsThatCannotBeImported()
    {
        var locked = Row("PT02", "Stainless", 0.25);
        locked.IsEditable = false;
        var noDrawing = Row("PT05", "Stainless", 0.25);
        noDrawing.DxfPath = null;
        var rows = new List<BomPartRow>
        {
            Row("PT01", "Stainless", 0.25),
            locked,
            Row("PT03", " ", 0.25),
            Row("PT04", "Stainless", null),
            noDrawing,
        };

        var groups = BomImportGroups.Build(rows);

        var group = Assert.Single(groups);
        Assert.Equal("PT01", Assert.Single(group.Parts).FileName);
    }

    [Fact]
    public void Key_IgnoresMaterialCase()
    {
        Assert.Equal(BomImportGroups.Key("Stainless", 0.25), BomImportGroups.Key("STAINLESS", 0.25));
        Assert.NotEqual(BomImportGroups.Key("Stainless", 0.25), BomImportGroups.Key("Stainless", 0.125));
    }

    private static BomPartRow Row(string fileName, string material, double? thickness, int? qty = 1) =>
        new()
        {
            FileName = fileName,
            Material = material,
            Thickness = thickness,
            Qty = qty,
            DxfPath = $"/drawings/{fileName}.dxf",
            Status = "Matched",
            IsEditable = true,
        };
}
