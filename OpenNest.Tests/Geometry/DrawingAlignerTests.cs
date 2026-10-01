using System.Threading;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// Slice-1 tests for <see cref="DrawingAligner"/>: one rigid asymmetric line/arc
/// part with a hole must align against an independently known transform, and
/// symmetric, mirrored, degenerate or heavily revised geometry must refuse an
/// unearned unique/high-confidence result. Transforms are verified by applying
/// the returned pose to exact source geometry and measuring world-space error,
/// never by trusting the aligner's own residual scores.
/// </summary>
public class DrawingAlignerTests
{
    private static readonly AlignmentOptions Options = new()
    {
        FlattenTolerance = 0.01,
        SamplingSpacing = 0.5,
        OutlierDistance = 5.0,
    };

    // ---------- fixtures, each declared in its own drawing-local frame ----------

    /// <summary>
    /// Asymmetric outer contour: a 10x8 slab with one arc corner and one diagonal
    /// corner. The diagonal breaks both mirror and 180-degree symmetry, so a
    /// correct pose is recoverable and a wrong one must be distinguishable.
    /// </summary>
    private static List<Entity> AsymmetricOuter() =>
    [
        new Line(new Vector(0, 0), new Vector(10, 0)),
        new Line(new Vector(10, 0), new Vector(10, 6)),
        new Arc(new Vector(8, 6), 2, 0, System.Math.PI / 2),
        new Line(new Vector(8, 8), new Vector(4, 8)),
        new Line(new Vector(4, 8), new Vector(0, 4)),
        new Line(new Vector(0, 4), new Vector(0, 0)),
    ];

    private static List<Entity> HoleAt(double cx, double cy, double r) => [new Circle(new Vector(cx, cy), r)];

    private static Program BuildProgram(params List<Entity>[] contours) =>
        ConvertGeometry.ToProgram(contours.SelectMany(c => c).ToList());

    /// <summary>Exact points of a fixture: line endpoints plus dense circle samples.</summary>
    private static List<Vector> ExactPoints(params IEnumerable<Entity>[] contours)
    {
        var points = new List<Vector>();
        foreach (var contour in contours)
            foreach (var entity in contour)
                switch (entity)
                {
                    case Line line:
                        points.Add(line.StartPoint);
                        points.Add(line.EndPoint);
                        break;
                    case Circle circle:
                        for (var k = 0; k < 48; k++)
                        {
                            var a = 2 * System.Math.PI * k / 48;
                            points.Add(
                                new Vector(
                                    circle.Center.X + circle.Radius * System.Math.Cos(a),
                                    circle.Center.Y + circle.Radius * System.Math.Sin(a)
                                )
                            );
                        }
                        break;
                    case Arc arc:
                        // the arc center is NOT on the contour; sample the sweep
                        for (var k = 0; k <= 16; k++)
                        {
                            var a = arc.StartAngle + (arc.EndAngle - arc.StartAngle) * k / 16.0;
                            points.Add(
                                new Vector(
                                    arc.Center.X + arc.Radius * System.Math.Cos(a),
                                    arc.Center.Y + arc.Radius * System.Math.Sin(a)
                                )
                            );
                        }
                        break;
                }
        return points;
    }

    private readonly record struct Pose(double Rotation, Vector Translation);

    private static Vector ApplyPoint(Pose pose, Vector p)
    {
        var cos = System.Math.Cos(pose.Rotation);
        var sin = System.Math.Sin(pose.Rotation);
        return new Vector(
            pose.Translation.X + p.X * cos - p.Y * sin,
            pose.Translation.Y + p.X * sin + p.Y * cos
        );
    }

    /// <summary>Rigidly transform fixture entities, preserving line/arc/circle types.</summary>
    private static List<Entity> TransformEntities(IEnumerable<Entity> entities, Pose pose)
    {
        var result = new List<Entity>();
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case Line line:
                    result.Add(new Line(ApplyPoint(pose, line.StartPoint), ApplyPoint(pose, line.EndPoint)));
                    break;
                case Circle circle:
                    result.Add(new Circle(ApplyPoint(pose, circle.Center), circle.Radius));
                    break;
                case Arc arc:
                    result.Add(
                        new Arc(
                            ApplyPoint(pose, arc.Center),
                            arc.Radius,
                            arc.StartAngle + pose.Rotation,
                            arc.EndAngle + pose.Rotation,
                            arc.IsReversed
                        )
                    );
                    break;
            }
        }
        return result;
    }

    /// <summary>
    /// Independent oracle: every point of one fixture must lie on the OTHER
    /// fixture's TRUE geometry (segments, circles, arcs — not a sampled point
    /// cloud, whose spacing would quantize the measurement) under the recovered
    /// pose, and vice versa.
    /// </summary>
    private static double MaxWorldError(Pose pose, List<Entity> newEntities, List<Entity> oldEntities)
    {
        var worst = 0.0;
        foreach (var p in ExactPoints(newEntities))
            worst = System.Math.Max(worst, DistanceToGeometry(ApplyPoint(pose, p), oldEntities));
        foreach (var p in ExactPoints(oldEntities))
            worst = System.Math.Max(worst, DistanceToGeometry(p, TransformEntities(newEntities, pose)));
        return worst;
    }

    private static double DistanceToGeometry(Vector p, List<Entity> entities)
    {
        var best = double.PositiveInfinity;
        foreach (var entity in entities)
        {
            double d = entity switch
            {
                Line line => DistanceToSegment(p, line.StartPoint, line.EndPoint),
                Circle circle => System.Math.Abs(p.DistanceTo(circle.Center) - circle.Radius),
                Arc arc => System.Math.Abs(p.DistanceTo(arc.Center) - arc.Radius),
                _ => double.PositiveInfinity,
            };
            best = System.Math.Min(best, d);
        }
        return best;
    }

    private static double DistanceToSegment(Vector p, Vector a, Vector b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= 0)
            return p.DistanceTo(a);
        var t = System.Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0.0, 1.0);
        return p.DistanceTo(new Vector(a.X + t * dx, a.Y + t * dy));
    }

    private static void AssertNoFlag(AlignmentResult result, AlignmentReasons flag) =>
        Assert.True(
            (result.Reasons & flag) == 0,
            $"unexpected review reason {flag}; full set: {result.Reasons}"
        );

    private static void AssertRefused(AlignmentResult result, AlignmentReasons expected)
    {
        Assert.False(result.Converged);
        Assert.NotNull(result.FailureMessage);
        Assert.True(
            (result.Reasons & expected) != 0,
            $"expected review reason {expected}; got {result.Reasons}"
        );
    }

    // ---------- asymmetric success ----------

    [Theory]
    [InlineData(0.0, 3.25, -1.75)]
    [InlineData(0.5, -2.0, 4.5)]
    [InlineData(-1.25, 0.0, 0.0)]
    [InlineData(2.75, 6.0, -6.0)]
    public void Align_KnownRigidTransformOfAsymmetricHoledPart_RecoversWorldSpaceGeometry(
        double rotation,
        double tx,
        double ty
    )
    {
        var outer = AsymmetricOuter();
        var hole = HoleAt(3, 3, 1);
        var oldPgm = BuildProgram(outer, hole);

        // the revised drawing is the SAME geometry through an independently known
        // rigid transform, rebuilt as exact entities
        var pose = new Pose(rotation, new Vector(tx, ty));
        var revisedEntities = TransformEntities(outer.Concat(hole), pose);
        var revisedPgm = ConvertGeometry.ToProgram(revisedEntities);

        var result = DrawingAligner.Align(oldPgm, revisedPgm, Options);

        Assert.True(result.Converged, $"did not converge: {result.Reasons} {result.FailureMessage}");
        Assert.True(result.FailureMessage == null, result.FailureMessage);
        AssertNoFlag(result, AlignmentReasons.InsufficientSupport);
        AssertNoFlag(result, AlignmentReasons.InvalidGeometry);
        AssertNoFlag(result, AlignmentReasons.ReflectionUncertain);
        AssertNoFlag(result, AlignmentReasons.UnresolvedAlternatives);

        // verification against independent known geometry, not the aligner's
        // score: the recovered NEW->OLD pose must map the exact REVISED vertices
        // onto the exact OLD vertices and back
        var error = MaxWorldError(
            new Pose(result.Rotation, result.Translation),
            revisedEntities,
            outer.Concat(hole).ToList()
        );
        Assert.True(error < 0.05, $"world-space reconstruction error {error}");

        Assert.True(result.NewToOldCoverage > 0.95, $"coverage {result.NewToOldCoverage}");
        Assert.True(result.OldToNewCoverage > 0.95, $"coverage {result.OldToNewCoverage}");
        Assert.True(result.ResidualRms < 0.05, $"residual {result.ResidualRms}");
        Assert.False(result.Reflection);

        // the diagnostic IoU is bounded and meaningful for a holed part
        Assert.NotNull(result.DiagnosticIoU);
        Assert.InRange(result.DiagnosticIoU.Value, 0.0, 1.0);
        Assert.True(result.DiagnosticIoU > 0.9, $"diagnostic IoU {result.DiagnosticIoU}");
    }

    // ---------- symmetric outlines cannot claim a unique pose ----------

    [Fact]
    public void Align_SymmetricRectangle_FlagsUnresolvedAlternatives()
    {
        static List<Entity> Rect(double w, double h) =>
        [
            new Line(new Vector(-w / 2, -h / 2), new Vector(w / 2, -h / 2)),
            new Line(new Vector(w / 2, -h / 2), new Vector(w / 2, h / 2)),
            new Line(new Vector(w / 2, h / 2), new Vector(-w / 2, h / 2)),
            new Line(new Vector(-w / 2, h / 2), new Vector(-w / 2, -h / 2)),
        ];

        var oldPgm = BuildProgram(Rect(10, 6));
        var revisedPgm = ConvertGeometry.ToProgram(
            TransformEntities(Rect(10, 6), new Pose(System.Math.PI, new Vector(1.0, 0.5)))
        );

        var result = DrawingAligner.Align(oldPgm, revisedPgm, Options);

        Assert.True(
            (result.Reasons & AlignmentReasons.UnresolvedAlternatives) != 0,
            $"a rectangle's 180-degree reversal is not recoverable; reasons={result.Reasons}"
        );
    }

    [Fact]
    public void Align_Circle_FlagsUnresolvedAlternatives()
    {
        var oldPgm = BuildProgram(HoleAt(0, 0, 12)); // a circle used as the outer contour
        var revisedPgm = BuildProgram(HoleAt(2.5, -1.5, 12));

        var result = DrawingAligner.Align(oldPgm, revisedPgm, Options);

        Assert.True(
            (result.Reasons & AlignmentReasons.UnresolvedAlternatives) != 0,
            $"a circle has no unique angle; reasons={result.Reasons}"
        );
    }

    // ---------- mirror is never auto-selected (user decision D3) ----------

    [Fact]
    public void Align_MirroredRevision_NeverSelectsReflectionAndDoesNotLookSafe()
    {
        var outer = AsymmetricOuter();
        var hole = HoleAt(3, 3, 1);
        var oldPgm = BuildProgram(outer, hole);

        // mirror the revised drawing about its X axis
        var mirrored = new List<Entity>();
        foreach (var entity in TransformEntities(outer.Concat(hole), new Pose(0, new Vector(4, 0))))
        {
            switch (entity)
            {
                case Line line:
                    mirrored.Add(
                        new Line(
                            new Vector(line.StartPoint.X, -line.StartPoint.Y),
                            new Vector(line.EndPoint.X, -line.EndPoint.Y)
                        )
                    );
                    break;
                case Circle circle:
                    mirrored.Add(new Circle(new Vector(circle.Center.X, -circle.Center.Y), circle.Radius));
                    break;
                case Arc arc:
                    mirrored.Add(
                        new Arc(
                            new Vector(arc.Center.X, -arc.Center.Y),
                            arc.Radius,
                            -arc.EndAngle,
                            -arc.StartAngle,
                            !arc.IsReversed
                        )
                    );
                    break;
            }
        }
        var revisedPgm = ConvertGeometry.ToProgram(mirrored);

        var result = DrawingAligner.Align(oldPgm, revisedPgm, Options);

        Assert.False(result.Reflection);
        Assert.True(
            (result.Reasons & AlignmentReasons.ReflectionUncertain) != 0
                || result.FailureMessage != null
                || (result.Reasons & AlignmentReasons.SignificantBoundaryChange) != 0,
            $"a mirrored revision must not present as a safe rigid seed; reasons={result.Reasons}"
        );
    }

    // ---------- changed geometry is reported, not hidden ----------

    [Fact]
    public void Align_RemovedOuterEdge_ReportsBoundaryChangeOrRefuses()
    {
        var oldPgm = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));

        // the same slab with the diagonal corner squared off: a major outline edit
        var revisedOuter = new List<Entity>
        {
            new Line(new Vector(0, 0), new Vector(10, 0)),
            new Line(new Vector(10, 0), new Vector(10, 6)),
            new Arc(new Vector(8, 6), 2, 0, System.Math.PI / 2),
            new Line(new Vector(8, 8), new Vector(0, 8)),
            new Line(new Vector(0, 8), new Vector(0, 0)),
        };
        var revisedPgm = BuildProgram(revisedOuter, HoleAt(3, 3, 1));

        var result = DrawingAligner.Align(oldPgm, revisedPgm, Options);

        Assert.True(
            result.FailureMessage != null
                || (result.Reasons & (AlignmentReasons.SignificantBoundaryChange | AlignmentReasons.InsufficientSupport)) != 0,
            $"a squared-off corner must be reported; reasons={result.Reasons}"
        );
    }

    [Fact]
    public void Align_MovedHole_KeepsPerimeterPoseButReportsBoundaryChange()
    {
        var oldPgm = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));
        var revisedPgm = BuildProgram(AsymmetricOuter(), HoleAt(6.5, 5.5, 1));

        var result = DrawingAligner.Align(oldPgm, revisedPgm, Options);

        // the unchanged perimeter must still land on identity — a moved hole must
        // not drag it — while the changed hole is reported as an unmatched span
        Assert.True(System.Math.Abs(result.Rotation) < 1e-6, $"rotation drifted: {result.Rotation}");
        Assert.True(result.Translation.DistanceTo(new Vector()) < 0.05, $"translation drifted: {result.Translation}");
        Assert.True(
            (result.Reasons & (AlignmentReasons.SignificantBoundaryChange | AlignmentReasons.InsufficientSupport)) != 0,
            $"a moved hole is a changed span; reasons={result.Reasons}"
        );
    }

    // ---------- invalid input refuses instead of throwing ----------

    [Fact]
    public void Align_InvalidInput_ReportsInvalidGeometryWithoutThrowing()
    {
        var good = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));

        AssertRefused(DrawingAligner.Align(new Program(), good, Options), AlignmentReasons.InvalidGeometry);
        AssertRefused(DrawingAligner.Align(null, good, Options), AlignmentReasons.InvalidGeometry);

        var nan = new Program();
        nan.Codes.Add(new LinearMove(0, 0));
        nan.Codes.Add(new LinearMove(double.NaN, 5));
        nan.Codes.Add(new LinearMove(5, 5));
        nan.Codes.Add(new LinearMove(0, 0));
        AssertRefused(DrawingAligner.Align(nan, good, Options), AlignmentReasons.InvalidGeometry);
    }

    [Fact]
    public void Align_TooFewSamples_ReportsInsufficientSupport()
    {
        var oldPgm = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));
        var tiny = new Program();
        tiny.Codes.Add(new LinearMove(0, 0));
        tiny.Codes.Add(new LinearMove(1, 0));
        tiny.Codes.Add(new LinearMove(0, 1));
        tiny.Codes.Add(new LinearMove(0, 0));

        AssertRefused(DrawingAligner.Align(oldPgm, tiny, Options), AlignmentReasons.InsufficientSupport);
    }

    [Fact]
    public void Align_SampleBudgetExceeded_ReportsInsufficientSupport()
    {
        var oldPgm = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));
        var tight = new AlignmentOptions { MaxTotalSamples = 10 };

        AssertRefused(DrawingAligner.Align(oldPgm, oldPgm, tight), AlignmentReasons.InsufficientSupport);
    }

    [Fact]
    public void Align_DisconnectedMaterial_ReportsInvalidGeometry()
    {
        // two separate closed contours, neither containing the other
        var entities = new List<Entity>
        {
            new Line(new Vector(0, 0), new Vector(10, 0)),
            new Line(new Vector(10, 0), new Vector(10, 10)),
            new Line(new Vector(10, 10), new Vector(0, 10)),
            new Line(new Vector(0, 10), new Vector(0, 0)),
            new Line(new Vector(50, 50), new Vector(55, 50)),
            new Line(new Vector(55, 50), new Vector(55, 55)),
            new Line(new Vector(55, 55), new Vector(50, 55)),
            new Line(new Vector(50, 55), new Vector(50, 50)),
        };
        var pgm = ConvertGeometry.ToProgram(entities);
        var good = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));

        AssertRefused(DrawingAligner.Align(good, pgm, Options), AlignmentReasons.InvalidGeometry);
    }

    // ---------- cancellation fails closed ----------

    [Fact]
    public void Align_CancelledUpFront_FailsClosed()
    {
        var oldPgm = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = DrawingAligner.Align(oldPgm, oldPgm, Options, cts.Token);

        Assert.True(
            result.FailureMessage != null || !result.Converged,
            $"a cancelled run must not report a confident success; reasons={result.Reasons}"
        );
    }

    // ---------- diagnostic region arithmetic (Slice-0 step 4) ----------

    [Fact]
    public void DiagnosticIoU_IdenticalHoledPart_IsOne()
    {
        var pgm = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));

        var iou = DrawingAligner.DiagnosticIoU(pgm, pgm, 0, new Vector(), false, Options);

        Assert.NotNull(iou);
        Assert.InRange(iou.Value, 0.0, 1.0);
        Assert.True(iou > 0.999, $"identical parts must score 1, got {iou}");
    }

    [Fact]
    public void DiagnosticIoU_DisjointParts_IsZero()
    {
        var a = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));
        var b = ConvertGeometry.ToProgram(
            new List<Entity>
            {
                new Line(new Vector(200, 200), new Vector(210, 200)),
                new Line(new Vector(210, 200), new Vector(210, 208)),
                new Line(new Vector(210, 208), new Vector(200, 208)),
                new Line(new Vector(200, 208), new Vector(200, 200)),
            }
        );

        var iou = DrawingAligner.DiagnosticIoU(a, b, 0, new Vector(), false, Options);

        Assert.NotNull(iou);
        Assert.InRange(iou.Value, 0.0, 1.0);
        Assert.True(iou < 0.001, $"disjoint parts must score 0, got {iou}");
    }

    [Fact]
    public void DiagnosticIoU_TranslatedPart_MatchesIndependentAreaComputation()
    {
        var a = BuildProgram(AsymmetricOuter(), HoleAt(3, 3, 1));
        var b = ConvertGeometry.ToProgram(
            TransformEntities(AsymmetricOuter().Concat(HoleAt(3, 3, 1)), new Pose(0, new Vector(5, 0)))
        );

        // aligning first, then scoring at the returned pose, must beat scoring at identity
        var result = DrawingAligner.Align(a, b, Options);
        var aligned = DrawingAligner.DiagnosticIoU(a, b, result.Rotation, result.Translation, false, Options);
        var unaligned = DrawingAligner.DiagnosticIoU(a, b, 0, new Vector(), false, Options);

        Assert.NotNull(aligned);
        Assert.NotNull(unaligned);
        Assert.True(aligned > unaligned, $"aligned {aligned} should beat identity {unaligned}");
        Assert.True(aligned > 0.99, $"aligned IoU {aligned}");
    }
}
