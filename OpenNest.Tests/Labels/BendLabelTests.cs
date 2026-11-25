using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Labels;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.Labels;

/// <summary>
/// The bend group letter etched beside each bend line (docs/label-box.md): where it goes, what
/// it keeps clear of and which letter it is for the part's rotation against the grain.
/// </summary>
public class BendLabelTests
{
    private const double Quarter = System.Math.PI / 2;

    // Inch drawings: an eighth inch clearance, and the erosion tolerance a twentieth of that.
    private const double Clearance = 0.125;
    private const double Tolerance = Clearance / 20;

    private static List<Entity> Rectangle(double left, double bottom, double right, double top) =>
    [
        new Line(left, bottom, right, bottom), new Line(right, bottom, right, top),
        new Line(right, top, left, top), new Line(left, top, left, bottom),
    ];

    private static Bend Bend(double x1, double y1, double x2, double y2, BendDirection direction = BendDirection.Up) =>
        new() { StartPoint = new Vector(x1, y1), EndPoint = new Vector(x2, y2), Direction = direction };

    private static LabelBox Box(double x, double y, double height = 0.5) => new(new Vector(x, y), 2, height, 0);

    private static double DistanceToLine(Vector point, Bend bend)
    {
        var dx = bend.EndPoint.X - bend.StartPoint.X;
        var dy = bend.EndPoint.Y - bend.StartPoint.Y;
        return System.Math.Abs(dx * (point.Y - bend.StartPoint.Y) - dy * (point.X - bend.StartPoint.X)) / bend.Length;
    }

    [Fact]
    public void UpBend_GetsItsLetterBesideAnEtchDash_JustPastTheClearance()
    {
        var bend = Bend(5, 0, 5, 5);

        var spot = Assert.Single(BendLabels.Place(Rectangle(0, 0, 10, 5), [bend], Box(2, 2.5)));

        Assert.Equal(0, spot.Bend);
        Assert.Equal(0.5, spot.Diameter, 9);
        // The middle of the first inch-long dash, on the left of a line running up.
        Assert.Equal(0.5, spot.Center.Y, 6);
        Assert.Equal(5 - (Clearance + 0.25 + 2 * Tolerance), spot.Center.X, 6);
    }

    [Fact]
    public void DownBend_HasNoDashes_SoItsLetterSitsAtTheMiddle()
    {
        var bend = Bend(5, 0, 5, 5, BendDirection.Down);

        var spot = Assert.Single(BendLabels.Place(Rectangle(0, 0, 10, 5), [bend], Box(2, 4)));

        Assert.Equal(2.5, spot.Center.Y, 6);
    }

    [Fact]
    public void BendRadius_PushesTheLetterPastTheRolledBand()
    {
        var bend = Bend(5, 0, 5, 5);
        bend.Radius = 0.5;
        bend.Angle = 90;

        var spot = Assert.Single(BendLabels.Place(Rectangle(0, 0, 10, 5), [bend], Box(2, 2.5)));

        // Half the arc a 90 degree bend rolls at that radius, then the circle's own radius.
        var band = 0.5 * Quarter / 2;
        Assert.True(band > Clearance);
        Assert.Equal(band + 0.25 + 2 * Tolerance, DistanceToLine(spot.Center, bend), 6);
    }

    [Fact]
    public void NoRoomOnTheLeft_UsesTheRight()
    {
        // The bend runs up a quarter inch from the left edge: only its right side has material.
        var bend = Bend(0.25, 0, 0.25, 5);

        var spot = Assert.Single(BendLabels.Place(Rectangle(0, 0, 10, 5), [bend], Box(6, 2.5)));

        Assert.True(spot.Center.X > 0.25);
    }

    [Fact]
    public void Hole_LabelBoxAndMarks_AreKeptClear()
    {
        var bend = Bend(5, 0, 5, 5);
        var free = Assert.Single(BendLabels.Place(Rectangle(0, 0, 10, 5), [bend], Box(2, 2.5))).Center;

        // A hole over the first spot.
        var holed = Rectangle(0, 0, 10, 5);
        holed.Add(new Circle(free.X, free.Y, 0.2));
        var pastHole = Assert.Single(BendLabels.Place(holed, [bend], Box(2, 2.5)));
        Assert.True(pastHole.Center.DistanceTo(free) >= 0.2 + Clearance + 0.25 - 1e-3);

        // The label box over it.
        var box = new LabelBox(free, 2, 0.5, 0);
        var pastBox = Assert.Single(BendLabels.Place(Rectangle(0, 0, 10, 5), [bend], box));
        Assert.False(box.Contains(pastBox.Center));

        // An etched mark through it.
        var marked = Rectangle(0, 0, 10, 5);
        marked.Add(new Line(free.X - 1, free.Y, free.X, free.Y) { Layer = OpenNest.Bending.Bend.EtchLayer });
        var pastMark = Assert.Single(BendLabels.Place(marked, [bend], Box(2, 2.5)));
        Assert.True(pastMark.Center.DistanceTo(free) > 0.25);
    }

    [Fact]
    public void NarrowFlange_ShrinksTheLetter_AndNoRoomGetsNone()
    {
        // 0.7 either side of the bend holds a three eighths circle but not a half inch one.
        var shrunk = Assert.Single(BendLabels.Place(Rectangle(0, 0, 1.4, 5), [Bend(0.7, 0, 0.7, 5)], Box(20, 20)));
        Assert.Equal(0.375, shrunk.Diameter, 9);

        // Half an inch either side holds none, not even the smallest.
        Assert.Empty(BendLabels.Place(Rectangle(0, 0, 1, 5), [Bend(0.5, 0, 0.5, 5)], Box(20, 20)));
    }

    [Fact]
    public void EachBendGetsItsOwnSpot_ClearOfTheOthers_AndIsDeterministic()
    {
        // Two bends meeting at a corner, as on a part bent two ways.
        var bends = new List<Bend> { Bend(2, 2, 6, 2), Bend(2, 2, 2, 6) };
        var entities = Rectangle(0, 0, 12, 12);

        var spots = BendLabels.Place(entities, bends, Box(8, 8));

        Assert.Equal([0, 1], spots.Select(s => s.Bend));
        Assert.True(spots[0].Center.DistanceTo(spots[1].Center)
            >= spots[0].Diameter / 2 + spots[1].Diameter / 2 + Clearance - 1e-9);
        // Each circle stays off both bend lines.
        foreach (var spot in spots)
            Assert.All(bends, bend => Assert.True(DistanceToLine(spot.Center, bend) >= Clearance + spot.Diameter / 2 - 1e-9
                || spot.Center.X > 6 || spot.Center.Y > 6));
        Assert.Equal(spots, BendLabels.Place(entities, bends, Box(8, 8)));
    }

    [Fact]
    public void NoLabelBox_StillPlacesTheLetter_AndMillimeterDrawingsGetMillimeterSizes()
    {
        var spot = Assert.Single(BendLabels.Place(Rectangle(0, 0, 10, 5), [Bend(5, 0, 5, 5)], null));
        Assert.Equal(0.5, spot.Diameter, 9);

        var metric = Assert.Single(BendLabels.Place(Rectangle(0, 0, 250, 125), [Bend(125, 0, 125, 125, BendDirection.Down)],
            null, Units.Millimeters));
        Assert.Equal(12, metric.Diameter, 9);
        // 3 mm clear of the line, then the circle's radius.
        Assert.InRange(DistanceToLine(metric.Center, Bend(125, 0, 125, 125)), 9, 9.5);

        var unboxed = new Drawing("1234", LeadPathValidationTests.Rectangle(0, 0, 12, 12));
        unboxed.Bends.Add(Bend(2, 2, 6, 2));
        var spots = BendLabels.Spots(unboxed);
        Assert.Single(spots);
        Assert.NotEmpty(BendLabels.Strokes(unboxed, spots, 0, 0));
    }

    [Fact]
    public void ParallelBendsCloseTogether_ShareOneLetter()
    {
        // An inch apart, as on a channel's two flanges: the first bend's letter says both.
        var near = new List<Bend> { Bend(2, 5, 28, 5), Bend(2, 6, 28, 6) };
        var shared = Assert.Single(BendLabels.Place(Rectangle(0, 0, 30, 30), near, null));
        Assert.Equal(0, shared.Bend);

        // Running the other way is still parallel.
        var reversed = new List<Bend> { Bend(2, 5, 28, 5), Bend(28, 6, 2, 6) };
        Assert.Single(BendLabels.Place(Rectangle(0, 0, 30, 30), reversed, null));

        // Past twelve inches from the letter, the second bend gets its own.
        var far = new List<Bend> { Bend(2, 5, 28, 5), Bend(2, 25, 28, 25) };
        Assert.Equal([0, 1], BendLabels.Place(Rectangle(0, 0, 30, 30), far, null).Select(s => s.Bend));

        // So does a parallel bend in line with the first but far along it.
        var inLine = new List<Bend> { Bend(2, 5, 8, 5), Bend(40, 5, 46, 5) };
        Assert.Equal([0, 1], BendLabels.Place(Rectangle(0, 0, 48, 30), inLine, null).Select(s => s.Bend));
    }

    [Fact]
    public void ABendAtAnotherAngle_KeepsItsOwnLetter_HoweverClose()
    {
        // The slanted bend lies between two parallel ones: it may be another group.
        var bends = new List<Bend> { Bend(2, 5, 28, 5), Bend(2, 7, 28, 9), Bend(2, 12, 28, 12) };

        var spots = BendLabels.Place(Rectangle(0, 0, 30, 30), bends, null);

        Assert.Equal([0, 1], spots.Select(s => s.Bend));
    }

    [Fact]
    public void SharedReach_IsTwelveInchesOrThreeHundredMillimeters()
    {
        Assert.Equal(12, BendLabels.SharedReach(Units.Inches));
        Assert.Equal(300, BendLabels.SharedReach(Units.Millimeters));

        // 200 mm apart shares; 400 mm apart does not.
        var near = new List<Bend> { Bend(50, 100, 700, 100), Bend(50, 300, 700, 300) };
        Assert.Single(BendLabels.Place(Rectangle(0, 0, 750, 750), near, null, Units.Millimeters));
        var far = new List<Bend> { Bend(50, 100, 700, 100), Bend(50, 500, 700, 500) };
        Assert.Equal(2, BendLabels.Place(Rectangle(0, 0, 750, 750), far, null, Units.Millimeters).Count);
    }

    [Fact]
    public void NothingToPlace_IsEmpty()
    {
        var bend = Bend(5, 0, 5, 5);
        Assert.Empty(BendLabels.Place(Rectangle(0, 0, 10, 5), [], Box(2, 2.5)));
        Assert.Empty(BendLabels.Place([new Line(0, 0, 10, 0)], [bend], Box(2, 2.5)));
        Assert.Empty(BendLabels.Place(Rectangle(0, 0, 10, 5), [null!, Bend(1, 1, 1, 1)], Box(2, 2.5)));
        Assert.Empty(BendLabels.Spots(null));
    }

    [Fact]
    public void Layout_CirclesEachBendsOwnLetter_ForThePartsRotationAgainstTheGrain()
    {
        // One bend with the grain (B) and one across it (A); a quarter turn swaps them.
        var bends = new List<Bend> { Bend(2, 2, 6, 2), Bend(2, 2, 2, 6) };
        var spots = BendLabels.Place(Rectangle(0, 0, 12, 12), bends, Box(8, 8));

        var upright = BendLabels.Layout(spots, bends, 0, 0);
        var turned = BendLabels.Layout(spots, bends, Quarter, 0);

        List<Vector> Points(IEnumerable<Stroke> strokes) => strokes.SelectMany(s => s.Points()).ToList();
        // Each letter reads along its own bend line: level beside the first, turned a quarter
        // beside the second.
        var second = LabelBox.ReadableRotation(bends[1].LineAngle);
        var expected = PartLabels.CircledLetter('B', spots[0].Center, spots[0].Diameter)
            .Concat(PartLabels.CircledLetter('A', spots[1].Center, spots[1].Diameter, second));
        Assert.Equal(Points(expected), Points(upright));
        var swapped = PartLabels.CircledLetter('A', spots[0].Center, spots[0].Diameter)
            .Concat(PartLabels.CircledLetter('B', spots[1].Center, spots[1].Diameter, second));
        Assert.Equal(Points(swapped), Points(turned));

        // Every stroke stays inside its circle, however the bend runs.
        var slanted = new List<Bend> { Bend(2, 2, 6, 4) };
        var slantedSpots = BendLabels.Place(Rectangle(0, 0, 12, 12), slanted, null);
        Assert.NotEmpty(slantedSpots);
        foreach (var stroke in BendLabels.Layout(slantedSpots, slanted, 0, 0))
            Assert.Contains(slantedSpots, spot => stroke.Points().All(p => p.DistanceTo(spot.Center) <= spot.Diameter / 2 + 1e-9));
    }

    [Fact]
    public void DrawingSpots_FollowTheSourceOffset_AndStrokesTurnWithThePart()
    {
        Drawing Panel(Vector offset)
        {
            var drawing = new Drawing("1234", LeadPathValidationTests.Rectangle(0, 0, 12, 12))
            {
                LabelBox = new LabelBox(new Vector(8, 8) + offset, 3.5, 0.5, 0),
            };
            drawing.Source.Offset = offset;
            drawing.Bends.Add(Bend(2 + offset.X, 2 + offset.Y, 6 + offset.X, 2 + offset.Y));
            return drawing;
        }

        // The box and bend are stored in the source frame; the program has the offset taken off.
        var plain = Assert.Single(BendLabels.Spots(Panel(Vector.Zero)));
        var moved = Assert.Single(BendLabels.Spots(Panel(new Vector(3, 1))));
        Assert.Equal(plain.Center.X, moved.Center.X, 6);
        Assert.Equal(plain.Center.Y, moved.Center.Y, 6);

        var drawing = Panel(Vector.Zero);
        var spots = BendLabels.Spots(drawing);
        var turned = BendLabels.Strokes(drawing, spots, Quarter, 0);
        var center = plain.Center.Rotate(Quarter);
        Assert.NotEmpty(turned);
        Assert.All(turned, stroke => Assert.All(stroke.Points(),
            p => Assert.True(p.DistanceTo(center) <= plain.Diameter / 2 + 1e-9)));

        var program = (Program)drawing.Program.Clone();
        var before = program.Codes.Count;
        Assert.True(BendLabels.AddTo(program, drawing, spots, 0, 0));
        Assert.All(program.Codes.Skip(before).OfType<LinearMove>(), move => Assert.Equal(LayerType.Scribe, move.Layer));
        Assert.False(BendLabels.AddTo(program, drawing, [], 0, 0));
    }
}
