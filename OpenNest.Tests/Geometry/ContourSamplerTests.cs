using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// Characterization tests for the pure sampling math extracted from
/// <c>CutDirectionArrows</c>: they pin the arrow display policy (per-move counts,
/// short-move skipping, sweep conventions) and the new contour-wide arclength
/// scheduler used for alignment measurements.
/// </summary>
public class ContourSamplerTests
{
    private const double Tol = 1e-9;

    private static void AssertVector(Vector actual, double x, double y, string what = "")
    {
        Assert.True(
            System.Math.Abs(actual.X - x) < Tol && System.Math.Abs(actual.Y - y) < Tol,
            $"{what} expected ({x},{y}) but was ({actual.X},{actual.Y})"
        );
    }

    // ---------- line policy (mirrors DrawLineArrows) ----------

    [Fact]
    public void LineMoves_EstablishedCountsAndInteriorPositions()
    {
        var samples = new List<ContourSample>();
        ContourSampler.LineMoves(new Vector(0, 0), new Vector(10, 0), 3.0, samples);

        // count = max(1, (int)(10/3)) = 3, step = 10/(3+1) = 2.5
        Assert.Equal(3, samples.Count);
        AssertVector(samples[0].Position, 2.5, 0, "sample 0");
        AssertVector(samples[1].Position, 5.0, 0, "sample 1");
        AssertVector(samples[2].Position, 7.5, 0, "sample 2");
        foreach (var s in samples)
        {
            AssertVector(s.Direction, 1, 0, "direction");
            Assert.Equal(0.0, s.Tangent, 9);
        }
        Assert.Equal(2.5, samples[0].At, 9);
        Assert.Equal(5.0, samples[1].At, 9);
        Assert.Equal(7.5, samples[2].At, 9);
    }

    [Fact]
    public void LineMoves_SkipsMovesShorterThanHalfSpacing()
    {
        var samples = new List<ContourSample>();
        ContourSampler.LineMoves(new Vector(0, 0), new Vector(1.4, 0), 3.0, samples);
        Assert.Empty(samples);
    }

    [Fact]
    public void LineMoves_AlwaysAtLeastOneArrowWhenKept()
    {
        var samples = new List<ContourSample>();
        ContourSampler.LineMoves(new Vector(0, 0), new Vector(2, 0), 3.0, samples);
        Assert.Single(samples);
        AssertVector(samples[0].Position, 1.0, 0, "single arrow");
    }

    [Fact]
    public void LineMoves_ZeroLengthProducesNoSamples()
    {
        var samples = new List<ContourSample>();
        ContourSampler.LineMoves(new Vector(3, 3), new Vector(3, 3), 3.0, samples);
        Assert.Empty(samples);
    }

    // ---------- arc policy (mirrors DrawArcArrows) ----------

    [Fact]
    public void ArcMoves_CcwFullCircle_FullSweepAndTangents()
    {
        var samples = new List<ContourSample>();
        ContourSampler.ArcMoves(
            new Vector(5, 0),
            new Vector(5, 0),
            new Vector(0, 0),
            RotationType.CCW,
            4.0,
            samples
        );

        // radius 5, sweep 2*PI -> arcLength ~31.4159, count = (int)(31.4159/4) = 7
        Assert.Equal(7, samples.Count);
        for (var i = 0; i < samples.Count; i++)
        {
            var angle = (2 * System.Math.PI * (i + 1)) / (samples.Count + 1);
            AssertVector(samples[i].Position, 5 * System.Math.Cos(angle), 5 * System.Math.Sin(angle), $"ccw sample {i}");
            // CCW tangent is the radius angle + 90 degrees
            AssertVector(
                samples[i].Direction,
                System.Math.Cos(angle + System.Math.PI / 2),
                System.Math.Sin(angle + System.Math.PI / 2),
                $"ccw tangent {i}"
            );
            Assert.Equal(5 * angle, samples[i].At, 7);
        }
    }

    [Fact]
    public void ArcMoves_CwFullCircle_MirrorsCcwPositions()
    {
        var cw = new List<ContourSample>();
        ContourSampler.ArcMoves(
            new Vector(5, 0),
            new Vector(5, 0),
            new Vector(0, 0),
            RotationType.CW,
            4.0,
            cw
        );
        var ccw = new List<ContourSample>();
        ContourSampler.ArcMoves(
            new Vector(5, 0),
            new Vector(5, 0),
            new Vector(0, 0),
            RotationType.CCW,
            4.0,
            ccw
        );

        Assert.Equal(ccw.Count, cw.Count);
        for (var i = 0; i < cw.Count; i++)
        {
            // CW runs the negative angle family: mirror of the CCW sample across the X axis.
            AssertVector(cw[i].Position, ccw[i].Position.X, -ccw[i].Position.Y, $"cw sample {i}");
            AssertVector(cw[i].Direction, ccw[i].Direction.X, -ccw[i].Direction.Y, $"cw dir {i}");
        }
    }

    [Fact]
    public void ArcMoves_HalfTurnSweepIsPositivePi()
    {
        var samples = new List<ContourSample>();
        ContourSampler.ArcMoves(
            new Vector(5, 0),
            new Vector(-5, 0),
            new Vector(0, 0),
            RotationType.CCW,
            1.0,
            samples
        );

        // sweep = PI, arcLength = 5*PI ~ 15.708, count = 15, stepAngle = PI/16
        Assert.Equal(15, samples.Count);
        var stepAngle = System.Math.PI / 16;
        AssertVector(
            samples[0].Position,
            5 * System.Math.Cos(stepAngle),
            5 * System.Math.Sin(stepAngle),
            "first half-turn sample"
        );
        Assert.True(samples[^1].Position.X < 0 && samples[^1].Position.Y > 0);
    }

    [Fact]
    public void ArcMoves_DegenerateRadiusProducesNoSamples()
    {
        var samples = new List<ContourSample>();
        ContourSampler.ArcMoves(
            new Vector(0, 0),
            new Vector(1, 0),
            new Vector(0, 0),
            RotationType.CCW,
            0.1,
            samples
        );
        Assert.Empty(samples);
    }

    // ---------- program walk ----------

    private static Program Triangle(double side)
    {
        var pgm = new Program();
        pgm.Codes.Add(new LinearMove(0, 0));
        pgm.Codes.Add(new LinearMove(side, 0));
        pgm.Codes.Add(new LinearMove(side, side));
        pgm.Codes.Add(new LinearMove(0, 0));
        return pgm;
    }

    [Fact]
    public void ProgramMoves_AbsoluteEndpointsAreRelativeToBasePos()
    {
        var samples = new List<ContourSample>();
        var end = ContourSampler.ProgramMoves(
            Triangle(10),
            new Vector(100, 200),
            new Vector(),
            4.0,
            samples
        );

        Assert.NotEmpty(samples);
        // first side runs (100,200)->(110,200): count=(int)(10/4)=2, step=10/3
        Assert.Contains(samples, s => System.Math.Abs(s.Position.X - (100 + 10.0 / 3)) < Tol && System.Math.Abs(s.Position.Y - 200) < Tol);
        Assert.Contains(samples, s => System.Math.Abs(s.Position.X - (100 + 20.0 / 3)) < Tol && System.Math.Abs(s.Position.Y - 200) < Tol);
        AssertVector(end, 100, 200, "pen returns to start of closed triangle");
    }

    [Fact]
    public void ProgramMoves_RapidAndSuppressedMovesAdvancePenWithoutSamples()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(50, 50));
        var suppressed = new LinearMove(100, 50) { Suppressed = true };
        pgm.Codes.Add(suppressed);
        pgm.Codes.Add(new LinearMove(100, 100));

        var samples = new List<ContourSample>();
        var end = ContourSampler.ProgramMoves(pgm, new Vector(), new Vector(), 4.0, samples);

        AssertVector(end, 100, 100, "pen after suppressed move");
        Assert.All(samples, s => Assert.True(s.Position.X >= 100 - Tol)); // only the final visible line
        Assert.NotEmpty(samples);
    }

    [Fact]
    public void ProgramMoves_SubProgramExecutesAtBasePlusOffset()
    {
        var hole = new Program();
        hole.Codes.Add(new LinearMove(0, 0));
        hole.Codes.Add(new LinearMove(2, 0));
        hole.Codes.Add(new LinearMove(0, 0));

        var main = new Program();
        main.Codes.Add(new SubProgramCall(hole, 0) { Offset = new Vector(10, 0) });

        var samples = new List<ContourSample>();
        ContourSampler.ProgramMoves(main, new Vector(5, 5), new Vector(), 0.5, samples);

        Assert.NotEmpty(samples);
        // hole geometry lives around x=15, y=5 (basePos + offset), never at origin
        Assert.All(samples, s => Assert.True(s.Position.X >= 15 - Tol && System.Math.Abs(s.Position.Y - 5) < Tol));
    }

    [Fact]
    public void ProgramMoves_IncrementalEndpointsAccumulate()
    {
        var pgm = new Program(Mode.Incremental);
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(0, 10));

        var samples = new List<ContourSample>();
        var end = ContourSampler.ProgramMoves(
            pgm,
            new Vector(),
            new Vector(),
            4.0,
            samples
        );

        AssertVector(end, 10, 10, "incremental pen");
        Assert.Contains(samples, s => s.Position.Y > 0); // second move exists in world space
    }

    [Fact]
    public void ProgramMoves_ArcWalkAccumulatesArclengthAcrossMoves()
    {
        // quarter circle CCW radius 10 from (10,0) to (0,10)
        var pgm = new Program();
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new ArcMove(0, 10, 0, 0, RotationType.CCW));

        var samples = new List<ContourSample>();
        ContourSampler.ProgramMoves(pgm, new Vector(), new Vector(), 2.0, samples);

        Assert.NotEmpty(samples);
        var arcSamples = samples.FindAll(s => s.Position.Y > Tol);
        Assert.NotEmpty(arcSamples);
        // arc arclength continues after the visible first side; radii hold
        Assert.All(arcSamples, s => Assert.True(System.Math.Abs(s.Position.DistanceTo(new Vector()) - 10) < 1e-6));
        var lineSamples = samples.FindAll(s => s.Position.Y <= Tol);
        Assert.NotEmpty(lineSamples);
        Assert.All(lineSamples, s => Assert.True(s.At < 10 + Tol, "line samples carry the walk's arclength origin"));
    }

    // ---------- ring scheduler ----------

    private static readonly Vector[] Square =
    [
        new(0, 0), new(10, 0), new(10, 10), new(0, 10),
    ];

    [Fact]
    public void RingMoves_UniformArclengthSamples()
    {
        var samples = new List<ContourSample>();
        ContourSampler.RingMoves(Square, 2.5, samples);

        Assert.Equal(16, samples.Count); // perimeter 40 / 2.5
        for (var i = 0; i < samples.Count; i++)
        {
            Assert.Equal(2.5 * i, samples[i].At, 7);
            Assert.True(
                System.Math.Abs(System.Math.Sqrt(
                        samples[i].Direction.X * samples[i].Direction.X
                            + samples[i].Direction.Y * samples[i].Direction.Y
                    ) - 1) < Tol,
                "unit direction"
            );
        }
        AssertVector(samples[0].Position, 0, 0, "first sample at ring start");
    }

    [Fact]
    public void RingMoves_IgnoresExplicitClosingVertex()
    {
        var withClose = new List<ContourSample>();
        ContourSampler.RingMoves([.. Square, new Vector(0, 0)], 2.5, withClose);

        var withoutClose = new List<ContourSample>();
        ContourSampler.RingMoves(Square, 2.5, withoutClose);

        Assert.Equal(withoutClose.Count, withClose.Count);
        for (var i = 0; i < withClose.Count; i++)
        {
            AssertVector(withClose[i].Position, withoutClose[i].Position.X, withoutClose[i].Position.Y);
            Assert.Equal(withClose[i].At, withoutClose[i].At, 9);
        }
    }

    [Fact]
    public void RingMoves_StartVertexShiftByWholeStepsKeepsSampleSet()
    {
        // same square started one vertex along (perimeter shift 10 = 4 steps of 2.5)
        var shifted = new[] { Square[1], Square[2], Square[3], Square[0] };

        var a = new List<ContourSample>();
        ContourSampler.RingMoves(Square, 2.5, a);
        var b = new List<ContourSample>();
        ContourSampler.RingMoves(shifted, 2.5, b);

        Assert.Equal(a.Count, b.Count);
        var setA = a
            .Select(s => (X: System.Math.Round(s.Position.X, 6), Y: System.Math.Round(s.Position.Y, 6)))
            .ToHashSet();
        var setB = b
            .Select(s => (X: System.Math.Round(s.Position.X, 6), Y: System.Math.Round(s.Position.Y, 6)))
            .ToHashSet();
        Assert.True(setA.SetEquals(setB));
    }

    [Fact]
    public void RingMoves_ShortSegmentsAdvanceArclengthNotSamplesPerMove()
    {
        // one tiny edge among long edges: every sample still sits on the contour,
        // arclength is strictly increasing, and nothing is omitted
        var ring = new[]
        {
            new Vector(0, 0), new Vector(10, 0), new Vector(10, 0.1), new Vector(0, 0.1),
        };
        var samples = new List<ContourSample>();
        ContourSampler.RingMoves(ring, 1.0, samples);

        var perimeter = 10 + 0.1 + 10 + 0.1;
        Assert.Equal((int)System.Math.Round(perimeter), samples.Count);
        for (var i = 1; i < samples.Count; i++)
            Assert.True(samples[i].At > samples[i - 1].At);
    }

    [Fact]
    public void RingMoves_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(
            () => ContourSampler.RingMoves([new Vector(0, 0), new Vector(1, 1)], 1, new())
        );
        Assert.Throws<ArgumentException>(
            () =>
                ContourSampler.RingMoves(
                    [new Vector(0, 0), new Vector(double.NaN, 1), new Vector(1, 1)],
                    1,
                    new()
                )
        );
        Assert.Throws<ArgumentException>(
            () =>
                ContourSampler.RingMoves(
                    [new Vector(1, 1), new Vector(1, 1), new Vector(1, 1)],
                    1,
                    new()
                )
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContourSampler.RingMoves(Square, 0, new())
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContourSampler.RingMoves(Square, double.NaN, new())
        );
        Assert.Throws<ArgumentNullException>(
            () => ContourSampler.RingMoves(null!, 1, new())
        );
    }
}
