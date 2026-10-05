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
        };

        Assert.Equal(expected, row.Status);
        Assert.Equal(expected is not (BomRowStatus.NoFileName or BomRowStatus.NoDrawing), row.IsEditable);
    }

    [Fact]
    public void Status_ChangesWhenTheOperatorFillsInAValue()
    {
        var row = new BomPartRow { FileName = "PT01", DxfPath = "/d/PT01.dxf" };
        Assert.Equal(BomRowStatus.NeedsMaterial, row.Status);

        row.Material = "Steel";
        Assert.Equal(BomRowStatus.NeedsThickness, row.Status);

        row.Thickness = 0.25;
        Assert.Equal(BomRowStatus.Ready, row.Status);
        Assert.Equal("Ready", row.StatusText);
    }

    [Fact]
    public void Describe_NamesEveryStatus()
    {
        Assert.Equal(
            new[] { "Ready", "Needs material", "Needs thickness", "No drawing found", "No file name" },
            Enum.GetValues<BomRowStatus>().Select(BomPartRow.Describe)
        );
    }
}
