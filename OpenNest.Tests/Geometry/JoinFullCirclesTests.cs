using OpenNest.Dimensions;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

public class JoinFullCirclesTests
{
    private static readonly Layer Cut = new("0") { IsVisible = true };

    private static Arc ArcDeg(double x, double y, double radius, double start, double end, Layer layer = null) =>
        new(x, y, radius, Angle.ToRadians(start), Angle.ToRadians(end)) { Layer = layer ?? Cut };

    private static Vector OnCircle(double x, double y, double radius, double degrees)
    {
        var angle = Angle.ToRadians(degrees);
        return new Vector(x + radius * System.Math.Cos(angle), y + radius * System.Math.Sin(angle));
    }

    // Two arcs overshooting the seam by the given degrees, then a chord back to the start.
    private static List<Entity> OvershotHole(double x, double y, double radius, double seam, double overshoot) =>
    [
        ArcDeg(x, y, radius, seam, seam + 180),
        ArcDeg(x + 8e-8, y - 5e-8, radius + 8e-8, seam + 180, seam + overshoot),
        new Line(OnCircle(x, y, radius, seam + overshoot), OnCircle(x, y, radius, seam)) { Layer = Cut },
    ];

    [Theory]
    [InlineData(210, 5.5)]
    [InlineData(120, 1.14592)]
    [InlineData(300, 5.5)]
    [InlineData(0, 5.5)]
    public void Joins_OvershotArcs_AndDropsTheirChord(double seam, double overshoot)
    {
        var entities = OvershotHole(2, 3, 0.3125, seam, overshoot);

        GeometryOptimizer.JoinFullCircles(entities);

        var circle = Assert.IsType<Circle>(Assert.Single(entities));
        Assert.Equal(2, circle.Center.X, 5);
        Assert.Equal(3, circle.Center.Y, 5);
        Assert.Equal(0.3125, circle.Radius, 5);
        Assert.Same(Cut, circle.Layer);
    }

    [Fact]
    public void Joins_OverlappingArcs_WithNoChord()
    {
        var entities = new List<Entity> { ArcDeg(0, 0, 1, 210, 30), ArcDeg(0, 0, 1, 30, 215.5) };

        GeometryOptimizer.JoinFullCircles(entities);

        Assert.IsType<Circle>(Assert.Single(entities));
    }

    [Fact]
    public void Joins_ArcsThatMeetEndToEnd_InAnyOrderAndDirection()
    {
        var reversed = ArcDeg(0, 0, 1, 120, 240);
        reversed.Reverse();
        var entities = new List<Entity> { ArcDeg(0, 0, 1, 240, 0), reversed, ArcDeg(0, 0, 1, 0, 120) };

        GeometryOptimizer.JoinFullCircles(entities);

        Assert.IsType<Circle>(Assert.Single(entities));
    }

    [Fact]
    public void Joins_WhenAWrappingArcBridgesTheFirstGap()
    {
        var entities = new List<Entity>
        {
            ArcDeg(0, 0, 1, 10, 30),
            ArcDeg(0, 0, 1, 40, 300),
            ArcDeg(0, 0, 1, 300, 40),
        };

        GeometryOptimizer.JoinFullCircles(entities);

        Assert.IsType<Circle>(Assert.Single(entities));
    }

    [Fact]
    public void LeavesArcs_ThatDoNotCloseTheCircle()
    {
        var entities = new List<Entity> { ArcDeg(0, 0, 1, 0, 170), ArcDeg(0, 0, 1, 180, 350) };

        GeometryOptimizer.JoinFullCircles(entities);

        Assert.Equal(2, entities.Count);
        Assert.All(entities, e => Assert.IsType<Arc>(e));
    }

    [Fact]
    public void LeavesDuplicateArcs_WhoseSweepsOnlySumPastACircle()
    {
        var entities = new List<Entity> { ArcDeg(0, 0, 1, 0, 200), ArcDeg(0, 0, 1, 0, 200) };

        GeometryOptimizer.JoinFullCircles(entities);

        Assert.Equal(2, entities.Count);
    }

    [Fact]
    public void LeavesArcs_OverlappingPastTheSeamLimit()
    {
        var entities = new List<Entity> { ArcDeg(0, 0, 1, 0, 200), ArcDeg(0, 0, 1, 170, 40) };

        GeometryOptimizer.JoinFullCircles(entities);

        Assert.Equal(2, entities.Count);
    }

    [Fact]
    public void LeavesArcs_OnDifferentLayers_OrNotOfMaterial()
    {
        var other = new Layer("OTHER") { IsVisible = true };
        var split = new List<Entity> { ArcDeg(0, 0, 1, 0, 180), ArcDeg(0, 0, 1, 180, 0, other) };
        var marks = new List<Entity>
        {
            ArcDeg(0, 0, 1, 0, 180, SpecialLayers.Scribe),
            ArcDeg(0, 0, 1, 180, 0, SpecialLayers.Scribe),
        };

        GeometryOptimizer.JoinFullCircles(split);
        GeometryOptimizer.JoinFullCircles(marks);

        Assert.Equal(2, split.Count);
        Assert.Equal(2, marks.Count);
    }

    [Fact]
    public void KeepsLines_OffTheOverlap()
    {
        var elsewhere = new Line(OnCircle(0, 0, 1, 80), OnCircle(0, 0, 1, 100)) { Layer = Cut };
        var outside = new Line(OnCircle(0, 0, 1, 210), new Vector(-5, -5)) { Layer = Cut };
        var entities = OvershotHole(0, 0, 1, 210, 5.5);
        entities.AddRange([elsewhere, outside]);

        GeometryOptimizer.JoinFullCircles(entities);

        Assert.Equal(3, entities.Count);
        Assert.Contains(elsewhere, entities);
        Assert.Contains(outside, entities);
        Assert.Single(entities.OfType<Circle>());
    }

    [Fact]
    public void HoleTable_ListsTheJoinedHoles()
    {
        var entities = new List<Entity> { new Circle(0, 0, 5.5) { Layer = Cut } };
        entities.AddRange(OvershotHole(0, 0, 1.5, 120, 1.14592));
        for (var i = 0; i < 8; i++)
        {
            var center = OnCircle(0, 0, 4.75, i * 45);
            entities.AddRange(OvershotHole(center.X, center.Y, 0.3125, 30 + i * 45, 5.5));
        }

        Assert.Empty(HoleTable.Build(entities));

        GeometryOptimizer.JoinFullCircles(entities);

        var table = HoleTable.Build(entities);
        Assert.Equal(2, table.Count);
        Assert.Equal((0.625, 8), (table[0].Diameter, table[0].Quantity));
        Assert.Equal((3.0, 1), (table[1].Diameter, table[1].Quantity));
    }
}
