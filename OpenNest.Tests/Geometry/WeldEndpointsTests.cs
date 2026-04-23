using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests.Geometry;

public class WeldEndpointsTests
{
    [Fact]
    public void WeldEndpoints_SnapsNearbyLineEndpoints()
    {
        var line1 = new Line(0, 0, 10, 0);
        var line2 = new Line(10.0000005, 0, 20, 0);
        var entities = new List<Entity> { line1, line2 };

        ShapeBuilder.WeldEndpoints(entities, 0.000001);

        Assert.True(line1.EndPoint.DistanceTo(line2.StartPoint) <= Tolerance.Epsilon);
    }

    [Fact]
    public void WeldEndpoints_SnapsArcEndpointByAdjustingAngle()
    {
        var line = new Line(0, 0, 10, 0);
        var arc = new Arc(15, 0, 5, Angle.ToRadians(180.001), Angle.ToRadians(90));
        var entities = new List<Entity> { line, arc };

        ShapeBuilder.WeldEndpoints(entities, 0.01);

        var arcStart = arc.StartPoint();
        Assert.True(line.EndPoint.DistanceTo(arcStart) <= 0.01);
    }

    [Fact]
    public void WeldEndpoints_DoesNotWeldDistantEndpoints()
    {
        var line1 = new Line(0, 0, 10, 0);
        var line2 = new Line(10.1, 0, 20, 0);
        var entities = new List<Entity> { line1, line2 };

        ShapeBuilder.WeldEndpoints(entities, 0.000001);

        Assert.True(line1.EndPoint.DistanceTo(line2.StartPoint) > 0.01);
    }

    [Fact]
    public void GetShapes_WithWeldTolerance_WeldsBeforeChaining()
    {
        var line1 = new Line(0, 0, 10, 0);
        var line2 = new Line(10.0000005, 0, 10.0000005, 10);
        var entities = new List<Entity> { line1, line2 };

        var shapes = ShapeBuilder.GetShapes(entities, weldTolerance: 0.000001);

        Assert.Single(shapes);
        Assert.Equal(2, shapes[0].Entities.Count);
    }

    [Fact]
    public void GetShapes_WithoutWeldTolerance_DefaultBehavior()
    {
        var line1 = new Line(0, 0, 10, 0);
        var line2 = new Line(10, 0, 10, 10);
        var entities = new List<Entity> { line1, line2 };

        var shapes = ShapeBuilder.GetShapes(entities);

        Assert.Single(shapes);
        Assert.Equal(2, shapes[0].Entities.Count);
    }
}
