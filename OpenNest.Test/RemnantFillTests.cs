using OpenNest.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace OpenNest.Test;

public class RemnantFillTests
{
    private readonly ITestOutputHelper _output;

    public RemnantFillTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    [Trait("Category", "Remnant")]
    public void N0308_017_PT02_RemnantFillsAtLeast10()
    {
        Skip.IfNot(TestData.IsAvailable, TestData.SkipReason);

        var nest = TestData.LoadNest("N0308-017.zip");
        var plate = nest.Plates[0];
        var pt02 = nest.Drawings.First(d => d.Name.Contains("PT02"));
        var remnant = plate.GetRemnants()[0];

        _output.WriteLine($"Remnant: ({remnant.X:F2},{remnant.Y:F2}) {remnant.Width:F2}x{remnant.Height:F2}");

        var countBefore = plate.Parts.Count;
        var engine = new NestEngine(plate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Fill(new NestItem { Drawing = pt02, Quantity = 0 }, remnant);
        sw.Stop();

        var added = plate.Parts.Count - countBefore;
        _output.WriteLine($"Added: {added} parts | Time: {sw.ElapsedMilliseconds}ms");

        Assert.True(added >= 10, $"Expected >= 10 parts in remnant, got {added}");

        var newParts = plate.Parts.Skip(countBefore).ToList();
        AssertNoOverlaps(newParts);
        AssertNoCrossOverlaps(plate.Parts.Take(countBefore).ToList(), newParts);
    }

    [SkippableFact]
    [Trait("Category", "Remnant")]
    public void N0308_008_HingePlate_RemnantFillsAtLeast8()
    {
        Skip.IfNot(TestData.IsAvailable, TestData.SkipReason);

        var nest = TestData.LoadNest("N0308-008.zip");
        var plate = nest.Plates[0];
        var hinge = nest.Drawings.First(d => d.Name.Contains("HINGE PLATE #2"));
        var remnants = plate.GetRemnants();

        _output.WriteLine($"Remnant 0: ({remnants[0].X:F2},{remnants[0].Y:F2}) {remnants[0].Width:F2}x{remnants[0].Height:F2}");

        var countBefore = plate.Parts.Count;
        var engine = new NestEngine(plate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Fill(new NestItem { Drawing = hinge, Quantity = 0 }, remnants[0]);
        sw.Stop();

        var added = plate.Parts.Count - countBefore;
        _output.WriteLine($"Added: {added} parts | Time: {sw.ElapsedMilliseconds}ms");

        Assert.True(added >= 8, $"Expected >= 8 parts in remnant, got {added}");

        var newParts = plate.Parts.Skip(countBefore).ToList();
        AssertNoOverlaps(newParts);
        AssertNoCrossOverlaps(plate.Parts.Take(countBefore).ToList(), newParts);
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

    private void AssertNoCrossOverlaps(List<Part> existing, List<Part> added)
    {
        for (var i = 0; i < existing.Count; i++)
        {
            for (var j = 0; j < added.Count; j++)
            {
                if (existing[i].Intersects(added[j], out _))
                    Assert.Fail($"Cross-overlap: existing [{i}] vs added [{j}]");
            }
        }
    }
}
