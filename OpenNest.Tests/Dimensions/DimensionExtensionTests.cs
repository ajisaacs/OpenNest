using OpenNest.Dimensions;
using OpenNest.Geometry;

namespace OpenNest.Tests.Dimensions;

public class DimensionExtensionTests
{
    private const double Clearance = 0.1;

    private static List<Entity> Loop(params Vector[] points)
    {
        var layer = new Layer("0") { IsVisible = true };
        var entities = new List<Entity>();
        for (var i = 0; i < points.Length; i++)
            entities.Add(new Line(points[i], points[(i + 1) % points.Length]) { Layer = layer });
        return entities;
    }

    private static DimensionLayout Horizontal(List<Entity> entities, double offset) =>
        DimensionBuilder.Layout(entities,
            new DrawingDimension { Kind = DimensionKind.OverallHorizontal, Offset = offset });

    [Fact]
    public void OnARectangle_TheLineStopsAtTheOffsetEdge()
    {
        var layout = Horizontal(Loop(new(0, 0), new(10, 0), new(10, 6), new(0, 6)), -1);

        var tip = layout.ExtensionTip(layout.LineStart, Clearance);

        Assert.Equal(0, tip.X, 6);
        Assert.Equal(-Clearance, tip.Y, 1);
    }

    [Fact]
    public void OnADiamond_TheLineFollowsTheExtentsUntilItMeetsTheOffsetProfile()
    {
        var part = Loop(new(5, 0), new(10, 3), new(5, 6), new(0, 3));
        var layout = Horizontal(part, -1);

        var tip = layout.ExtensionTip(layout.LineStart, Clearance);

        Assert.Equal(0, tip.X, 6);
        Assert.InRange(tip.Y, 0, 3);
        var distance = part.Min(e => tip.DistanceTo(e.ClosestPointTo(tip)));
        Assert.InRange(distance, Clearance, Clearance * 1.06);
    }

    [Fact]
    public void AFootAlreadyWithinTheClearance_StaysPut()
    {
        var layout = Horizontal(Loop(new(0, 0), new(10, 0), new(10, 6), new(0, 6)), -0.05);

        var tip = layout.ExtensionTip(layout.LineStart, Clearance);

        Assert.Equal(layout.LineStart.Y, tip.Y, 6);
    }
}
