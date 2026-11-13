using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.IO;
using Xunit.Abstractions;

namespace OpenNest.Tests.Engine;

public class EngineOverlapTests
{
    private const string DxfPath = @"C:\Users\AJ\Desktop\Templates\4526 A14 PT15.dxf";
    private readonly ITestOutputHelper _output;

    public EngineOverlapTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Drawing? ImportDxf()
    {
        if (!System.IO.File.Exists(DxfPath))
            return null;

        var geometry = Dxf.GetGeometry(DxfPath);
        var pgm = ConvertGeometry.ToProgram(geometry);
        return new Drawing("PT15", pgm);
    }

    [Theory]
    [InlineData("Fill")]
    [InlineData("Strip")]
    [InlineData("Vertical Remnant")]
    [InlineData("Horizontal Remnant")]
    public void FillPlate_NoOverlaps(string strategy)
    {
        var drawing = ImportDxf();
        if (drawing is null)
            return; // Skip if test DXF not available

        var plate = new Plate(60, 120);

        var item = new NestItem { Drawing = drawing };
        var parts = PlateFillService.FillItem(
            strategy,
            plate,
            item,
            plate.WorkArea(),
            progress: null,
            token: CancellationToken.None
        );
        plate.Parts.AddRange(parts);

        _output.WriteLine(
            $"Strategy: {strategy}, Parts: {plate.Parts.Count}, Utilization: {plate.Utilization():P1}"
        );

        // Show rotation distribution
        var rotGroups = plate
            .Parts.GroupBy(p => System.Math.Round(OpenNest.Math.Angle.ToDegrees(p.Rotation), 1))
            .OrderBy(g => g.Key);
        foreach (var g in rotGroups)
            _output.WriteLine($"  Rotation {g.Key:F1}°: {g.Count()} parts");

        var pairs = PartOverlapChecker.FindOverlappingPairs(plate.Parts);
        _output.WriteLine($"Overlaps: {pairs.Count > 0} ({pairs.Count} overlapping pair(s))");

        foreach (var (first, second) in pairs.Take(10))
            _output.WriteLine($"  part {plate.Parts.IndexOf(first)} overlaps part {plate.Parts.IndexOf(second)}");

        Assert.True(
            pairs.Count == 0,
            $"Strategy '{strategy}' produced {pairs.Count} overlapping pair(s) with {plate.Parts.Count} parts"
        );
    }

    [Fact]
    public void AdjacentParts_ShouldNotOverlap()
    {
        var plate = TestHelpers.MakePlate(
            60,
            120,
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(10, 0, 10)
        );

        var pairs = PartOverlapChecker.FindOverlappingPairs(plate.Parts);
        _output.WriteLine($"Adjacent squares: overlapping pairs={pairs.Count}");

        Assert.True(
            pairs.Count == 0,
            "Adjacent edge-touching parts should not be reported as overlapping"
        );
    }
}
