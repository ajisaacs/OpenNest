using OpenNest.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace OpenNest.Test;

public class FillTests
{
    private readonly ITestOutputHelper _output;

    public FillTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    [Trait("Category", "Fill")]
    public void N0308_008_HingePlate_FillsAtLeast75()
    {
        Skip.IfNot(TestData.IsAvailable, TestData.SkipReason);

        var nest = TestData.LoadNest("N0308-008.zip");
        var hinge = nest.Drawings.First(d => d.Name.Contains("HINGE PLATE #2"));
        var plate = TestData.CleanPlateFrom(nest.Plates[0]);

        var engine = new NestEngine(plate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Fill(new NestItem { Drawing = hinge, Quantity = 0 });
        sw.Stop();

        _output.WriteLine($"Parts: {plate.Parts.Count} | Time: {sw.ElapsedMilliseconds}ms");

        Assert.True(plate.Parts.Count >= 75,
            $"Expected >= 75 parts, got {plate.Parts.Count}");
        AssertNoOverlaps(plate.Parts.ToList());
    }

    [SkippableFact]
    [Trait("Category", "Fill")]
    public void RemainderStripRefill_30pcs_FillsAtLeast32()
    {
        Skip.IfNot(TestData.IsAvailable, TestData.SkipReason);

        var nest = TestData.LoadNest("30pcs Fill.zip");
        var drawing = nest.Drawings.First();
        var plate = TestData.CleanPlateFrom(nest.Plates[0]);

        var engine = new NestEngine(plate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Fill(new NestItem { Drawing = drawing, Quantity = 0 });
        sw.Stop();

        _output.WriteLine($"Parts: {plate.Parts.Count} | Time: {sw.ElapsedMilliseconds}ms");

        Assert.True(plate.Parts.Count >= 32,
            $"Expected >= 32 parts, got {plate.Parts.Count}");
        AssertNoOverlaps(plate.Parts.ToList());
    }

    private void AssertNoOverlaps(List<Part> parts)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            for (var j = i + 1; j < parts.Count; j++)
            {
                if (parts[i].Intersects(parts[j], out _))
                    Assert.Fail($"Overlap detected: part [{i}] vs [{j}]");
            }
        }
    }
}
