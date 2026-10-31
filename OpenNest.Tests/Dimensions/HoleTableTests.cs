using OpenNest.Dimensions;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Dimensions;

public class HoleTableTests
{
    private static readonly Layer Cut = new("0") { IsVisible = true };

    private static List<Entity> Rectangle(double width, double height) =>
    [
        new Line(0, 0, width, 0) { Layer = Cut },
        new Line(width, 0, width, height) { Layer = Cut },
        new Line(width, height, 0, height) { Layer = Cut },
        new Line(0, height, 0, 0) { Layer = Cut },
    ];

    private static Circle Hole(double x, double y, double diameter, Layer layer = null) =>
        new(x, y, diameter / 2) { Layer = layer ?? Cut };

    [Fact]
    public void Build_GroupsHolesByDiameter_SmallestFirst_WithEveryEntityId()
    {
        var a = Hole(1, 1, 0.5);
        var b = Hole(3, 1, 0.5);
        var c = Hole(5, 1, 0.25);
        var entities = Rectangle(10, 4);
        entities.AddRange([a, b, c]);

        var table = HoleTable.Build(entities);

        Assert.Equal(2, table.Count);
        Assert.Equal(0.25, table[0].Diameter, 12);
        Assert.Equal(1, table[0].Quantity);
        Assert.Equal([c.Id], table[0].EntityIds);
        Assert.Equal(0.5, table[1].Diameter, 12);
        Assert.Equal(2, table[1].Quantity);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), table[1].EntityIds.Order());
        Assert.Equal("\u00D80.5000", table[1].DiameterText);
    }

    [Fact]
    public void Build_CountsAHoleDrawnAsTwoArcs_AsOneHole()
    {
        var top = new Arc(5, 2, 0.375, 0, Angle.ToRadians(180)) { Layer = Cut };
        var bottom = new Arc(5, 2, 0.375, Angle.ToRadians(180), Angle.TwoPI) { Layer = Cut };
        var entities = Rectangle(10, 4);
        entities.AddRange([top, bottom]);

        var table = HoleTable.Build(entities);

        var size = Assert.Single(table);
        Assert.Equal(0.75, size.Diameter, 12);
        Assert.Equal(1, size.Quantity);
        Assert.Equal(new[] { top.Id, bottom.Id }.Order(), size.EntityIds.Order());
    }

    [Fact]
    public void Build_SharesARow_WhenDiametersAgreeToFourPlaces()
    {
        var entities = Rectangle(10, 4);
        entities.AddRange([Hole(1, 1, 0.25001), Hole(3, 1, 0.24999), Hole(5, 1, 0.2502)]);

        var table = HoleTable.Build(entities);

        Assert.Equal([2, 1], table.Select(s => s.Quantity));
        Assert.Equal(0.25, table[0].Diameter, 12);
        Assert.Equal(0.2502, table[1].Diameter, 12);
    }

    [Fact]
    public void Build_DoesNotCountTheOutlineOfARoundPart_OrMarks_OrSlots()
    {
        var outline = Hole(0, 0, 6);
        var hole = Hole(0, 0, 1);
        var etched = Hole(1.5, 0, 0.5, SpecialLayers.Scribe);
        var slot = new List<Entity>
        {
            new Arc(-2, 1, 0.25, Angle.ToRadians(90), Angle.ToRadians(270)) { Layer = Cut },
            new Line(-2, 0.75, -1, 0.75) { Layer = Cut },
            new Arc(-1, 1, 0.25, Angle.ToRadians(270), Angle.ToRadians(90)) { Layer = Cut },
            new Line(-1, 1.25, -2, 1.25) { Layer = Cut },
        };
        var entities = new List<Entity> { outline, hole, etched };
        entities.AddRange(slot);

        var table = HoleTable.Build(entities);

        var size = Assert.Single(table);
        Assert.Equal(1, size.Diameter, 12);
        Assert.Equal([hole.Id], size.EntityIds);
    }

    [Fact]
    public void Build_IsEmpty_WithNoGeometry()
    {
        Assert.Empty(HoleTable.Build([]));
        Assert.Empty(HoleTable.Build(Rectangle(4, 4)));
    }
}
