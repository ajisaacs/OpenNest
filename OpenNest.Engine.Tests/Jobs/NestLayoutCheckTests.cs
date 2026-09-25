using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestLayoutCheckTests
{
    [Fact]
    public void TangentDiscsClearAtSafeMarginAcrossRadiiAnglesAndTolerances()
    {
        var count = 0;
        foreach (var radius in new[] { 0.1, 1.0, 10.0 })
        foreach (var tolerance in new[] { 0.0, 0.0005, 0.01 })
        foreach (var spacing in new[] { 0.0, 0.25 })
        {
            var program = new Program();
            program.MoveTo(radius, 0);
            program.Codes.Add(new ArcMove(radius, 0, 0, 0, RotationType.CW));
            var geometry = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(program));
            // Two inscribed engine outlines can underestimate true extent by t each.
            var distance = 2 * radius + spacing
                + NestTolerances.SafeClearanceMargin(tolerance) - 2 * tolerance;
            for (var degrees = 0; degrees < 360; degrees += 15)
            {
                var angle = degrees * System.Math.PI / 180;
                var a = new NestJobPlacement("disc", 0, 0.12345, -0.54321, angle / 3);
                var b = new NestJobPlacement("disc", 1, a.X + distance * System.Math.Cos(angle),
                    a.Y + distance * System.Math.Sin(angle), -angle / 7);
                Assert.True(NestLayoutCheck.Clears(geometry, a, geometry, b, spacing));
                Assert.True(NestLayoutCheck.Clears(geometry, b, geometry, a, spacing));
                count++;
            }
        }
        Assert.Equal(432, count);
    }

    [Fact]
    public void PairCheckDetectsOverlapAndLeavesGeometryUnchanged()
    {
        var geometry = JobPartGeometry.Read(PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 3)));
        var bounds = geometry.Bounds;
        Assert.False(NestLayoutCheck.Clears(geometry, new("p", 0, 0, 0, 0),
            geometry, new("p", 1, 2, 0, 0), 0.25));
        Assert.Equal(bounds, geometry.Bounds);
        Assert.Equal(0.0032, NestTolerances.SafeClearanceMargin(0.0005), 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => NestTolerances.SafeClearanceMargin(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => NestTolerances.SafeClearanceMargin(double.NaN));
    }
}
