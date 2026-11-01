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

    // A circle with a square key cut outward at the right.
    private static List<Entity> KeyedHole(double x, double y, double diameter, double keyWidth)
    {
        var radius = diameter / 2;
        var half = keyWidth / 2;
        var angle = System.Math.Asin(half / radius);
        var shoulder = System.Math.Sqrt(radius * radius - half * half);
        var tip = radius + half;
        return
        [
            new Arc(x, y, radius, angle, Angle.TwoPI - angle) { Layer = Cut },
            new Line(x + shoulder, y - half, x + tip, y - half) { Layer = Cut },
            new Line(x + tip, y - half, x + tip, y + half) { Layer = Cut },
            new Line(x + tip, y + half, x + shoulder, y + half) { Layer = Cut },
        ];
    }

    // A round hole with one flat at the right, leaving the given arc in degrees.
    private static List<Entity> FlattedHole(double x, double y, double diameter, double arcDegrees)
    {
        var radius = diameter / 2;
        var start = Angle.ToRadians((360 - arcDegrees) / 2);
        var cos = System.Math.Cos(start);
        var sin = System.Math.Sin(start);
        return
        [
            new Arc(x, y, radius, start, Angle.TwoPI - start) { Layer = Cut },
            new Line(x + radius * cos, y - radius * sin, x + radius * cos, y + radius * sin) { Layer = Cut },
        ];
    }

    [Fact]
    public void Build_ListsAKeyedHole_InItsOwnRowAfterRoundHolesOfThatDiameter()
    {
        var round = Hole(1, 2, 1);
        var keyed = KeyedHole(5, 2, 1, 0.2);
        var entities = Rectangle(10, 4);
        entities.Add(round);
        entities.AddRange(keyed);

        var table = HoleTable.Build(entities);

        Assert.Equal(2, table.Count);
        Assert.False(table[0].IsKeyed);
        Assert.Equal("Ø1.0000", table[0].DiameterText);
        Assert.True(table[1].IsKeyed);
        Assert.Equal(1, table[1].Diameter, 12);
        Assert.Equal("Ø1.0000 keyed", table[1].DiameterText);
        Assert.Equal(5, table[1].Centers[0].X, 12);
        Assert.Equal(2, table[1].Centers[0].Y, 12);
        Assert.Equal(keyed.Select(e => e.Id).Order(), table[1].EntityIds.Order());
    }

    [Fact]
    public void Build_CountsAKeyedHoleWhoseBoreIsSplitIntoArcs_AsOneHole()
    {
        var keyed = KeyedHole(5, 2, 1, 0.2);
        var bore = (Arc)keyed[0];
        var split = new List<Entity>
        {
            new Arc(5, 2, 0.5, bore.StartAngle, System.Math.PI) { Layer = Cut },
            new Arc(5, 2, 0.5, System.Math.PI, bore.EndAngle) { Layer = Cut },
        };
        split.AddRange(keyed.Skip(1));
        var entities = Rectangle(10, 4);
        entities.AddRange(split);

        var size = Assert.Single(HoleTable.Build(entities));

        Assert.True(size.IsKeyed);
        Assert.Equal(1, size.Quantity);
        Assert.Equal(1, size.Diameter, 12);
        Assert.Equal(5, size.EntityIds.Count);
    }

    [Fact]
    public void Build_ListsAFlattedHole_DownTo270DegreesOfArc()
    {
        var entities = Rectangle(10, 4);
        entities.AddRange(FlattedHole(2, 2, 1, 270));
        entities.AddRange(FlattedHole(6, 2, 0.5, 260));

        var size = Assert.Single(HoleTable.Build(entities));

        Assert.True(size.IsKeyed);
        Assert.Equal(1, size.Diameter, 12);
    }

    [Fact]
    public void Build_DoesNotCountACurvedSlot_OrAKeyedOutline()
    {
        // A slot wrapping 280 degrees around solid material.
        const double cx = 5, cy = 2, outer = 1, inner = 0.75;
        var a = Angle.ToRadians(40);
        var b = Angle.ToRadians(320);
        var slotted = Rectangle(10, 4);
        slotted.AddRange(
        [
            new Arc(cx, cy, outer, a, b) { Layer = Cut },
            new Line(cx + outer * System.Math.Cos(b), cy + outer * System.Math.Sin(b),
                cx + inner * System.Math.Cos(b), cy + inner * System.Math.Sin(b)) { Layer = Cut },
            new Arc(cx, cy, inner, b, a, reversed: true) { Layer = Cut },
            new Line(cx + inner * System.Math.Cos(a), cy + inner * System.Math.Sin(a),
                cx + outer * System.Math.Cos(a), cy + outer * System.Math.Sin(a)) { Layer = Cut },
        ]);

        Assert.Empty(HoleTable.Build(slotted));
        Assert.Empty(HoleTable.Build(KeyedHole(5, 2, 3, 0.5)));
    }

    [Fact]
    public void Build_IsEmpty_WithNoGeometry()
    {
        Assert.Empty(HoleTable.Build([]));
        Assert.Empty(HoleTable.Build(Rectangle(4, 4)));
    }
}
