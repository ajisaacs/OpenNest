using OpenNest.IO.Bom;

namespace OpenNest.Tests.Bom;

public sealed class BomImportRowsTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "bom-rows-" + Guid.NewGuid().ToString("N")
    );

    public BomImportRowsTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Build_MatchesRowsToDrawingFilesInBomOrder()
    {
        Touch("PT01.dxf");
        Touch("PT03.dwg");
        var items = new List<BomItem>
        {
            Item("PT01", qty: 2, itemNum: 1, description: "Gusset"),
            Item("PT02", qty: 3, itemNum: 2),
            Item(null, qty: 4, itemNum: 3),
            Item("PT03", qty: null, itemNum: 4),
        };

        var rows = BomImportRows.Build(items, folder);

        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(r => r.ItemNum ?? 0));
        Assert.Equal(
            new[]
            {
                BomRowStatus.Ready,
                BomRowStatus.NoDrawing,
                BomRowStatus.NoFileName,
                BomRowStatus.Ready,
            },
            rows.Select(r => r.Status)
        );
        Assert.Equal(new[] { true, false, false, true }, rows.Select(r => r.IsEditable));
        Assert.Equal(Path.Combine(folder, "PT01.dxf"), rows[0].DxfPath);
        Assert.Null(rows[1].DxfPath);
        Assert.Null(rows[2].DxfPath);
        Assert.Equal(Path.Combine(folder, "PT03.dwg"), rows[3].DxfPath);

        Assert.Equal("Gusset", rows[0].Description);
        Assert.Equal(2, rows[0].Qty);
        Assert.Equal(2, rows[0].BomQty);
        Assert.False(rows[0].QtyAssumed);
        Assert.Equal(1, rows[3].Qty);
        Assert.Null(rows[3].BomQty);
        Assert.True(rows[3].QtyAssumed);
        Assert.Equal("Stainless", rows[0].Material);
        Assert.Equal(0.25, rows[0].Thickness);
    }

    [Fact]
    public void Build_MatchesFileNamesIgnoringCase()
    {
        Touch("PT01.dxf");

        var rows = BomImportRows.Build(new List<BomItem> { Item("pt01") }, folder);

        Assert.Equal(BomRowStatus.Ready, rows[0].Status);
        Assert.Equal(Path.Combine(folder, "PT01.dxf"), rows[0].DxfPath);
    }

    [Fact]
    public void Build_MatchesFileNamesThatIncludeTheExtension()
    {
        Touch("PT01.dxf");
        Touch("PT02.dwg");

        var rows = BomImportRows.Build(
            new List<BomItem> { Item("PT01.dxf"), Item("PT02.DWG") },
            folder
        );

        Assert.All(rows, r => Assert.True(r.IsEditable, r.FileName));
        Assert.Equal(Path.Combine(folder, "PT01.dxf"), rows[0].DxfPath);
        Assert.Equal(Path.Combine(folder, "PT02.dwg"), rows[1].DxfPath);
    }

    [Fact]
    public void Build_RowWithoutThickness_StillFindsItsDrawing()
    {
        Touch("PT01.dxf");
        var item = Item("PT01");
        item.Thickness = null;

        var rows = BomImportRows.Build(new List<BomItem> { item }, folder);

        Assert.Equal(Path.Combine(folder, "PT01.dxf"), rows[0].DxfPath);
        Assert.True(rows[0].IsEditable);
        Assert.Equal(BomRowStatus.NeedsThickness, rows[0].Status);
    }

    [Fact]
    public void Build_RowWithoutMaterial_NeedsAMaterial()
    {
        Touch("PT01.dxf");
        var item = Item("PT01");
        item.Material = " ";

        var rows = BomImportRows.Build(new List<BomItem> { item }, folder);

        Assert.True(rows[0].IsEditable);
        Assert.Equal(BomRowStatus.NeedsMaterial, rows[0].Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Build_RowWithBomQuantityBelowOne_NeedsAQuantity(int bomQty)
    {
        Touch("PT01.dxf");

        var rows = BomImportRows.Build(new List<BomItem> { Item("PT01", qty: bomQty) }, folder);

        Assert.Equal(bomQty, rows[0].Qty);
        Assert.False(rows[0].QtyAssumed);
        Assert.Equal(BomRowStatus.NeedsQuantity, rows[0].Status);
    }

    [Fact]
    public void Build_WithMissingFolder_FindsNoDrawings()
    {
        var missing = Path.Combine(folder, "missing");

        var rows = BomImportRows.Build(new List<BomItem> { Item("PT01") }, missing);

        Assert.Equal(BomRowStatus.NoDrawing, rows[0].Status);
        Assert.False(rows[0].IsEditable);
        Assert.Null(rows[0].DxfPath);
    }

    private void Touch(string name) => File.WriteAllText(Path.Combine(folder, name), "");

    private static BomItem Item(
        string? fileName,
        int? qty = 1,
        int? itemNum = null,
        string? description = null
    ) =>
        new()
        {
            ItemNum = itemNum,
            FileName = fileName,
            Qty = qty,
            Description = description,
            Material = "Stainless",
            Thickness = 0.25,
        };
}
