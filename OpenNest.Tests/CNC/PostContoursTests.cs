using OpenNest.CNC;
using OpenNest.CNC.Posting;
using OpenNest.Geometry;

namespace OpenNest.Tests.CNC;

/// <summary>
/// The geometry every post writes from: a square with a hole flattened to sheet coordinates,
/// the same contours whichever mode the part and hole programs are stored in, and a kerf side
/// that keeps the part out of the kerf.
/// </summary>
public class PostContoursTests
{
    private static readonly Vector Location = new(1, 2);

    /// <summary>
    /// A 10x10 square cut CW from (0,0) after a linear lead-in, and a radius-0.5 hole at (5,5)
    /// cut CCW as a hole call, the way the cutting strategy writes them. Built absolute, then
    /// switched to the requested modes.
    /// </summary>
    private static Program SquareWithHole(Mode partMode = Mode.Absolute, Mode holeMode = Mode.Incremental,
        bool reversed = false)
    {
        var holeRotation = reversed ? RotationType.CW : RotationType.CCW;
        var hole = new Program(Mode.Absolute);
        hole.Codes.Add(new RapidMove(new Vector(0, -0.6)));
        hole.Codes.Add(new LinearMove(new Vector(0, -0.5)) { Layer = LayerType.Leadin });
        hole.Codes.Add(new ArcMove(new Vector(0, -0.5), Vector.Zero, holeRotation) { Layer = LayerType.Cut });
        hole.Mode = holeMode;

        var pgm = new Program(Mode.Absolute);
        pgm.Codes.Add(new SubProgramCall { Id = 1, Program = hole, Offset = new Vector(5, 5) });
        pgm.Codes.Add(new RapidMove(new Vector(0, -0.5)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)) { Layer = LayerType.Leadin });
        var corners = reversed
            ? new[] { new Vector(10, 0), new Vector(10, 10), new Vector(0, 10), Vector.Zero }
            : new[] { new Vector(0, 10), new Vector(10, 10), new Vector(10, 0), Vector.Zero };
        foreach (var corner in corners)
            pgm.Codes.Add(new LinearMove(corner) { Layer = LayerType.Cut });
        pgm.Mode = partMode;
        return pgm;
    }

    private static Part Place(Program program) => new(new Drawing("square-hole", program), Location);

    [Fact]
    public void SquareWithHole_PostsInSheetCoordinates()
    {
        var contours = PostContours.Extract(Place(SquareWithHole()));

        Assert.Equal(2, contours.Count);

        // The hole: part location + call offset + hole-local points.
        var hole = contours[0];
        Assert.False(hole.IsPerimeter);
        AssertPoint(new Vector(6, 6.4), hole.Pierce);
        AssertPoint(new Vector(6, 6.5), hole.LeadIn.EndPoint);
        var arc = Assert.IsType<ArcMove>(Assert.Single(hole.Cuts));
        AssertPoint(new Vector(6, 6.5), arc.EndPoint);
        AssertPoint(new Vector(6, 7), arc.CenterPoint);
        Assert.Equal(RotationType.CCW, arc.Rotation);

        var perimeter = contours[1];
        Assert.True(perimeter.IsPerimeter);
        AssertPoint(new Vector(1, 1.5), perimeter.Pierce);
        AssertPoint(new Vector(1, 2), perimeter.LeadIn.EndPoint);
        Assert.Equal(
            new[] { new Vector(1, 12), new Vector(11, 12), new Vector(11, 2), new Vector(1, 2) },
            perimeter.Cuts.Select(c => Round(c.EndPoint)));
    }

    [Theory]
    [InlineData(Mode.Absolute, Mode.Absolute)]
    [InlineData(Mode.Absolute, Mode.Incremental)]
    [InlineData(Mode.Incremental, Mode.Absolute)]
    [InlineData(Mode.Incremental, Mode.Incremental)]
    public void AbsoluteAndIncrementalPrograms_PostTheSameContours(Mode partMode, Mode holeMode)
    {
        var expected = PostContours.Extract(Place(SquareWithHole(Mode.Absolute, Mode.Absolute)));
        var program = SquareWithHole(partMode, holeMode);

        var actual = PostContours.Extract(Place(program));

        AssertSameContours(expected, actual);
        Assert.Equal(partMode, program.Mode);
    }

    [Fact]
    public void RotatedPart_RotatesEveryPointAndKeepsTheKerfSide()
    {
        var expected = PostContours.Extract(Place(SquareWithHole()));
        var part = Place(SquareWithHole());
        part.Rotate(OpenNest.Math.Angle.ToRadians(90));

        var actual = PostContours.Extract(part);

        // A 90-degree CCW turn about the origin: (x, y) -> (-y, x).
        static Vector Turn(Vector v) => new(-v.Y, v.X);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            AssertPoint(Turn(expected[i].Pierce), actual[i].Pierce);
            AssertPoint(Turn(expected[i].LeadIn.EndPoint), actual[i].LeadIn.EndPoint);
            Assert.Equal(expected[i].Cuts.Count, actual[i].Cuts.Count);
            for (var c = 0; c < expected[i].Cuts.Count; c++)
                AssertPoint(Turn(expected[i].Cuts[c].EndPoint), actual[i].Cuts[c].EndPoint);
            Assert.Equal(expected[i].Kerf, actual[i].Kerf);
            Assert.Equal(expected[i].IsPerimeter, actual[i].IsPerimeter);
        }
    }

    [Theory]
    [InlineData(false, KerfType.Left)]
    [InlineData(true, KerfType.Right)]
    public void KerfSide_KeepsThePartOutOfTheKerf(bool reversed, KerfType expected)
    {
        var contours = PostContours.Extract(Place(SquareWithHole(reversed: reversed)));

        Assert.Equal(2, contours.Count);
        foreach (var contour in contours)
        {
            Assert.Equal(expected, contour.Kerf);
            AssertPartOnCompensatedSide(contour);
        }
    }

    [Fact]
    public void PartFrame_LeavesThePartLocationOut()
    {
        var sheet = PostContours.Extract(Place(SquareWithHole()));
        var local = PostContours.Extract(SquareWithHole(), Vector.Zero);

        Assert.Equal(sheet.Count, local.Count);
        for (var i = 0; i < sheet.Count; i++)
        {
            AssertPoint(sheet[i].Pierce - Location, local[i].Pierce);
            for (var c = 0; c < sheet[i].Cuts.Count; c++)
                AssertPoint(sheet[i].Cuts[c].EndPoint - Location, local[i].Cuts[c].EndPoint);
        }
    }

    [Fact]
    public void SuppressedSide_IsCrossedWithTheBeamOffNotCutAsAChord()
    {
        var pgm = new Program(Mode.Absolute);
        pgm.Codes.Add(new RapidMove(new Vector(0, -0.5)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)) { Layer = LayerType.Leadin });
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)) { Suppressed = true });
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));

        var contours = PostContours.Extract(pgm, Vector.Zero);

        Assert.Equal(2, contours.Count);
        Assert.Equal(new[] { new Vector(0, 10) }, contours[0].Cuts.Select(c => Round(c.EndPoint)));
        AssertPoint(new Vector(10, 10), contours[1].Pierce);
        Assert.Equal(
            new[] { new Vector(10, 0), new Vector(0, 0) },
            contours[1].Cuts.Select(c => Round(c.EndPoint)));
    }

    [Fact]
    public void ScribeMark_IsNeverThePerimeterAndHasNoKerf()
    {
        var pgm = SquareWithHole();
        // A mark wider than the part.
        pgm.Codes.Add(new RapidMove(new Vector(-5, -5)));
        pgm.Codes.Add(new LinearMove(new Vector(20, -5)) { Layer = LayerType.Scribe });
        pgm.Codes.Add(new LinearMove(new Vector(20, 20)) { Layer = LayerType.Scribe });
        pgm.Codes.Add(new LinearMove(new Vector(-5, -5)) { Layer = LayerType.Scribe });

        var contours = PostContours.Extract(pgm, Vector.Zero);

        var mark = Assert.Single(contours, c => c.IsMark);
        Assert.False(mark.IsPerimeter);
        Assert.Equal(KerfType.None, mark.Kerf);
        Assert.True(contours[1].IsPerimeter);
    }

    [Fact]
    public void CutOff_HasNoPerimeterOrKerf()
    {
        var plate = new Plate(60, 120);
        var cutOff = new CutOff(new Vector(20, 0), CutOffAxis.Vertical);
        cutOff.Regenerate(plate, new CutOffSettings());

        var contours = PostContours.Extract(new Part(cutOff.Drawing, Vector.Zero));

        Assert.NotEmpty(contours);
        Assert.All(contours, c =>
        {
            Assert.False(c.IsPerimeter);
            Assert.Equal(KerfType.None, c.Kerf);
        });
    }

    [Fact]
    public void Extract_LeavesThePartProgramUnchanged()
    {
        var program = SquareWithHole(Mode.Incremental, Mode.Incremental);
        var hole = ((SubProgramCall)program.Codes[0]).Program;
        var before = program.Codes.Select(c => c.ToString()).ToList();

        PostContours.Extract(Place(program));

        Assert.Equal(Mode.Incremental, program.Mode);
        Assert.Equal(Mode.Incremental, hole.Mode);
        Assert.Equal(before, program.Codes.Select(c => c.ToString()));
    }

    /// <summary>
    /// On the first cut (mid-line, away from corners), the point just right of travel is in the
    /// part for G41 and the point just left of it is in the part for G42; the other side is the
    /// kerf's.
    /// </summary>
    private static void AssertPartOnCompensatedSide(PostContour contour)
    {
        var from = contour.LeadIn.EndPoint;
        var cut = contour.Cuts[0];
        var start = cut is ArcMove ? from : (from + cut.EndPoint) * 0.5;
        var travel = cut is ArcMove arc
            ? Tangent(from, arc)
            : cut.EndPoint - from;
        var length = System.Math.Sqrt(travel.X * travel.X + travel.Y * travel.Y);
        var right = new Vector(travel.Y / length, -travel.X / length);
        var partSide = contour.Kerf == KerfType.Left ? right : new Vector(-right.X, -right.Y);

        Assert.True(InPart(start + partSide * 0.01), $"Part is not on the compensated side at {start}");
        Assert.False(InPart(start - partSide * 0.01), $"Kerf side is in the part at {start}");
    }

    private static Vector Tangent(Vector start, ArcMove arc)
    {
        var radial = start - arc.CenterPoint;
        return arc.Rotation == RotationType.CCW
            ? new Vector(-radial.Y, radial.X)
            : new Vector(radial.Y, -radial.X);
    }

    private static bool InPart(Vector p) =>
        p.X > Location.X && p.X < Location.X + 10
        && p.Y > Location.Y && p.Y < Location.Y + 10
        && p.DistanceTo(Location + new Vector(5, 5)) > 0.5;

    private static void AssertSameContours(IReadOnlyList<PostContour> expected, IReadOnlyList<PostContour> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            AssertPoint(expected[i].Pierce, actual[i].Pierce);
            AssertPoint(expected[i].LeadIn.EndPoint, actual[i].LeadIn.EndPoint);
            Assert.Equal(expected[i].Cuts.Count, actual[i].Cuts.Count);
            for (var c = 0; c < expected[i].Cuts.Count; c++)
            {
                var want = expected[i].Cuts[c];
                var got = actual[i].Cuts[c];
                Assert.Equal(want.GetType(), got.GetType());
                AssertPoint(want.EndPoint, got.EndPoint);
                if (want is ArcMove wantArc)
                {
                    var gotArc = (ArcMove)got;
                    AssertPoint(wantArc.CenterPoint, gotArc.CenterPoint);
                    Assert.Equal(wantArc.Rotation, gotArc.Rotation);
                }
            }
            Assert.Equal(expected[i].IsPerimeter, actual[i].IsPerimeter);
            Assert.Equal(expected[i].Kerf, actual[i].Kerf);
        }
    }

    private static void AssertPoint(Vector expected, Vector actual) =>
        Assert.True(expected.DistanceTo(actual) < 1e-9, $"Expected {expected}, got {actual}");

    private static Vector Round(Vector v) => new(System.Math.Round(v.X, 9), System.Math.Round(v.Y, 9));
}
