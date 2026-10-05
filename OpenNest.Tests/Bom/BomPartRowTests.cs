using OpenNest.IO.Bom;

namespace OpenNest.Tests.Bom;

public class BomPartRowTests
{
    [Theory]
    [InlineData(null, "/d/PT01.dxf", "Steel", 0.25, BomRowStatus.NoFileName)]
    [InlineData(" ", "/d/PT01.dxf", "Steel", 0.25, BomRowStatus.NoFileName)]
    [InlineData("PT01", null, "Steel", 0.25, BomRowStatus.NoDrawing)]
    [InlineData("PT01", "/d/PT01.dxf", null, 0.25, BomRowStatus.NeedsMaterial)]
    [InlineData("PT01", "/d/PT01.dxf", "\t", 0.25, BomRowStatus.NeedsMaterial)]
    [InlineData("PT01", "/d/PT01.dxf", "Steel", null, BomRowStatus.NeedsThickness)]
    [InlineData("PT01", "/d/PT01.dxf", "Steel", 0.0, BomRowStatus.NeedsThickness)]
    [InlineData("PT01", "/d/PT01.dxf", "Steel", -0.25, BomRowStatus.NeedsThickness)]
    [InlineData("PT01", "/d/PT01.dxf", "Steel", double.NaN, BomRowStatus.NeedsThickness)]
    [InlineData("PT01", "/d/PT01.dxf", "Steel", double.PositiveInfinity, BomRowStatus.NeedsThickness)]
    [InlineData("PT01", "/d/PT01.dxf", "Steel", 0.25, BomRowStatus.Ready)]
    public void Status_FollowsTheFirstMissingValue(
        string? fileName,
        string? dxfPath,
        string? material,
        double? thickness,
        BomRowStatus expected
    )
    {
        var row = new BomPartRow
        {
            FileName = fileName,
            DxfPath = dxfPath,
            Material = material,
            Thickness = thickness,
            Qty = 1,
        };

        Assert.Equal(expected, row.Status);
        Assert.Equal(expected is not (BomRowStatus.NoFileName or BomRowStatus.NoDrawing), row.IsEditable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-2)]
    public void Status_NeedsAQuantityOfAtLeastOne(int? qty)
    {
        var row = ReadyRow();
        row.Qty = qty;

        Assert.Equal(BomRowStatus.NeedsQuantity, row.Status);
        Assert.True(row.IsEditable);
    }

    [Fact]
    public void Status_ChangesWhenTheOperatorFillsInAValue()
    {
        var row = new BomPartRow { FileName = "PT01", DxfPath = "/d/PT01.dxf" };
        Assert.Equal(BomRowStatus.NeedsMaterial, row.Status);

        row.Material = "Steel";
        Assert.Equal(BomRowStatus.NeedsThickness, row.Status);

        row.Thickness = 0.25;
        Assert.Equal(BomRowStatus.NeedsQuantity, row.Status);

        Assert.True(row.TrySetQuantity("3"));
        Assert.Equal(BomRowStatus.Ready, row.Status);
        Assert.Equal("Ready", row.StatusText);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2.5")]
    [InlineData("")]
    [InlineData("abc")]
    public void TrySetQuantity_RefusesInvalidTextAndKeepsTheQuantity(string text)
    {
        var row = ReadyRow();
        row.Qty = 4;
        row.QtyAssumed = true;
        var changes = Changes(row);

        Assert.False(row.TrySetQuantity(text));

        Assert.Equal(4, row.Qty);
        Assert.True(row.QtyAssumed);
        Assert.Empty(changes);
    }

    [Fact]
    public void TrySetQuantity_SetsTheQuantityAndReportsTheChange()
    {
        var row = ReadyRow();
        row.QtyAssumed = true;
        var changes = Changes(row);

        Assert.True(row.TrySetQuantity(" 12 "));

        Assert.Equal(12, row.Qty);
        Assert.False(row.QtyAssumed);
        Assert.Equal(new[] { "Qty", "Status", "StatusText" }, changes);
    }

    [Fact]
    public void EditingMaterialOrThickness_ReportsTheValueAndTheStatus()
    {
        var row = ReadyRow();
        var changes = Changes(row);

        row.Material = "Aluminum";
        row.Thickness = 0.125;
        row.Thickness = 0.125;

        Assert.Equal(
            new[] { "Material", "Status", "StatusText", "Thickness", "Status", "StatusText" },
            changes
        );
    }

    [Fact]
    public void Describe_NamesEveryStatus()
    {
        Assert.Equal(
            new[]
            {
                "Ready",
                "Needs material",
                "Needs thickness",
                "Needs quantity",
                "No drawing found",
                "No file name",
            },
            Enum.GetValues<BomRowStatus>().Select(BomPartRow.Describe)
        );
    }

    private static BomPartRow ReadyRow() =>
        new()
        {
            FileName = "PT01",
            DxfPath = "/d/PT01.dxf",
            Material = "Steel",
            Thickness = 0.25,
            Qty = 1,
        };

    private static List<string?> Changes(BomPartRow row)
    {
        var names = new List<string?>();
        row.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }
}
