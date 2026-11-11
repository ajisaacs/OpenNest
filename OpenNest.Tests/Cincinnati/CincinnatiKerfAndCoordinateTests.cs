using OpenNest.CNC;
using OpenNest.CNC.Posting;
using OpenNest.Geometry;

namespace OpenNest.Tests.Cincinnati;

/// <summary>
/// A square with a hole through the CL post: the exact program, where the replayed G-code cuts
/// against Core's shared contours, a kerf side that keeps the part out of the kerf in inline
/// posting, part sub-programs and the hole sub-program, and the same program whichever mode
/// the part and hole programs are stored in.
/// </summary>
public class CincinnatiKerfAndCoordinateTests
{
    private static readonly Vector Location = new(1, 2);

    /// <summary>
    /// A 10x10 square cut CW from (0,0) after a linear lead-in, and a radius-0.5 hole at (5,5)
    /// cut CCW as a hole call; <paramref name="reversed"/> winds both the other way.
    /// </summary>
    private static Nest SquareWithHole(Mode partMode = Mode.Absolute, Mode holeMode = Mode.Incremental,
        bool reversed = false)
    {
        var hole = new Program(Mode.Absolute);
        hole.Codes.Add(new RapidMove(new Vector(0, -0.6)));
        hole.Codes.Add(new LinearMove(new Vector(0, -0.5)) { Layer = LayerType.Leadin });
        hole.Codes.Add(new ArcMove(new Vector(0, -0.5), Vector.Zero, reversed ? RotationType.CW : RotationType.CCW)
        {
            Layer = LayerType.Cut,
        });
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

        var plate = new Plate(60, 120);
        plate.Parts.Add(new Part(new Drawing("square-hole", pgm), Location));
        var nest = new Nest("N");
        nest.Plates.Add(plate);
        return nest;
    }

    [Fact]
    public void SquareWithHole_Inline_PostsTheExactProgram()
    {
        var output = CincinnatiReplay.Post(SquareWithHole(), partSubprograms: false);

        var body = output[output.IndexOf("GOTO1( Goto Feature )", StringComparison.Ordinal)..];
        Assert.Equal(
            """
            GOTO1( Goto Feature )
            N1 G52 X6 Y7
            ( PART: square-hole )
            M98 P200
            G52 X0 Y0
            M47
            N1002 G0 X1 Y1.5
            (WARNING: No library found)
            G84
            G41 G1 X1 Y2 F#126
            G1 X1 Y12 F#148
            G1 X11 Y12
            G1 X11 Y2
            G1 X1 Y2
            G40
            M35
            M42
            M99 (END OF N.001)
            (*****************************************************)
            :200
            ( PART: HOLE )
            N1 G0 X0 Y-0.6
            (WARNING: No library found)
            G84
            G41 G1 X0 Y-0.5 F#126
            G3 X0 Y-0.5 I0 J0.5 F[#148*#128]
            G40
            M35
            M99 (END OF HOLE)

            """,
            body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SquareWithHole_CutsTheSharedContoursOnTheSheet(bool partSubprograms)
    {
        var nest = SquareWithHole();
        var contours = PostContours.Extract(nest.Plates[0].Parts[0]);

        var run = CincinnatiReplay.Run(CincinnatiReplay.Post(nest, partSubprograms));

        AssertPoints(contours.Select(c => c.Pierce), run.PierceLog.Select(p => p.Point));
        var expected = contours.SelectMany(c => new[] { c.LeadIn }.Concat(c.Cuts)).ToList();
        Assert.Equal(expected.Count, run.Moves.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(CincinnatiReplay.Near(expected[i].EndPoint, run.Moves[i].To),
                $"Move {i} ends at {run.Moves[i].To}, expected {expected[i].EndPoint}");
            if (expected[i] is ArcMove arc)
            {
                Assert.True(CincinnatiReplay.Near(arc.CenterPoint, run.Moves[i].Center!.Value));
                Assert.Equal(arc.Rotation == RotationType.CW, run.Moves[i].Clockwise);
            }
        }
    }

    [Theory]
    [InlineData(false, false, "G41")]
    [InlineData(true, false, "G41")]
    [InlineData(false, true, "G42")]
    [InlineData(true, true, "G42")]
    public void KerfSide_KeepsThePartOutOfTheKerf(bool partSubprograms, bool reversed, string expected)
    {
        var run = CincinnatiReplay.Run(CincinnatiReplay.Post(SquareWithHole(reversed: reversed), partSubprograms));

        Assert.Equal(2, run.Pierces);
        Assert.All(run.Moves, move => Assert.Equal(expected, move.Compensation));

        // The first move after each pierce is the lead-in, which runs through scrap.
        var checkedCuts = 0;
        foreach (var move in run.Moves.Where((m, i) => i > 0 && run.Moves[i - 1].Pierce == m.Pierce))
        {
            AssertPartOnCompensatedSide(move);
            checkedCuts++;
        }
        Assert.Equal(5, checkedCuts);
    }

    [Theory]
    [InlineData(false, Mode.Absolute, Mode.Absolute)]
    [InlineData(false, Mode.Incremental, Mode.Absolute)]
    [InlineData(false, Mode.Incremental, Mode.Incremental)]
    [InlineData(true, Mode.Absolute, Mode.Absolute)]
    [InlineData(true, Mode.Incremental, Mode.Absolute)]
    [InlineData(true, Mode.Incremental, Mode.Incremental)]
    public void AbsoluteAndIncrementalPrograms_PostTheSameProgram(bool partSubprograms, Mode partMode, Mode holeMode)
    {
        var expected = WithoutDate(CincinnatiReplay.Post(SquareWithHole(Mode.Absolute, Mode.Incremental), partSubprograms));

        var actual = WithoutDate(CincinnatiReplay.Post(SquareWithHole(partMode, holeMode), partSubprograms));

        Assert.Equal(expected, actual);
    }

    // The header's third line is the posting time.
    private static string WithoutDate(string output) =>
        string.Join('\n', output.Split('\n').Where((_, i) => i != 2));

    /// <summary>
    /// Mid-line (or at an arc's start), the point just right of travel is in the part under G41
    /// and the point just left of it under G42; the other side is the kerf's.
    /// </summary>
    private static void AssertPartOnCompensatedSide(CincinnatiReplay.CutMove move)
    {
        Vector at, travel;
        if (move.Center is { } center)
        {
            at = move.From;
            var radial = move.From - center;
            travel = move.Clockwise ? new Vector(radial.Y, -radial.X) : new Vector(-radial.Y, radial.X);
        }
        else
        {
            at = (move.From + move.To) * 0.5;
            travel = move.To - move.From;
        }

        var length = System.Math.Sqrt(travel.X * travel.X + travel.Y * travel.Y);
        var right = new Vector(travel.Y / length, -travel.X / length);
        var partSide = move.Compensation == "G41" ? right : new Vector(-right.X, -right.Y);

        Assert.True(InPart(at + partSide * 0.01), $"Part is not on the compensated side at {at}");
        Assert.False(InPart(at - partSide * 0.01), $"Kerf side is in the part at {at}");
    }

    private static bool InPart(Vector p) =>
        p.X > Location.X && p.X < Location.X + 10
        && p.Y > Location.Y && p.Y < Location.Y + 10
        && p.DistanceTo(Location + new Vector(5, 5)) > 0.5;

    private static void AssertPoints(IEnumerable<Vector> expected, IEnumerable<Vector> actual)
    {
        var want = expected.ToList();
        var got = actual.ToList();
        Assert.Equal(want.Count, got.Count);
        for (var i = 0; i < want.Count; i++)
            Assert.True(CincinnatiReplay.Near(want[i], got[i]), $"Point {i} is {got[i]}, expected {want[i]}");
    }
}
