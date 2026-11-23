using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.Labels;

namespace OpenNest.Tests.Labels;

public class LabelBoxFinderTests
{
    private static List<Entity> Rectangle(double left, double bottom, double right, double top) =>
    [
        new Line(left, bottom, right, bottom), new Line(right, bottom, right, top),
        new Line(right, top, left, top), new Line(left, top, left, bottom),
    ];

    // A width x height rectangle turned about the origin and moved to (cx, cy).
    private static List<Entity> Turned(double width, double height, double degrees, double cx, double cy)
    {
        var angle = degrees * System.Math.PI / 180;
        Vector P(double x, double y) => new(
            cx + x * System.Math.Cos(angle) - y * System.Math.Sin(angle),
            cy + x * System.Math.Sin(angle) + y * System.Math.Cos(angle));
        var corners = new[] { P(-width / 2, -height / 2), P(width / 2, -height / 2), P(width / 2, height / 2), P(-width / 2, height / 2) };
        return corners.Select((c, i) => (Entity)new Line(c, corners[(i + 1) % 4])).ToList();
    }

    private static LabelBoxOptions Options(double width = 2, double height = 0.5, double clearance = 0.25) =>
        new() { Width = width, Height = height, Clearance = clearance };

    [Fact]
    public void FlatPart_GetsAnAxisAlignedBoxInsideTheClearance()
    {
        var box = LabelBoxFinder.Find(Rectangle(0, 0, 10, 5), [], Options());

        Assert.NotNull(box);
        Assert.Equal(0, box.Rotation, 9);
        Assert.Equal(2, box.Width, 9);
        Assert.Equal(0.5, box.Height, 9);
        foreach (var corner in box.Corners())
        {
            Assert.InRange(corner.X, 0.25 - 1e-6, 9.75 + 1e-6);
            Assert.InRange(corner.Y, 0.25 - 1e-6, 4.75 + 1e-6);
        }
    }

    [Fact]
    public void Hole_IsAvoided()
    {
        var entities = Rectangle(0, 0, 10, 5);
        entities.Add(new Circle(5, 2.5, 2));

        var box = LabelBoxFinder.Find(entities, [], Options());

        Assert.NotNull(box);
        foreach (var corner in box.Corners())
            Assert.True(corner.DistanceTo(new Vector(5, 2.5)) >= 2.25 - 1e-3, $"{corner} is too close to the hole");
        Assert.False(box.Contains(new Vector(5, 2.5)));
    }

    [Fact]
    public void BendLine_IsKeptClearOnBothSides()
    {
        var bend = new Bend { StartPoint = new Vector(5, 0), EndPoint = new Vector(5, 5) };

        var box = LabelBoxFinder.Find(Rectangle(0, 0, 10, 5), [bend], Options());

        Assert.NotNull(box);
        var xs = box.Corners().Select(c => c.X).ToList();
        Assert.True(xs.Max() <= 5 - 0.25 + 1e-3 || xs.Min() >= 5 + 0.25 - 1e-3,
            $"The box spans the bend: x from {xs.Min()} to {xs.Max()}");
    }

    [Fact]
    public void BendClearance_CanDifferFromTheEdgeClearance()
    {
        var bend = new Bend { StartPoint = new Vector(5, 0), EndPoint = new Vector(5, 5) };
        var options = Options(clearance: 0.25);
        options.BendClearance = 1;

        var box = LabelBoxFinder.Find(Rectangle(0, 0, 10, 5), [bend], options);

        Assert.NotNull(box);
        var xs = box.Corners().Select(c => c.X).ToList();
        Assert.True(xs.Max() <= 4 + 1e-3 || xs.Min() >= 6 - 1e-3);
    }

    [Fact]
    public void Gusset_GetsAHorizontalBoxInTheOpenPocket_NotBesideTheSlant()
    {
        // The hole and the slanted edge leave a long, narrow diagonal strip between them, and a
        // roomier pocket at the bottom right: the label belongs in the pocket, reading level.
        List<Entity> gusset =
        [
            new Line(0, 0, 8, 0), new Line(8, 0, 8, 1), new Line(8, 1, 1, 8),
            new Line(1, 8, 0, 8), new Line(0, 8, 0, 0), new Circle(2.6, 2.6, 1.1),
        ];

        var box = LabelBoxFinder.Find(gusset, [], LabelBoxOptions.Default(Units.Inches));

        Assert.NotNull(box);
        Assert.Equal(0, box.Rotation, 9);
        Assert.InRange(box.Center.X, 4.6, 6.5);
        Assert.InRange(box.Center.Y, 1.1, 1.7);
        foreach (var corner in box.Corners())
        {
            Assert.True(corner.DistanceTo(new Vector(2.6, 2.6)) >= 1.1 + 0.125 - 1e-3, $"{corner} is too close to the hole");
            Assert.True((9 - corner.X - corner.Y) / System.Math.Sqrt(2) >= 0.125 - 1e-3, $"{corner} is too close to the slant");
            Assert.InRange(corner.X, 0.125 - 1e-3, 7.875 + 1e-3);
            Assert.InRange(corner.Y, 0.125 - 1e-3, 7.875 + 1e-3);
        }
    }

    [Fact]
    public void KeepOutOutline_IsAvoided_WithTheClearance()
    {
        // The left half is breakoff scrap: the box must stay in the right half, clear of the joint.
        var scrap = Rectangle(0, 0, 5, 5);

        var box = LabelBoxFinder.Find(Rectangle(0, 0, 10, 5), [], Options(), [scrap]);

        Assert.NotNull(box);
        Assert.True(box.Corners().All(c => c.X >= 5 + 0.25 - 1e-3), "The box reaches into the scrap or its clearance");
    }

    [Fact]
    public void KeepOutThatLeavesNoRoom_GivesNoBox()
    {
        Assert.Null(LabelBoxFinder.Find(Rectangle(0, 0, 10, 5), [], Options(), [Rectangle(0, 0, 9, 5)]));
    }

    [Fact]
    public void PartTooSmall_GivesNoBox()
    {
        Assert.Null(LabelBoxFinder.Find(Rectangle(0, 0, 3, 1), [], Options(clearance: 0.5)));
        Assert.Null(LabelBoxFinder.Find(Rectangle(0, 0, 10, 5), [], Options(width: 20)));
    }

    [Fact]
    public void BendsThatLeaveNoRoom_GiveNoBox()
    {
        var bends = Enumerable.Range(1, 9)
            .Select(i => new Bend { StartPoint = new Vector(i, 0), EndPoint = new Vector(i, 5) })
            .ToList();

        Assert.Null(LabelBoxFinder.Find(Rectangle(0, 0, 10, 5), bends, Options()));
    }

    [Fact]
    public void NarrowDiagonalPart_GetsABoxAlongItsLength()
    {
        var part = Turned(20, 1.5, 30, 10, 10);

        var box = LabelBoxFinder.Find(part, [], Options(width: 3, height: 0.5, clearance: 0.2));

        Assert.NotNull(box);
        Assert.Equal(30 * System.Math.PI / 180, box.Rotation, 2);
        foreach (var corner in box.Corners())
        {
            // The part's own frame: x along its length, y across it.
            var dx = corner.X - 10;
            var dy = corner.Y - 10;
            var along = dx * System.Math.Cos(30 * System.Math.PI / 180) + dy * System.Math.Sin(30 * System.Math.PI / 180);
            var across = -dx * System.Math.Sin(30 * System.Math.PI / 180) + dy * System.Math.Cos(30 * System.Math.PI / 180);
            Assert.True(System.Math.Abs(along) <= 10 - 0.2 + 1e-2);
            Assert.True(System.Math.Abs(across) <= 0.75 - 0.2 + 1e-2, $"{corner} is outside the part's clearance");
        }
    }

    [Fact]
    public void BendDirection_IsTriedAndText_NeverReadsUpsideDown()
    {
        var part = Turned(20, 1.5, 120, 0, 0);
        var bend = new Bend { StartPoint = new Vector(0, 0), EndPoint = new Vector(System.Math.Cos(120 * System.Math.PI / 180), System.Math.Sin(120 * System.Math.PI / 180)) };

        var box = LabelBoxFinder.Find(part, [bend], Options(width: 3, height: 0.5, clearance: 0.2));

        Assert.NotNull(box);
        Assert.InRange(box.Rotation, -System.Math.PI / 2 - 1e-9, System.Math.PI / 2 + 1e-9);
    }

    [Fact]
    public void Marks_AreNotMaterialAndDoNotBlockTheBox()
    {
        var entities = Rectangle(0, 0, 10, 5);
        var plain = LabelBoxFinder.Find(entities, [], Options());
        entities.Add(new Line(0, 2.5, 10, 2.5) { Layer = SpecialLayers.Scribe });

        var marked = LabelBoxFinder.Find(entities, [], Options());

        Assert.NotNull(marked);
        Assert.Equal(plain.Center, marked.Center);
    }

    [Fact]
    public void Result_IsDeterministic()
    {
        var entities = Rectangle(0, 0, 10, 5);
        entities.Add(new Circle(3, 2.5, 1));
        var bends = new List<Bend> { new() { StartPoint = new Vector(7, 0), EndPoint = new Vector(7, 5) } };

        var a = LabelBoxFinder.Find(entities, bends, Options());
        var b = LabelBoxFinder.Find(entities, bends, Options());

        Assert.NotNull(a);
        Assert.Equal(a.Center, b.Center);
        Assert.Equal(a.Rotation, b.Rotation);
    }

    [Fact]
    public void NoClosedGeometry_GivesNoBox()
    {
        Assert.Null(LabelBoxFinder.Find([new Line(0, 0, 10, 0)], [], Options()));
        Assert.Null(LabelBoxFinder.Find([], [], Options()));
    }

    [Fact]
    public void BadOptions_Throw()
    {
        Assert.Throws<ArgumentException>(() => LabelBoxFinder.Find(Rectangle(0, 0, 10, 5), [], Options(width: 0)));
        Assert.Throws<ArgumentException>(() => LabelBoxFinder.Find(Rectangle(0, 0, 10, 5), [], Options(clearance: -1)));
    }

    [Fact]
    public void ForText_SizesTheBoxFromTheCharacters()
    {
        var options = LabelBoxOptions.ForText(7, 0.25, 0.125);

        Assert.Equal(7 * 0.25 * LabelBoxOptions.CharacterAdvance, options.Width, 9);
        Assert.Equal(0.25, options.Height, 9);
        Assert.Equal(0.125, options.Clearance, 9);
        Assert.True(LabelBoxOptions.Default(Units.Millimeters).Height > LabelBoxOptions.Default(Units.Inches).Height);
    }

    [Fact]
    public void LabelBox_ConvertsBetweenItsOwnFrameAndTheDrawingFrame()
    {
        var box = new LabelBox(new Vector(10, 20), 4, 2, System.Math.PI / 2);

        Assert.Equal(new Vector(10, 22), box.ToWorld(new Vector(2, 0)).Round());
        Assert.Equal(2, box.ToLocal(new Vector(10, 22)).X, 9);
        Assert.True(box.Contains(new Vector(10, 21.5)));
        Assert.False(box.Contains(new Vector(12, 20)));
        Assert.Equal(4, box.Corners().Length);
    }
}

file static class VectorExtensions
{
    public static Vector Round(this Vector v) => new(System.Math.Round(v.X, 6), System.Math.Round(v.Y, 6));
}
