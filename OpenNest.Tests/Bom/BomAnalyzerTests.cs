namespace OpenNest.Tests.Bom;

using OpenNest.IO.Bom;

public class BomAnalyzerTests
{
    [Fact]
    public void Analyze_GroupsByMaterialAndThickness()
    {
        var items = new List<BomItem>
        {
            new BomItem { FileName = "PT01", Thickness = 0.25, Material = "AISI 304", Qty = 2 },
            new BomItem { FileName = "PT02", Thickness = 0.25, Material = "AISI 304", Qty = 3 },
            new BomItem { FileName = "PT03", Thickness = 0.375, Material = "AISI 304", Qty = 1 },
        };

        var result = BomAnalyzer.Analyze(items, "C:\\fake");

        Assert.Equal(2, result.Groups.Count);
        Assert.Single(result.Groups, g => g.Thickness == 0.25 && g.Material == "AISI 304");
        Assert.Single(result.Groups, g => g.Thickness == 0.375 && g.Material == "AISI 304");
    }

    [Fact]
    public void Analyze_SkipsItemsWithNoFileName()
    {
        var items = new List<BomItem>
        {
            new BomItem { FileName = "PT01", Thickness = 0.25, Material = "AISI 304", Qty = 2 },
            new BomItem { FileName = null, Thickness = 0.25, Material = "AISI 304", Qty = 3 },
            new BomItem { FileName = "", Thickness = 0.25, Material = "AISI 304", Qty = 1 },
        };

        var result = BomAnalyzer.Analyze(items, "C:\\fake");

        Assert.Equal(2, result.Skipped.Count);
    }

    [Fact]
    public void Analyze_SkipsItemsWithNoThickness()
    {
        var items = new List<BomItem>
        {
            new BomItem { FileName = "PT01", Thickness = 0.25, Material = "AISI 304", Qty = 2 },
            new BomItem { FileName = "PT02", Thickness = null, Material = "AISI 304", Qty = 3 },
        };

        var result = BomAnalyzer.Analyze(items, "C:\\fake");

        Assert.Single(result.Skipped);
        Assert.Equal("PT02", result.Skipped[0].FileName);
    }

    [Fact]
    public void Analyze_GroupsMaterialCaseInsensitive()
    {
        var items = new List<BomItem>
        {
            new BomItem { FileName = "PT01", Thickness = 0.25, Material = "AISI 304", Qty = 1 },
            new BomItem { FileName = "PT02", Thickness = 0.25, Material = "aisi 304", Qty = 1 },
        };

        var result = BomAnalyzer.Analyze(items, "C:\\fake");

        Assert.Single(result.Groups);
        Assert.Equal(2, result.Groups[0].Parts.Count);
    }

    [Fact]
    public void Analyze_MatchesDxfFiles_WithAndWithoutExtension()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BomAnalyzerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "PT01.dxf"), "");

            var items = new List<BomItem>
            {
                new BomItem { FileName = "PT01", Thickness = 0.25, Material = "AISI 304", Qty = 2 },
            };

            var result = BomAnalyzer.Analyze(items, tempDir);

            Assert.Single(result.Groups);
            Assert.Single(result.Groups[0].Parts);
            Assert.EndsWith(".dxf", result.Groups[0].Parts[0].DxfPath, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(result.Unmatched);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Analyze_ReportsUnmatchedItems_WhenDxfNotFound()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BomAnalyzerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var items = new List<BomItem>
            {
                new BomItem { FileName = "PT99", Thickness = 0.25, Material = "AISI 304", Qty = 1 },
            };

            var result = BomAnalyzer.Analyze(items, tempDir);

            Assert.Single(result.Unmatched);
            Assert.Empty(result.Groups);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Analyze_DifferentMaterials_SameThickness_SeparateGroups()
    {
        var items = new List<BomItem>
        {
            new BomItem { FileName = "PT01", Thickness = 0.25, Material = "AISI 304", Qty = 1 },
            new BomItem { FileName = "PT02", Thickness = 0.25, Material = "Plain Carbon Steel", Qty = 1 },
        };

        var result = BomAnalyzer.Analyze(items, "C:\\fake");

        Assert.Equal(2, result.Groups.Count);
    }

    [Fact]
    public void Analyze_GroupPartsCount_MatchesBomItems()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BomAnalyzerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "PT01.dxf"), "");
            File.WriteAllText(Path.Combine(tempDir, "PT02.dxf"), "");
            File.WriteAllText(Path.Combine(tempDir, "PT03.dxf"), "");

            var items = new List<BomItem>
            {
                new BomItem { FileName = "PT01", Thickness = 0.25, Material = "AISI 304", Qty = 2 },
                new BomItem { FileName = "PT02", Thickness = 0.25, Material = "AISI 304", Qty = 5 },
                new BomItem { FileName = "PT03", Thickness = 0.375, Material = "AISI 304", Qty = 1 },
            };

            var result = BomAnalyzer.Analyze(items, tempDir);

            var quarterGroup = result.Groups.First(g => g.Thickness == 0.25);
            Assert.Equal(2, quarterGroup.Parts.Count);
            Assert.Equal(2, quarterGroup.Parts.First(p => p.Item.FileName == "PT01").Item.Qty);

            var threeEighthsGroup = result.Groups.First(g => g.Thickness == 0.375);
            Assert.Single(threeEighthsGroup.Parts);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
