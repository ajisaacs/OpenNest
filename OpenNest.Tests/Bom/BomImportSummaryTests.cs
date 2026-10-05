using OpenNest.IO.Bom;

namespace OpenNest.Tests.Bom;

public class BomImportSummaryTests
{
    [Fact]
    public void Describe_CountsReadyRowsAndEachProblem()
    {
        var rows = new List<BomPartRow>
        {
            Row("PT01", "Steel", 0.25),
            Row("PT02", "Steel", 0.25),
            Row("PT03", null, 0.25),
            Row("PT04", "Steel", null),
            Row("PT05", "Steel", null),
            Row("PT06", "Steel", 0.25, dxfPath: null),
            Row(null, "Steel", 0.25),
        };

        Assert.Equal(
            "2 ready, 1 needs a material, 2 need a thickness, 1 no drawing found, 1 no file name",
            BomImportSummary.Describe(rows)
        );
    }

    [Fact]
    public void Describe_WithEveryRowReady_ListsOnlyTheReadyCount()
    {
        Assert.Equal("1 ready", BomImportSummary.Describe(new[] { Row("PT01", "Steel", 0.25) }));
        Assert.Equal("0 ready", BomImportSummary.Describe(Array.Empty<BomPartRow>()));
    }

    private static BomPartRow Row(
        string? fileName,
        string? material,
        double? thickness,
        string? dxfPath = "/d/part.dxf"
    ) =>
        new()
        {
            FileName = fileName,
            Material = material,
            Thickness = thickness,
            DxfPath = dxfPath,
        };
}
