using System.Collections.Generic;
using OpenNest.Engine.RapidPlanning;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.RapidPlanning;

public class SafeHeightRapidPlannerTests
{
    [Fact]
    public void AlwaysReturnsHeadUp()
    {
        var planner = new SafeHeightRapidPlanner();
        var from = new Vector(10, 10);
        var to = new Vector(50, 50);
        var cutAreas = new List<Shape>();

        var result = planner.Plan(from, to, cutAreas);

        Assert.True(result.HeadUp);
        Assert.Empty(result.Waypoints);
    }

    [Fact]
    public void ReturnsHeadUp_EvenWithCutAreas()
    {
        var planner = new SafeHeightRapidPlanner();
        var from = new Vector(0, 0);
        var to = new Vector(10, 10);

        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(5, 0), new Vector(5, 20)));
        var cutAreas = new List<Shape> { shape };

        var result = planner.Plan(from, to, cutAreas);

        Assert.True(result.HeadUp);
    }
}
