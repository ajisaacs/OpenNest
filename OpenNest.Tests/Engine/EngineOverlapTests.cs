using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using Xunit.Abstractions;
using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement;

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
    [InlineData("Default")]
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

        var hasOverlaps = plate.HasOverlappingParts(out var collisionPoints);
        _output.WriteLine($"Overlaps: {hasOverlaps} ({collisionPoints.Count} collision pts)");

        if (hasOverlaps)
        {
            for (var i = 0; i < System.Math.Min(collisionPoints.Count, 10); i++)
                _output.WriteLine($"  ({collisionPoints[i].X:F2}, {collisionPoints[i].Y:F2})");
        }

        Assert.False(
            hasOverlaps,
            $"Strategy '{strategy}' produced {collisionPoints.Count} collision point(s) with {plate.Parts.Count} parts"
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

        var hasOverlaps = plate.HasOverlappingParts(out var pts);
        _output.WriteLine($"Adjacent squares: overlaps={hasOverlaps}, collision count={pts.Count}");

        Assert.False(
            hasOverlaps,
            "Adjacent edge-touching parts should not be reported as overlapping"
        );
    }
}
