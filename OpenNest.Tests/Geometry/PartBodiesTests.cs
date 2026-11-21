using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class PartBodiesTests
{
    private static List<Entity> Rect(double x, double y, double w, double h) =>
    [
        new Line(x, y, x + w, y),
        new Line(x + w, y, x + w, y + h),
        new Line(x + w, y + h, x, y + h),
        new Line(x, y + h, x, y),
    ];

    [Fact]
    public void OnePart_WithHoles_IsOneBody()
    {
        var entities = Rect(0, 0, 10, 10);
        entities.Add(new Circle(new Vector(3, 3), 1));
        entities.AddRange(Rect(6, 6, 2, 2));

        var bodies = PartBodies.Find(entities);

        Assert.Equal(1, bodies.Count);
        Assert.Single(bodies.Group(entities));
    }

    [Fact]
    public void SideBySideParts_AreSeparate_EachKeepingItsHole()
    {
        var left = Rect(0, 0, 10, 10);
        var leftHole = new Circle(new Vector(5, 8), 0.5);
        var right = Rect(20, 0, 5, 12);
        var rightHole = new Circle(new Vector(22.5, 10), 0.5);
        var entities = right.Concat(left).Append(rightHole).Append(leftHole).ToList();

        var bodies = PartBodies.Find(entities);
        var groups = bodies.Group(entities);

        Assert.Equal(2, bodies.Count);
        // Reading order: the taller right part's top is higher, but both share a row, so left first.
        Assert.Equal(left.Append(leftHole), groups[0]);
        Assert.Equal(right.Append(rightHole), groups[1]);
    }

    [Fact]
    public void PartInsideAHole_IsItsOwnPart()
    {
        var ring = Rect(0, 0, 20, 20);
        var hole = Rect(5, 5, 10, 10);
        var island = Rect(8, 8, 4, 4);
        var islandHole = new Circle(new Vector(10, 10), 0.5);
        var entities = ring.Concat(hole).Concat(island).Append(islandHole).ToList();

        var bodies = PartBodies.Find(entities);
        var groups = bodies.Group(entities);

        Assert.Equal(2, bodies.Count);
        Assert.Contains(groups, g => g.SequenceEqual(ring.Concat(hole)));
        Assert.Contains(groups, g => g.SequenceEqual(island.Append(islandHole)));
    }

    [Fact]
    public void Marks_GoToThePartAroundThem_OrTheNearestOne()
    {
        var a = Rect(0, 0, 10, 10);
        var b = Rect(20, 0, 10, 10);
        var etch = new Line(2, 5, 8, 5) { Layer = new Layer("ETCH") };
        var stray = new Line(31, 5, 33, 5);
        var bodies = PartBodies.Find(a.Concat(b));

        Assert.Equal(0, bodies.IndexOf(etch));
        Assert.Equal(1, bodies.IndexOf(stray));
        Assert.Equal(1, bodies.IndexOf(new Vector(25, 5)));
    }

    [Fact]
    public void OpenGeometry_IsNotAPart()
    {
        var entities = Rect(0, 0, 10, 10);
        entities.Add(new Line(20, 0, 30, 0));
        entities.Add(new Line(30, 0, 30, 10));

        Assert.Equal(1, PartBodies.Find(entities).Count);
    }

    [Fact]
    public void Rows_ReadTopToBottom_LeftToRight()
    {
        var bottomLeft = Rect(0, 0, 5, 5);
        var topRight = Rect(10, 10, 5, 5);
        var topLeft = Rect(0, 10, 5, 5);
        var bodies = PartBodies.Find(bottomLeft.Concat(topRight).Concat(topLeft));

        Assert.Equal(0, bodies.IndexOf(topLeft[0]));
        Assert.Equal(1, bodies.IndexOf(topRight[0]));
        Assert.Equal(2, bodies.IndexOf(bottomLeft[0]));
    }

    [Fact]
    public void NameOf_TakesTheSmallestLabelHoldingOnlyThatPart()
    {
        var bodies = PartBodies.Find(Rect(0, 0, 10, 10).Concat(Rect(20, 0, 10, 10)));
        var labels = new[]
        {
            new LabeledRegion("everything", new Box(0, 0, 30, 10)),
            new LabeledRegion(" star ", new Box(0, 0, 10, 10)),
            new LabeledRegion("too small", new Box(1, 1, 2, 2)),
        };

        Assert.Equal("star", bodies.NameOf(0, labels));
        Assert.Null(bodies.NameOf(1, labels));
        Assert.Null(bodies.NameOf(0, null));
    }
}
