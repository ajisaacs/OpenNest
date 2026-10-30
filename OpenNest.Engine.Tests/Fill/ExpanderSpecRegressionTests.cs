using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

public class ExpanderSpecRegressionTests
{
    private static Program Disk(double radius)
    {
        var p = new Program();
        p.MoveTo(radius, 0);
        p.ArcTo(-radius, 0, 0, 0, RotationType.CCW);
        p.ArcTo(radius, 0, 0, 0, RotationType.CCW);
        return p;
    }

    private static (Plate Plate, Part A, Part B) Fixture()
    {
        var plate = new Plate(new Size(12, 30)) { EdgeSpacing = new Spacing(0, 0) };
        var a = new Part(new Drawing("disk-a", Disk(2)), new Vector(7, 6));
        var b = new Part(new Drawing("disk-b", Disk(2)), new Vector(9, 6));
        plate.Parts.Add(a);
        plate.Parts.Add(b);
        return (plate, a, b);
    }

    [Fact]
    public void Separate_OverlappingNativeDisks_RetainsCertifiedConvexExit()
    {
        var (plate, a, b) = Fixture();
        var result = Expander.Separate(new List<Part> { a, b }, plate, spacing: 3);
        Assert.True(result.Converged,
            $"remaining violations={result.Violations.Count}; mover=({result.Positions[1].X:R},{result.Positions[1].Y:R})");
        // Independent analytic disk material clearance, not production polygons.
        Assert.True(result.Positions[1].DistanceTo(result.Positions[0]) - 4 >= 2.99);
        Assert.Equal(7, a.Location.X);
        Assert.Equal(9, b.Location.X); // scratch-only
    }

    [Fact]
    public void Separate_OverlappingNativeDisks_CannotPassThroughThirdWall()
    {
        var (plate, a, b) = Fixture();
        var wallProgram = new Program();
        wallProgram.MoveTo(0, 0);
        wallProgram.LineTo(0.2, 0);
        wallProgram.LineTo(0.2, 12);
        wallProgram.LineTo(0, 12);
        wallProgram.LineTo(0, 0);
        var wall = new Part(new Drawing("fixed-wall", wallProgram), new Vector(12, 0));
        plate.Parts.Add(wall);

        var result = Expander.Separate(new List<Part> { a, b }, plate, spacing: 3);
        Assert.False(result.Converged);
        Assert.True(result.Positions[1].X + 2 <= 12 + 1e-6,
            $"native disk crossed third wall: center={result.Positions[1].X:R}");
        Assert.Equal(9, b.Location.X);
        Assert.Equal(12, wall.Location.X);
    }

    [Fact]
    public void Expand_OverlappingNativeDisks_RetainsExistingSeparation()
    {
        var (plate, a, b) = Fixture();
        var result = Expander.Expand(new List<Part> { a, b }, plate,
            new Expander.Options { InitialStep = 0.5, MaxSpacing = 3 });
        var nativeGap = a.Location.DistanceTo(b.Location) - 4;
        Assert.True(nativeGap >= 2.99,
            $"native gap={nativeGap:R}; achieved={result.AchievedSpacing:R}; violations={result.Violations.Count}");
        Assert.True(result.AchievedSpacing >= 2.9);
        Assert.Empty(result.Violations);
        Assert.Equal(7, a.Location.X);
    }
}
