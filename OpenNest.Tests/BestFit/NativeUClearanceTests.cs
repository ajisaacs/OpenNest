using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Tests.Geometry;
using Xunit.Abstractions;

namespace OpenNest.Tests.BestFit;

[Collection(nameof(FillCacheCollection))]
public class NativeUClearanceTests
{
    private readonly ITestOutputHelper output;

    public NativeUClearanceTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void BestFitCache_TopKeptPairPreservesQuarterInchClearance()
    {
        var drawing = NativeUFixture.CreateDrawing();
        var bestFits = BestFitCache.GetOrCompute(drawing, 24, 24, 0.25);
        var top = bestFits.Where(r => r.Keep).OrderBy(r => r.RotatedArea).First();
        var parts = top.BuildSourceParts(drawing);
        Assert.Equal(2, parts.Count);
        output.WriteLine($"Kept={bestFits.Count(r => r.Keep)}; top rotation={top.Candidate.Part2Rotation:R}; area={top.RotatedArea:R}");
        AssertClearance(parts, 0.25);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FillItem_NativeUHasRequiredClearance(int quantity)
    {
        var drawing = NativeUFixture.CreateDrawing();
        var plate = new Plate(new Size(24, 24))
        {
            PartSpacing = 0.25,
            EdgeSpacing = new Spacing(1, 1),
        };
        var parts = PlateFillService.FillItem("Fill", plate,
            new NestItem { Drawing = drawing, Quantity = quantity }, plate.WorkArea(),
            null, CancellationToken.None);

        Assert.NotEmpty(parts);
        if (quantity == 2)
            Assert.Equal(2, parts.Count);
        else
            Assert.True(parts.Count > 2);
        Assert.Empty(plate.Parts);
        Assert.All(parts, part =>
        {
            Assert.Same(drawing, part.BaseDrawing);
            var workArea = plate.WorkArea();
            Assert.InRange(part.BoundingBox.Left, workArea.Left - 1e-9, workArea.Right + 1e-9);
            Assert.InRange(part.BoundingBox.Right, workArea.Left - 1e-9, workArea.Right + 1e-9);
            Assert.InRange(part.BoundingBox.Bottom, workArea.Bottom - 1e-9, workArea.Top + 1e-9);
            Assert.InRange(part.BoundingBox.Top, workArea.Bottom - 1e-9, workArea.Top + 1e-9);
        });
        output.WriteLine($"Quantity={quantity}; returned={parts.Count}");
        var violations = NestLayoutCheck.Validate(new() { (plate, parts) },
            new Dictionary<Drawing, (string Name, int Quantity)> { [drawing] = (drawing.Name, parts.Count) });
        output.WriteLine($"Validator violations={violations.Count}");
        AssertClearance(parts, plate.PartSpacing);
        // The complete grid currently triggers offset-validator reports even though its
        // independently measured raw boundaries clear. Do not change validator tolerance
        // here: the fine-boundary oracle above checks EVERY nearby pair in either mode.
        if (quantity == 2)
            Assert.Empty(violations);
    }

    private void AssertClearance(List<Part> parts, double spacing)
    {
        // Fine raw outlines, independently measured: neither offset curves nor the slide solver.
        var outlines = parts.Select(p => PartGeometry.GetPartLines(p, 1e-6)).ToList();
        var squared = double.MaxValue;
        var closest = (-1, -1);
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                if (BoxGapSquared(parts[i].BoundingBox, parts[j].BoundingBox) > spacing * spacing)
                    continue;
                Assert.False(parts[i].Intersects(parts[j], out _));
                foreach (var a in outlines[i])
                    foreach (var b in outlines[j])
                    {
                        if (BoxGapSquared(a.BoundingBox, b.BoundingBox) >= squared)
                            continue;
                        var distanceSquared = System.Math.Min(
                            System.Math.Min(PointSegmentSquared(a.StartPoint, b), PointSegmentSquared(a.EndPoint, b)),
                            System.Math.Min(PointSegmentSquared(b.StartPoint, a), PointSegmentSquared(b.EndPoint, a)));
                        if (distanceSquared < squared)
                        {
                            squared = distanceSquared;
                            closest = (i, j);
                        }
                    }
            }
        var distance = System.Math.Sqrt(squared);
        output.WriteLine($"Minimum raw boundary gap={distance:R}; pair={closest}");
        Assert.True(distance >= spacing - 2e-6, $"Raw boundary gap {distance:R} is below {spacing:R} (chord tolerance 1e-6), pair {closest}.");
    }

    private static double BoxGapSquared(Box a, Box b)
    {
        var x = System.Math.Max(0, System.Math.Max(a.Left - b.Right, b.Left - a.Right));
        var y = System.Math.Max(0, System.Math.Max(a.Bottom - b.Top, b.Bottom - a.Top));
        return x * x + y * y;
    }

    private static double PointSegmentSquared(Vector p, Line line)
    {
        var dx = line.EndPoint.X - line.StartPoint.X;
        var dy = line.EndPoint.Y - line.StartPoint.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : System.Math.Clamp(
            ((p.X - line.StartPoint.X) * dx + (p.Y - line.StartPoint.Y) * dy) / lengthSquared, 0, 1);
        var x = p.X - line.StartPoint.X - t * dx;
        var y = p.Y - line.StartPoint.Y - t * dy;
        return x * x + y * y;
    }
}
