using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Converters;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// Interactive pushes on an outlined sheet stop at the edge of its usable material (the
/// outline less its obstacles, eroded by the largest edge spacing), not only at the bounding
/// box's work area: a part pushed toward a missing corner used to run through it to the box edge.
/// </summary>
public class OutlinePushTests
{
    // A 60 x 27 sheet missing a 6 x 3 rectangle at its lower-left corner.
    private const double Length = 60;
    private const double Width = 27;
    private const double NotchLength = 6;
    private const double NotchWidth = 3;
    private const double Edge = 0.25;

    private static SheetOutline NotchedSheet(IEnumerable<IEnumerable<Vector>> obstacles = null) =>
        new(new[]
        {
            new Vector(NotchLength, 0), new Vector(Length, 0), new Vector(Length, Width),
            new Vector(0, Width), new Vector(0, NotchWidth), new Vector(NotchLength, NotchWidth),
        }, obstacles);

    private static Plate MakePlate(int quadrant = 1, SheetOutline outline = null)
    {
        var plate = new Plate(10, 10)
        {
            Quadrant = quadrant,
            EdgeSpacing = new Spacing(Edge, Edge),
            PartSpacing = 0.25,
        };
        plate.Outline = outline ?? NotchedSheet();
        return plate;
    }

    private static Program SquareProgram(double size)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(size, 0);
        program.LineTo(size, size);
        program.LineTo(0, size);
        program.LineTo(0, 0);
        return program;
    }

    private static Program DiscProgram(double radius)
    {
        var program = new Program();
        program.MoveTo(2 * radius, radius);
        program.ArcTo(new Vector(2 * radius, radius), new Vector(radius, radius), RotationType.CCW);
        return program;
    }

    private static Program ObroundProgram(double length, double width)
    {
        var r = width / 2;
        var program = new Program();
        program.MoveTo(r, 0);
        program.LineTo(length - r, 0);
        program.ArcTo(new Vector(length - r, width), new Vector(length - r, r), RotationType.CCW);
        program.LineTo(r, width);
        program.ArcTo(new Vector(r, 0), new Vector(r, r), RotationType.CCW);
        return program;
    }

    private static Part Add(Plate plate, Program program, double x, double y)
    {
        var part = new Part(new Drawing($"part{plate.Parts.Count}", program), new Vector(x, y));
        plate.Parts.Add(part);
        return part;
    }

    private static Part AddSquare(Plate plate, double x, double y, double size = 2) =>
        Add(plate, SquareProgram(size), x, y);

    /// <summary>The plate's lower-left corner in the parts' placement frame.</summary>
    private static Vector Origin(Plate plate)
    {
        var sheet = plate.BoundingBox(includeParts: false);
        return new Vector(sheet.X, sheet.Y);
    }

    /// <summary>The edge review's outline test: the material keeps the edge spacing from the
    /// outline and its obstacles, within the shared slack; what the circumscribed outline flags
    /// is confirmed on the native contours.</summary>
    private static bool KeepsTheOutline(Plate plate, Part part)
    {
        // The drawing's own material: a placed program can carry leads. No test here rotates a part.
        var profile = new ShapeProfile(ConvertProgram.ToGeometry(part.BaseDrawing.Program).ForNesting().ToList());
        var material = SpacingOutline.Build(profile, part.Location, 0);
        var region = new PathsD { ClipperBridge.ToPath(material.Perimeter, positive: true) };
        return plate.Outline.Holds(region, () => NativeContours.Region(profile.Perimeter, part.Location),
            Edge, Origin(plate), SpacingOutline.SpacingSlack);
    }

    [Fact]
    public void PushLeftTowardTheMissingCornerStopsAtItsWall()
    {
        var plate = MakePlate();
        var mover = AddSquare(plate, 20, 0.25);

        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Left);

        Assert.Equal(NotchLength + Edge, mover.BoundingBox.Left, 6);
        Assert.True(KeepsTheOutline(plate, mover));
    }

    [Fact]
    public void PushDownOntoTheMissingCornerStopsAtItsWall()
    {
        var plate = MakePlate();
        var mover = AddSquare(plate, 2, 12);

        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Down);

        Assert.Equal(NotchWidth + Edge, mover.BoundingBox.Bottom, 6);
        Assert.True(KeepsTheOutline(plate, mover));
    }

    [Fact]
    public void PushPastTheMissingCornerStillReachesThePlateEdge()
    {
        var plate = MakePlate();
        var above = AddSquare(plate, 20, 8);
        var beside = AddSquare(plate, 30, 12);

        Compactor.Push(new List<Part> { above }, plate, PushDirection.Left);
        Compactor.Push(new List<Part> { beside }, plate, PushDirection.Down);

        Assert.Equal(Edge, above.BoundingBox.Left, 6);
        Assert.Equal(Edge, beside.BoundingBox.Bottom, 6);
    }

    [Fact]
    public void APartOverTheCornerOfTheNotchStopsWhenItsLowerEdgeMeetsTheWall()
    {
        // Half of the square is above the notch's top, half below: the lower half meets the wall.
        var plate = MakePlate();
        var mover = AddSquare(plate, 20, 2.5);

        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Left);

        Assert.Equal(NotchLength + Edge, mover.BoundingBox.Left, 6);
    }

    [Fact]
    public void APartRestingOnTheWallsStillMovesAlongAndAwayFromThem()
    {
        var plate = MakePlate();
        var mover = AddSquare(plate, 20, 1);
        var moving = new List<Part> { mover };

        Compactor.Push(moving, plate, PushDirection.Left); // against the notch's side
        Assert.Equal(0, Compactor.Push(moving, plate, PushDirection.Left), 6);
        Compactor.Push(moving, plate, PushDirection.Down); // along it to the plate edge
        Assert.Equal(Edge, mover.BoundingBox.Bottom, 6);
        Assert.Equal(NotchLength + Edge, mover.BoundingBox.Left, 6);

        Compactor.Push(moving, plate, PushDirection.Up); // along it, past its corner, to the top
        Assert.Equal(Width - Edge, mover.BoundingBox.Top, 6);
        Compactor.Push(moving, plate, PushDirection.Left); // now clear of the notch
        Assert.Equal(Edge, mover.BoundingBox.Left, 6);
        Compactor.Push(moving, plate, PushDirection.Down); // down the plate edge onto the notch's top
        Assert.Equal(NotchWidth + Edge, mover.BoundingBox.Bottom, 6);
        Compactor.Push(moving, plate, PushDirection.Right); // along the top, off its end, to the far edge
        Assert.Equal(Length - Edge, mover.BoundingBox.Right, 6);
        Assert.True(KeepsTheOutline(plate, mover));
    }

    [Fact]
    public void ARoundPartRestsOnTheWallAsOnThePlateEdge()
    {
        // The arc is slid as an arc and the review confirms on native contours: no chord gap.
        var plate = MakePlate();
        var mover = Add(plate, DiscProgram(1.5), 20, 1);
        var moving = new List<Part> { mover };
        Assert.True(KeepsTheOutline(plate, mover));

        Compactor.Push(moving, plate, PushDirection.Left);

        Assert.Equal(NotchLength + Edge, mover.BoundingBox.Left, 6);
        Assert.True(KeepsTheOutline(plate, mover));
        Assert.Equal(0, Compactor.Push(moving, plate, PushDirection.Left), 6);

        // Down the wall to the plate's own edge, where the box test holds it: still clean.
        Compactor.Push(moving, plate, PushDirection.Down);
        Assert.Equal(Edge, mover.BoundingBox.Bottom, 6);
        Assert.Equal(NotchLength + Edge, mover.BoundingBox.Left, 6);
        Assert.True(KeepsTheOutline(plate, mover));
    }

    [Fact]
    public void ARoundPartPastTheWallByMoreThanTheSlackFailsTheReview()
    {
        var plate = MakePlate();
        var resting = Add(plate, DiscProgram(1.5), NotchLength + Edge, 0.25);
        var within = Add(plate, DiscProgram(1.5), NotchLength + Edge - 0.0003, 0.25);
        var past = Add(plate, DiscProgram(1.5), NotchLength + Edge - 0.001, 0.25);

        Assert.True(KeepsTheOutline(plate, resting));
        Assert.True(KeepsTheOutline(plate, within));
        Assert.False(KeepsTheOutline(plate, past));
    }

    [Fact]
    public void ADiagonalPushRoundsTheCornerOfTheNotchAtTheEdgeSpacing()
    {
        var plate = MakePlate();
        var mover = AddSquare(plate, NotchLength + 5, NotchWidth + 5);

        // Straight at the notch's corner: the square's own corner leads.
        var moved = Compactor.Push(new List<Part> { mover }, plate, System.Math.PI * 1.25);

        Assert.True(moved > 0);
        var corner = new Vector(NotchLength, NotchWidth);
        var lead = new Vector(mover.BoundingBox.Left, mover.BoundingBox.Bottom);
        // The usable area's round corner is chorded inside its arc by up to the join tolerance.
        Assert.InRange(lead.DistanceTo(corner), Edge - 0.001 - 1e-6, Edge + 1e-6);
        Assert.True(KeepsTheOutline(plate, mover));
    }

    [Fact]
    public void AnObstacleInsideTheSheetIsAWall()
    {
        var burn = new[] { new Vector(30, 10), new Vector(33, 10), new Vector(33, 13), new Vector(30, 13) };
        var plate = MakePlate(outline: NotchedSheet(new[] { burn }));
        var mover = AddSquare(plate, 45, 10.5);
        var clear = AddSquare(plate, 45, 20);

        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Left);
        Compactor.Push(new List<Part> { clear }, plate, PushDirection.Left);

        Assert.Equal(33 + Edge, mover.BoundingBox.Left, 6);
        Assert.Equal(Edge, clear.BoundingBox.Left, 6);
        Assert.True(KeepsTheOutline(plate, mover));
    }

    [Fact]
    public void AGroupStopsWhenAnyMemberMeetsTheWall()
    {
        var plate = MakePlate();
        var low = AddSquare(plate, 20, 0.25);
        var high = AddSquare(plate, 14, 10);

        Compactor.Push(new List<Part> { low, high }, plate, PushDirection.Left);

        Assert.Equal(NotchLength + Edge, low.BoundingBox.Left, 6);
        Assert.Equal(14 - (20 - NotchLength - Edge), high.BoundingBox.Left, 6);
    }

    [Fact]
    public void APartAlreadyInTheMissingCornerCanBePushedOut()
    {
        var plate = MakePlate();
        var mover = AddSquare(plate, 3, 0.5);

        var moved = Compactor.Push(new List<Part> { mover }, plate, PushDirection.Right);

        Assert.True(moved > 10, $"moved {moved}");
        Assert.Equal(Length - Edge, mover.BoundingBox.Right, 6);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TheWallFollowsThePlateQuadrant(int quadrant)
    {
        var plate = MakePlate(quadrant);
        var origin = Origin(plate);
        var mover = AddSquare(plate, origin.X + 20, origin.Y + Edge);

        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Left);

        Assert.Equal(origin.X + NotchLength + Edge, mover.BoundingBox.Left, 4);
        Assert.True(KeepsTheOutline(plate, mover));
    }

    [Fact]
    public void UnevenEdgeSpacingKeepsTheLargestFromEveryOutlineEdge()
    {
        // The outline keeps the largest edge spacing all round; the left work-area edge alone
        // would let the part reach 0.25 from the sheet.
        var plate = MakePlate();
        plate.EdgeSpacing = new Spacing(Edge, 1, Edge, Edge);
        var mover = AddSquare(plate, 20, 10);

        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Left);

        Assert.Equal(1, mover.BoundingBox.Left, 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettlingACopyTowardTheMissingCornerStaysOnTheSheet(bool boundingBoxesOnly)
    {
        var plate = MakePlate();
        var copy = new Part(new Drawing("copy", SquareProgram(2)), new Vector(9, 1));
        var moving = new List<Part> { copy };

        Compactor.SettlePlacement(moving, plate, PushDirection.Left, PushDirection.Down,
            boundingBoxesOnly: boundingBoxesOnly);

        Assert.Equal(NotchLength + Edge, copy.BoundingBox.Left, 6);
        Assert.Equal(Edge, copy.BoundingBox.Bottom, 6);
        Assert.True(KeepsTheOutline(plate, copy));
    }

    // A box push rests the box on the wall; the material in it must still be held there.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettlingARoundPartOntoTheMissingCornerStaysOnTheSheet(bool boundingBoxesOnly)
    {
        foreach (var radius in new[] { 1.0, 1.5, 2.2 })
        {
            var plate = MakePlate();
            var disc = new Part(new Drawing("disc", DiscProgram(radius)), new Vector(1, 12));

            Compactor.SettlePlacement(new List<Part> { disc }, plate, PushDirection.Left, PushDirection.Down,
                boundingBoxesOnly: boundingBoxesOnly);

            Assert.Equal(NotchWidth + Edge, disc.BoundingBox.Bottom, 6);
            Assert.True(KeepsTheOutline(plate, disc), $"radius {radius}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettlingAnObroundBesideTheMissingCornerStaysOnTheSheet(bool boundingBoxesOnly)
    {
        var plate = MakePlate();
        var obround = new Part(new Drawing("obround", ObroundProgram(3, 0.75)), new Vector(12, 1.2));

        Compactor.SettlePlacement(new List<Part> { obround }, plate, PushDirection.Left, PushDirection.Down,
            boundingBoxesOnly: boundingBoxesOnly);

        Assert.Equal(NotchLength + Edge, obround.BoundingBox.Left, 6);
        Assert.True(KeepsTheOutline(plate, obround));
    }

    [Fact]
    public void BoxPushRestsOnTheWallAndTheNextGeometryPushIsHeld()
    {
        var plate = MakePlate();
        var mover = Add(plate, DiscProgram(1.5), 2, 12);
        var moving = new List<Part> { mover };

        Compactor.PushBoundingBox(moving, plate, PushDirection.Down);
        Assert.Equal(NotchWidth + Edge, mover.BoundingBox.Bottom, 6);
        Assert.True(KeepsTheOutline(plate, mover));

        Assert.Equal(0, Compactor.Push(moving, plate, PushDirection.Down), 6);
        Assert.Equal(0, Compactor.PushBoundingBox(moving, plate, PushDirection.Down), 6);
        Assert.True(KeepsTheOutline(plate, mover));
    }

    // A placed program carries its leads; the edge review judges the material without them.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APartWithLeadsIsHeldByItsMaterial(bool arcLeads)
    {
        var plate = MakePlate();
        var parameters = new CuttingParameters
        {
            ExternalLeadIn = arcLeads ? new ArcLeadIn { Radius = 0.25 } : new LineLeadIn { Length = 0.25, ApproachAngle = 135 },
            ExternalLeadOut = arcLeads ? new ArcLeadOut { Radius = 0.25 } : new LineLeadOut { Length = 0.25, ApproachAngle = 90 },
        };
        Part Leaded(double x, double y)
        {
            var part = new Part(new Drawing($"leaded{plate.Parts.Count}", SquareProgram(2)));
            part.ApplyLeadIns(parameters, new Vector(-5, -5));
            part.Location = new Vector(x, y);
            plate.Parts.Add(part);
            return part;
        }

        // Pushed from afar the material, not a lead, meets the wall.
        var far = Leaded(20, 1);
        Compactor.Push(new List<Part> { far }, plate, PushDirection.Left);
        Assert.Equal(NotchLength + Edge, far.Location.X, 6);

        // Resting on the notch's top with a lead in the edge margin, it is still held.
        var resting = Leaded(2, NotchWidth + Edge);
        Assert.Equal(0, Compactor.Push(new List<Part> { resting }, plate, PushDirection.Down), 6);
        Assert.True(KeepsTheOutline(plate, resting));
    }

    [Fact]
    public void ARectangularSheetHasNoWalls()
    {
        var plate = new Plate(Width, Length) { EdgeSpacing = new Spacing(Edge, Edge) };
        var mover = AddSquare(plate, 20, 0.25);

        Assert.True(InteractiveOutline.Of(plate).IsEmpty);
        Compactor.Push(new List<Part> { mover }, plate, PushDirection.Left);

        Assert.Equal(Edge, mover.BoundingBox.Left, 6);
    }

    [Fact]
    public void AnOutlineThatFillsItsBoxHasNoWalls()
    {
        // The usable area and the work area coincide: nothing may sit under a part on the edge.
        var box = new SheetOutline(new[]
        {
            new Vector(0, 0), new Vector(59.99995, 0), new Vector(59.99995, 26.99985), new Vector(0, 26.99985),
        });
        var plate = MakePlate(outline: box);

        Assert.True(InteractiveOutline.Of(plate).IsEmpty);
    }
}
