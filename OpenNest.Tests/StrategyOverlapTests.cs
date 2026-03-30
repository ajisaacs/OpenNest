using OpenNest.Converters;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.IO;
using Xunit.Abstractions;

namespace OpenNest.Tests;

public class StrategyOverlapTests
{
    private const string DxfPath = @"C:\Users\AJ\Desktop\Templates\4526 A14 PT15.dxf";
    private readonly ITestOutputHelper _output;

    public StrategyOverlapTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Drawing ImportDxf()
    {
        var importer = new DxfImporter();
        importer.GetGeometry(DxfPath, out var geometry);
        var pgm = ConvertGeometry.ToProgram(geometry);
        return new Drawing("PT15", pgm);
    }

    [Fact]
    public void EachStrategy_CheckOverlaps()
    {
        var drawing = ImportDxf();
        _output.WriteLine($"Drawing bbox: {drawing.Program.BoundingBox().Width:F2} x {drawing.Program.BoundingBox().Length:F2}");

        var strategies = FillStrategyRegistry.Strategies.ToList();
        var item = new NestItem { Drawing = drawing };
        var classification = PartClassifier.Classify(drawing);
        var failures = new List<string>();

        foreach (var strategy in strategies)
        {
            var plate = new Plate(60, 120);
            var comparer = new DefaultFillComparer();
            var policy = new FillPolicy(comparer);
            var context = new FillContext
            {
                Item = item,
                WorkArea = plate.WorkArea(),
                Plate = plate,
                PlateNumber = 0,
                Token = System.Threading.CancellationToken.None,
                Policy = policy,
            };
            context.SharedState["BestRotation"] = classification.PrimaryAngle;
            context.SharedState["Classification"] = classification;
            context.SharedState["AngleCandidates"] = new AngleCandidateBuilder().Build(
                item, classification, context.WorkArea);

            var parts = strategy.Fill(context);
            var count = parts?.Count ?? 0;

            _output.WriteLine($"\n{strategy.GetType().Name} (Phase: {strategy.Phase}, Order: {strategy.Order}): {count} parts");

            if (count == 0)
                continue;

            plate.Parts.AddRange(parts);
            _output.WriteLine($"  Utilization: {plate.Utilization():P1}");

            var rotGroups = parts
                .GroupBy(p => System.Math.Round(OpenNest.Math.Angle.ToDegrees(p.Rotation), 1))
                .OrderBy(g => g.Key);
            foreach (var g in rotGroups)
                _output.WriteLine($"  Rotation {g.Key:F1}°: {g.Count()} parts");

            var hasOverlaps = plate.HasOverlappingParts(out var pts);
            _output.WriteLine($"  Overlaps: {hasOverlaps} ({pts.Count} collision pts)");

            if (hasOverlaps)
            {
                failures.Add($"{strategy.GetType().Name} ({strategy.Phase}): {pts.Count} collision pts, {count} parts");

                // Show overlapping pair details
                for (var a = 0; a < parts.Count; a++)
                {
                    for (var b = a + 1; b < parts.Count; b++)
                    {
                        var ba = parts[a].BoundingBox;
                        var bb = parts[b].BoundingBox;
                        var oX = System.Math.Min(ba.Right, bb.Right) - System.Math.Max(ba.Left, bb.Left);
                        var oY = System.Math.Min(ba.Top, bb.Top) - System.Math.Max(ba.Bottom, bb.Bottom);
                        if (oX <= OpenNest.Math.Tolerance.Epsilon || oY <= OpenNest.Math.Tolerance.Epsilon)
                            continue;

                        if (parts[a].Intersects(parts[b], out var pairPts) && pairPts.Count > 0)
                        {
                            _output.WriteLine($"    [{a}] vs [{b}]: {pairPts.Count} pts, bbox overlap: {oX:F4} x {oY:F4}");
                            _output.WriteLine($"      [{a}]: loc=({parts[a].Location.X:F4},{parts[a].Location.Y:F4}) rot={OpenNest.Math.Angle.ToDegrees(parts[a].Rotation):F2}°");
                            _output.WriteLine($"      [{b}]: loc=({parts[b].Location.X:F4},{parts[b].Location.Y:F4}) rot={OpenNest.Math.Angle.ToDegrees(parts[b].Rotation):F2}°");
                        }
                    }
                }
            }
        }

        _output.WriteLine($"\n=== SUMMARY ===");
        foreach (var f in failures)
            _output.WriteLine($"  OVERLAP: {f}");

        Assert.Empty(failures);
    }

}
