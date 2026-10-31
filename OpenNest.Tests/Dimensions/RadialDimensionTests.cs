using OpenNest.Dimensions;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Dimensions;

public class RadialDimensionTests
{
    private static readonly Layer Cut = new("0") { IsVisible = true };

    private static Circle Hole(double x = 5, double y = 3, double radius = 1) =>
        new(x, y, radius) { Layer = Cut, Id = Guid.NewGuid() };

    private static DimensionLayout Layout(Entity curve, DimensionKind kind, double angle, double offset = 0.5) =>
        DimensionBuilder.Layout([curve], new DrawingDimension
        {
            Kind = kind,
            EntityId = curve.Id,
            Angle = angle,
            Offset = offset,
        });

    [Fact]
    public void Radius_MeasuresTheCircle_WithTheLeaderFromTheCenter()
    {
        var hole = Hole(radius: 1.25);

        var layout = Layout(hole, DimensionKind.Radius, 0, 0.5);

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(1.25, layout.Value, 9);
        Assert.Equal("R1.250", layout.DisplayText);
        Assert.Equal(0, layout.LineStart.DistanceTo(new Vector(5, 3)), 12);
        Assert.Equal(0, layout.ArrowPoint.DistanceTo(new Vector(6.25, 3)), 12);
        Assert.Equal(0, layout.LineEnd.DistanceTo(new Vector(6.75, 3)), 12);
    }

    [Fact]
    public void Diameter_MeasuresTwiceTheRadius_WithTheLeaderAcrossTheCircle()
    {
        var hole = Hole(radius: 1.25);

        var layout = Layout(hole, DimensionKind.Diameter, Angle.HalfPI, 0.5);

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(2.5, layout.Value, 9);
        Assert.Equal("\u00D82.500", layout.DisplayText);
        Assert.Equal(0, layout.LineStart.DistanceTo(new Vector(5, 1.75)), 12);
        Assert.Equal(0, layout.ArrowPoint.DistanceTo(new Vector(5, 4.25)), 12);
        Assert.Equal(0, layout.LineEnd.DistanceTo(new Vector(5, 4.75)), 12);
    }

    [Fact]
    public void RadiusOnAnArc_StaysOnItsSweep()
    {
        // A quarter arc from 0 to 90 degrees; a leader asked for at 180 degrees snaps to the
        // nearer end, 90 degrees.
        var arc = new Arc(new Vector(0, 0), 2, 0, Angle.HalfPI) { Layer = Cut, Id = Guid.NewGuid() };

        var layout = Layout(arc, DimensionKind.Radius, System.Math.PI * 0.9);

        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(2, layout.Value, 9);
        Assert.Equal(Angle.HalfPI, layout.Angle, 9);
        Assert.Equal(0, layout.ArrowPoint.DistanceTo(new Vector(0, 2)), 9);
    }

    [Fact]
    public void AMissingCurve_IsNotShown()
    {
        var layout = DimensionBuilder.Layout([Hole()], new DrawingDimension
        {
            Kind = DimensionKind.Radius,
            EntityId = Guid.NewGuid(),
            Angle = 0,
            Offset = 1,
        });

        Assert.False(layout.IsValid);
        Assert.Contains("not cut", layout.Error);
    }

    [Fact]
    public void ALine_CannotBeMeasuredAsARadius()
    {
        var line = new Line(0, 0, 4, 0) { Layer = Cut, Id = Guid.NewGuid() };

        Assert.False(DimensionBuilder.IsRadialTarget(line));
        Assert.Empty(DimensionBuilder.MissingRadial([line], DimensionKind.Radius, [line], []));
        Assert.False(Layout(line, DimensionKind.Radius, 0).IsValid);
    }

    [Fact]
    public void MissingRadial_AddsOncePerCurveAndKind_OutsideTheCurve()
    {
        var hole = Hole(radius: 1);
        var arc = new Arc(new Vector(0, 0), 2, 0, System.Math.PI) { Layer = Cut, Id = Guid.NewGuid() };

        var first = DimensionBuilder.MissingRadial([hole, arc], DimensionKind.Radius, [hole, arc], []);

        Assert.Equal(2, first.Count);
        Assert.All(first, d => Assert.True(d.Offset > 0));
        Assert.Equal(Angle.HalfPI, first[1].Angle, 9); // The middle of the half arc.
        Assert.Empty(DimensionBuilder.MissingRadial([hole, arc], DimensionKind.Radius, [hole, arc], first));
        Assert.Equal(2, DimensionBuilder.MissingRadial([hole, arc], DimensionKind.Diameter, [hole, arc], first).Count);
    }

    [Fact]
    public void MissingRadial_OnAReversedArc_PointsThroughItsMiddle()
    {
        // Clockwise from 90 down to 0 degrees covers the first quadrant; its middle is 45.
        var arc = new Arc(new Vector(0, 0), 2, Angle.HalfPI, 0, reversed: true) { Layer = Cut, Id = Guid.NewGuid() };

        var added = Assert.Single(DimensionBuilder.MissingRadial([arc], DimensionKind.Radius, [arc], []));

        Assert.Equal(Angle.HalfPI / 2, added.Angle, 9);
    }

    [Theory]
    [InlineData(8, 3, 2, 0)]        // Outside to the right: 2 past the curve.
    [InlineData(5, 3.5, -0.5, 0)]   // Inside, near the center: kept the minimum clear of it.
    [InlineData(6.02, 3, 0.1, 0)]   // Hugging the curve: pushed the minimum out.
    public void Dragging_PutsTheLabelUnderTheCursor_KeptClearOfCurveAndCenter(double x, double y,
        double expectedOffset, double expectedAngle)
    {
        var layout = Layout(Hole(radius: 1), DimensionKind.Radius, 0);

        var (offset, angle) = layout.RadialPlacementAt(new Vector(x, y), 0.1);

        Assert.Equal(expectedOffset, offset, 9);
        if (y == 3)
            Assert.Equal(expectedAngle, angle, 9);
        else
            Assert.Equal(Angle.HalfPI, angle, 9);
    }

    [Fact]
    public void Dragging_ASmallHolesLabelInside_KeepsItOutsideInstead()
    {
        // Radius 0.15 with a 0.1 minimum: no inside spot is clear of both curve and center.
        var layout = Layout(Hole(radius: 0.15), DimensionKind.Diameter, 0);

        var (offset, _) = layout.RadialPlacementAt(new Vector(5.05, 3), 0.1);

        Assert.Equal(0.1, offset, 9);
    }

    [Fact]
    public void Clone_KeepsTheCurveAndAngle()
    {
        var id = Guid.NewGuid();
        var clone = new DrawingDimension { Kind = DimensionKind.Diameter, EntityId = id, Angle = 1.5, Offset = 2 }.Clone();

        Assert.Equal((DimensionKind.Diameter, id, 1.5, 2.0), (clone.Kind, clone.EntityId, clone.Angle, clone.Offset));
    }
}
