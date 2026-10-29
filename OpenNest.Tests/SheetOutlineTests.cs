using Clipper2Lib;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests;

/// <summary>
/// <see cref="SheetOutline"/>: the material of a non-rectangular sheet, and what a plate, its
/// cut-offs and its remnant do with one.
/// </summary>
public class SheetOutlineTests
{
    /// <summary>A 96 x 48 sheet (X by Y) missing its upper-right 48 x 24 corner.</summary>
    public static SheetOutline EllSheet() => new(new[]
    {
        new Vector(0, 0), new Vector(96, 0), new Vector(96, 24), new Vector(48, 24), new Vector(48, 48), new Vector(0, 48),
    });

    [Fact]
    public void Constructor_NormalizesToTheOriginAndCounterclockwise()
    {
        // Clockwise, offset from the origin, with a repeated closing vertex.
        var outline = new SheetOutline(new[]
        {
            new Vector(10, 10), new Vector(10, 30), new Vector(50, 30), new Vector(50, 10), new Vector(10, 10),
        });

        Assert.Equal(new[] { new Vector(0, 0), new Vector(40, 0), new Vector(40, 20), new Vector(0, 20) }, outline.Points);
        Assert.Equal(20, outline.Size.Width);
        Assert.Equal(40, outline.Size.Length);
        Assert.Equal(800, outline.Area, 9);
    }

    [Fact]
    public void Constructor_RejectsDegenerateAndSelfCrossingPolygons()
    {
        Assert.Throws<ArgumentException>(() => new SheetOutline(new[] { new Vector(0, 0), new Vector(1, 0) }));
        Assert.Throws<ArgumentException>(() => new SheetOutline(new[] { new Vector(0, 0), new Vector(1, 0), new Vector(2, 0) }));
        Assert.Throws<ArgumentException>(() => new SheetOutline(new[] { new Vector(0, 0), new Vector(1, double.NaN), new Vector(0, 1) }));
        // A bow tie.
        Assert.Throws<ArgumentException>(() => new SheetOutline(new[]
        {
            new Vector(0, 0), new Vector(10, 10), new Vector(10, 0), new Vector(0, 10),
        }));
    }

    [Fact]
    public void Obstacles_ReduceTheAreaAndAreDroppedWhenOutside()
    {
        var outline = new SheetOutline(
            new[] { new Vector(0, 0), new Vector(100, 0), new Vector(100, 50), new Vector(0, 50) },
            new[]
            {
                new[] { new Vector(10, 10), new Vector(20, 10), new Vector(20, 20), new Vector(10, 20) },
                new[] { new Vector(200, 200), new Vector(210, 200), new Vector(210, 210) },
            });

        Assert.Single(outline.Obstacles);
        Assert.Equal(5000 - 100, outline.Area, 6);
        Assert.Throws<ArgumentException>(() => new SheetOutline(
            new[] { new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10) },
            new[] { new[] { new Vector(-1, -1), new Vector(11, -1), new Vector(11, 11), new Vector(-1, 11) } }));
    }

    [Fact]
    public void Equality_IsExactOnVertices()
    {
        Assert.Equal(EllSheet(), EllSheet());
        Assert.Equal(EllSheet().GetHashCode(), EllSheet().GetHashCode());
        var moved = new SheetOutline(EllSheet().Points.Select((p, i) => i == 2 ? new Vector(96, 24.001) : p));
        Assert.NotEqual(EllSheet(), moved);
    }

    [Fact]
    public void Usable_ErodesTheMaterialAndHoldsOnlyRegionsInsideIt()
    {
        var sheet = EllSheet();
        var usable = sheet.Usable(1, new Vector());
        var bounds = Clipper.GetBounds(usable);
        Assert.Equal(1, bounds.left, 6);
        Assert.Equal(1, bounds.top, 6);
        Assert.Equal(95, bounds.right, 6);
        Assert.Equal(47, bounds.bottom, 6);

        // A 10 x 10 square in the lower-left corner, exactly at the inset.
        Assert.True(sheet.Holds(Square(1, 1, 10), 1, new Vector(), 0));
        // The same square in the missing corner.
        Assert.False(sheet.Holds(Square(60, 30, 10), 1, new Vector(), 0));
        // Just across the inset by more than the slack.
        Assert.False(sheet.Holds(Square(0.9, 1, 10), 1, new Vector(), 1e-5));
        Assert.True(sheet.Holds(Square(0.9999999, 1, 10), 1, new Vector(), 1e-5));
    }

    [Fact]
    public void Usable_FollowsThePlacementOrigin()
    {
        var sheet = EllSheet();
        Assert.Equal(new Vector(-96, 0), sheet.PlacementOrigin(2));
        Assert.Equal(new Vector(-96, -48), sheet.PlacementOrigin(3));
        Assert.Equal(new Vector(0, -48), sheet.PlacementOrigin(4));
        Assert.True(sheet.Holds(Square(-95, -47, 10), 1, sheet.PlacementOrigin(3), 0));
        Assert.False(sheet.Holds(Square(1, 1, 10), 1, sheet.PlacementOrigin(3), 0));
    }

    [Fact]
    public void Rotated_TurnsTheSheetAboutItsOwnCorner()
    {
        var sheet = EllSheet();

        var ccw = sheet.Rotated90(RotationType.CCW);
        Assert.Equal(96, ccw.Size.Width);
        Assert.Equal(48, ccw.Size.Length);
        // The missing corner moves from upper right to upper left.
        Assert.False(ccw.Holds(Square(2, 60, 10), 0, new Vector(), 0));
        Assert.True(ccw.Holds(Square(36, 60, 10), 0, new Vector(), 0));

        var cw = sheet.Rotated90(RotationType.CW);
        Assert.False(cw.Holds(Square(36, 2, 10), 0, new Vector(), 0));
        Assert.True(cw.Holds(Square(2, 2, 10), 0, new Vector(), 0));

        var half = sheet.Rotated180();
        Assert.Equal(sheet.Size, half.Size);
        Assert.False(half.Holds(Square(2, 2, 10), 0, new Vector(), 0));
        Assert.True(half.Holds(Square(80, 30, 10), 0, new Vector(), 0));
        Assert.Equal(sheet, half.Rotated180());
    }

    [Fact]
    public void FromGeometry_TakesTheLargestLoopAndKeepsInsideTheTrueEdges()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 96, 0), new Line(96, 0, 96, 24), new Line(96, 24, 48, 24),
            new Line(48, 24, 48, 48), new Line(48, 48, 0, 48), new Line(0, 48, 0, 0),
            // A round burn mark.
            new Circle(new Vector(20, 20), 3),
        };

        var outline = SheetOutline.FromGeometry(entities);

        Assert.Equal(48 - 2 * SheetOutline.ChordTolerance, outline.Size.Width, 6);
        Assert.Equal(96 - 2 * SheetOutline.ChordTolerance, outline.Size.Length, 6);
        var obstacle = Assert.Single(outline.Obstacles);
        var radius = obstacle.Max(p => p.X) - obstacle.Min(p => p.X);
        Assert.InRange(radius, 6 + 2 * SheetOutline.ChordTolerance - 1e-6, 6 + 4 * SheetOutline.ChordTolerance);
        Assert.Throws<ArgumentException>(() => SheetOutline.FromGeometry(new List<Entity> { new Line(0, 0, 10, 0) }));
    }

    [Fact]
    public void Plate_OutlineSetsTheSizeAndAnotherSizeDropsIt()
    {
        var plate = new Plate(10, 10) { Outline = EllSheet() };

        Assert.Equal(48, plate.Size.Width);
        Assert.Equal(96, plate.Size.Length);
        Assert.Equal(96 * 48 - 48 * 24, plate.Area(), 6);

        plate.Size = new Size(48, 96); // the same box keeps it
        Assert.NotNull(plate.Outline);
        plate.Size = new Size(60, 120);
        Assert.Null(plate.Outline);
        Assert.Equal(60 * 120, plate.Area());
    }

    [Fact]
    public void Plate_RotationsTurnTheOutlineAndAutoSizeLeavesIt()
    {
        var plate = new Plate { Outline = EllSheet() };
        plate.Parts.Add(new Part(new RectangleShape { Name = "SQ", Length = 10, Width = 10 }.GetDrawing(), new Vector(1, 1)));

        plate.Rotate90(RotationType.CCW);
        Assert.Equal(96, plate.Size.Width);
        Assert.Equal(48, plate.Size.Length);
        Assert.Equal(EllSheet().Rotated90(RotationType.CCW), plate.Outline);

        plate.Rotate180();
        Assert.Equal(EllSheet().Rotated90(RotationType.CCW).Rotated180(), plate.Outline);

        plate.AutoSize();
        Assert.Equal(96, plate.Size.Width);
        Assert.NotNull(plate.Outline);
        Assert.Equal(default, plate.SnapToStandardSize());
        Assert.NotNull(plate.Outline);
    }

    [Fact]
    public void Plate_OutlinedSheetHasNoExpectedRemnantAndRefusesAutomaticCutOffs()
    {
        var plate = new Plate { Outline = EllSheet() };
        plate.Parts.Add(new Part(new RectangleShape { Name = "SQ", Length = 10, Width = 10 }.GetDrawing(), new Vector(0, 0)));

        Assert.Null(PlateRemnant.Expected(plate, 12));
        var exception = Assert.Throws<ArgumentException>(() =>
            AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = 35 }, new CutOffSettings()));
        Assert.Contains("outline", exception.Message);
    }

    [Fact]
    public void CutOff_IsTrimmedToTheOutlinedMaterial()
    {
        var settings = new CutOffSettings { Overtravel = 0.5 };
        var plate = new Plate { Outline = EllSheet() };
        // Through the arm: material from Y 0 to 24 only.
        plate.CutOffs.Add(new CutOff(new Vector(70, 0), CutOffAxis.Vertical));
        // Through the full height.
        plate.CutOffs.Add(new CutOff(new Vector(20, 0), CutOffAxis.Vertical));
        // Along Y 36: material from X 0 to 48 only.
        plate.CutOffs.Add(new CutOff(new Vector(0, 36), CutOffAxis.Horizontal));
        plate.RegenerateCutOffs(settings);

        var arm = Assert.Single(plate.Parts.Where(p => p.BaseDrawing == plate.CutOffs[0].Drawing)).Program.BoundingBox();
        Assert.Equal(0, arm.Bottom, 9);
        Assert.Equal(24.5, arm.Top, 9);

        var full = Assert.Single(plate.Parts.Where(p => p.BaseDrawing == plate.CutOffs[1].Drawing)).Program.BoundingBox();
        Assert.Equal(0, full.Bottom, 9);
        Assert.Equal(48.5, full.Top, 9);

        var band = Assert.Single(plate.Parts.Where(p => p.BaseDrawing == plate.CutOffs[2].Drawing)).Program.BoundingBox();
        Assert.Equal(0, band.Left, 9);
        Assert.Equal(48.5, band.Right, 9);
    }

    [Fact]
    public void CutOff_OnARectangleIsUnchanged()
    {
        var settings = new CutOffSettings { Overtravel = 0.5 };
        var rectangle = new Plate(48, 96);
        rectangle.CutOffs.Add(new CutOff(new Vector(70, 0), CutOffAxis.Vertical));
        rectangle.RegenerateCutOffs(settings);
        var box = Assert.Single(rectangle.Parts).Program.BoundingBox();
        Assert.Equal(0, box.Bottom, 9);
        Assert.Equal(48.5, box.Top, 9);
    }

    private static PathsD Square(double x, double y, double side) =>
        new() { new PathD { new(x, y), new(x + side, y), new(x + side, y + side), new(x, y + side) } };
}
