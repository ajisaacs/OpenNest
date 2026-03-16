using System.Collections.Generic;
using OpenNest.Engine.RapidPlanning;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.RapidPlanning;

public class DirectRapidPlannerTests
{
    [Fact]
    public void NoCutAreas_ReturnsHeadDown()
    {
        var planner = new DirectRapidPlanner();
        var result = planner.Plan(new Vector(0, 0), new Vector(10, 10), new List<Shape>());

        Assert.False(result.HeadUp);
        Assert.Empty(result.Waypoints);
    }

    [Fact]
    public void ClearPath_ReturnsHeadDown()
    {
        var planner = new DirectRapidPlanner();

        var cutArea = new Shape();
        cutArea.Entities.Add(new Line(new Vector(50, 0), new Vector(50, 10)));
        cutArea.Entities.Add(new Line(new Vector(50, 10), new Vector(60, 10)));
        cutArea.Entities.Add(new Line(new Vector(60, 10), new Vector(60, 0)));
        cutArea.Entities.Add(new Line(new Vector(60, 0), new Vector(50, 0)));

        var result = planner.Plan(
            new Vector(0, 0), new Vector(10, 10),
            new List<Shape> { cutArea });

        Assert.False(result.HeadUp);
    }

    [Fact]
    public void BlockedPath_ReturnsHeadUp()
    {
        var planner = new DirectRapidPlanner();

        var cutArea = new Shape();
        cutArea.Entities.Add(new Line(new Vector(5, 0), new Vector(5, 20)));
        cutArea.Entities.Add(new Line(new Vector(5, 20), new Vector(6, 20)));
        cutArea.Entities.Add(new Line(new Vector(6, 20), new Vector(6, 0)));
        cutArea.Entities.Add(new Line(new Vector(6, 0), new Vector(5, 0)));

        var result = planner.Plan(
            new Vector(0, 10), new Vector(10, 10),
            new List<Shape> { cutArea });

        Assert.True(result.HeadUp);
        Assert.Empty(result.Waypoints);
    }
}
