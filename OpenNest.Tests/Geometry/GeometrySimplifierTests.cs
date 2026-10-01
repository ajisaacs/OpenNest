using System.Linq;
using OpenNest.Geometry;
using OpenNest.IO;
using Xunit;

namespace OpenNest.Tests.Geometry;

public class GeometrySimplifierTests
{
    [Fact]
    public void Analyze_LinesFromSemicircle_FindsOneCandidate()
    {
        // Create 20 lines approximating a semicircle of radius 10
        var arc = new Arc(new Vector(0, 0), 10, 0, System.Math.PI, false);
        var points = arc.ToPoints(20);
        var shape = new Shape();
        for (var i = 0; i < points.Count - 1; i++)
            shape.Entities.Add(new Line(points[i], points[i + 1]));

        var simplifier = new GeometrySimplifier { Tolerance = 0.1 };
        var candidates = simplifier.Analyze(shape);

        Assert.Single(candidates);
        Assert.Equal(0, candidates[0].StartIndex);
        Assert.Equal(19, candidates[0].EndIndex);
        Assert.Equal(20, candidates[0].LineCount);
        Assert.InRange(candidates[0].FittedArc.Radius, 9.5, 10.5);
        Assert.True(candidates[0].MaxDeviation <= 0.1);
    }

    [Fact]
    public void Analyze_TooFewLines_ReturnsNoCandidates()
    {
        // Only 2 consecutive lines — below MinLines threshold
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(1, 1)));
        shape.Entities.Add(new Line(new Vector(1, 1), new Vector(2, 0)));

        var simplifier = new GeometrySimplifier { Tolerance = 0.1, MinLines = 3 };
        var candidates = simplifier.Analyze(shape);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Analyze_MixedEntitiesWithArc_FindsSeparateCandidates()
    {
        // Lines on one curve, then an arc at a different center, then lines on another curve
        // The arc is included in the run but can't merge with lines on different curves
        var shape = new Shape();
        // First run: 5 lines on a curve
        var arc1 = new Arc(new Vector(0, 0), 10, 0, System.Math.PI / 2, false);
        var pts1 = arc1.ToPoints(5);
        for (var i = 0; i < pts1.Count - 1; i++)
            shape.Entities.Add(new Line(pts1[i], pts1[i + 1]));

        // An existing arc entity (breaks the run)
        shape.Entities.Add(new Arc(new Vector(20, 0), 5, 0, System.Math.PI, false));

        // Second run: 4 lines on a different curve
        var arc2 = new Arc(new Vector(30, 0), 8, 0, System.Math.PI / 3, false);
        var pts2 = arc2.ToPoints(4);
        for (var i = 0; i < pts2.Count - 1; i++)
            shape.Entities.Add(new Line(pts2[i], pts2[i + 1]));

        var simplifier = new GeometrySimplifier { Tolerance = 0.5, MinLines = 3 };
        var candidates = simplifier.Analyze(shape);

        Assert.Equal(2, candidates.Count);
        // First candidate covers indices 0-4 (5 lines)
        Assert.Equal(0, candidates[0].StartIndex);
        Assert.Equal(4, candidates[0].EndIndex);
        // Second candidate covers indices 6-9 (4 lines, after the arc at index 5)
        Assert.Equal(6, candidates[1].StartIndex);
        Assert.Equal(9, candidates[1].EndIndex);
    }

    [Fact]
    public void Apply_SingleCandidate_ReplacesLinesWithArc()
    {
        // 20 lines approximating a semicircle
        var arc = new Arc(new Vector(0, 0), 10, 0, System.Math.PI, false);
        var points = arc.ToPoints(20);
        var shape = new Shape();
        for (var i = 0; i < points.Count - 1; i++)
            shape.Entities.Add(new Line(points[i], points[i + 1]));

        var simplifier = new GeometrySimplifier { Tolerance = 0.1 };
        var candidates = simplifier.Analyze(shape);
        var result = simplifier.Apply(shape, candidates);

        Assert.Single(result.Entities);
        Assert.IsType<Arc>(result.Entities[0]);
    }

    [Fact]
    public void Apply_OnlySelectedCandidates_LeavesUnselectedAsLines()
    {
        // Two runs of lines with an arc between them
        var shape = new Shape();
        var arc1 = new Arc(new Vector(0, 0), 10, 0, System.Math.PI / 2, false);
        var pts1 = arc1.ToPoints(5);
        for (var i = 0; i < pts1.Count - 1; i++)
            shape.Entities.Add(new Line(pts1[i], pts1[i + 1]));

        shape.Entities.Add(new Arc(new Vector(20, 0), 5, 0, System.Math.PI, false));

        var arc2 = new Arc(new Vector(30, 0), 8, 0, System.Math.PI / 3, false);
        var pts2 = arc2.ToPoints(4);
        for (var i = 0; i < pts2.Count - 1; i++)
            shape.Entities.Add(new Line(pts2[i], pts2[i + 1]));

        var simplifier = new GeometrySimplifier { Tolerance = 0.5, MinLines = 3 };
        var candidates = simplifier.Analyze(shape);

        // Deselect the first candidate
        candidates[0].IsSelected = false;

        var result = simplifier.Apply(shape, candidates);

        // First run (5 lines) stays as lines + middle arc + second run replaced by arc
        // 5 original lines + 1 original arc + 1 fitted arc = 7 entities
        Assert.Equal(7, result.Entities.Count);
        // First 5 should be lines
        for (var i = 0; i < 5; i++)
            Assert.IsType<Line>(result.Entities[i]);
        // Index 5 is the original arc
        Assert.IsType<Arc>(result.Entities[5]);
        // Index 6 is the fitted arc replacing the second run
        Assert.IsType<Arc>(result.Entities[6]);
    }

    [Fact]
    public void Analyze_FilletBetweenTangentLines_ArcIsTangentToLines()
    {
        // A 90-degree fillet (r=0.3, center origin, 270deg..360deg CCW) between two
        // long tangent lines, approximated by 8 chords whose interior vertices bulge
        // radially outward within tolerance (simulates real DXF tessellation noise).
        var r = 0.3;
        var deltas = new[] { 0.0, 0.002, 0.003, 0.0035, 0.0035, 0.0035, 0.003, 0.002, 0.0 };
        var pts = new List<Vector>();
        for (var i = 0; i <= 8; i++)
        {
            var ang = OpenNest.Math.Angle.ToRadians(270 + 11.25 * i);
            var radius = r + deltas[i];
            pts.Add(new Vector(radius * System.Math.Cos(ang), radius * System.Math.Sin(ang)));
        }

        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(-2, -r), pts[0]));
        for (var i = 0; i < pts.Count - 1; i++)
            shape.Entities.Add(new Line(pts[i], pts[i + 1]));
        shape.Entities.Add(new Line(pts[^1], new Vector(r, 2)));

        var simplifier = new GeometrySimplifier { Tolerance = 0.004 };
        var candidates = simplifier.Analyze(shape);

        Assert.Single(candidates);
        var arc = candidates[0].FittedArc;

        // Arc must pass exactly through the run's boundary vertices (no gaps)
        Assert.True(arc.StartPoint().DistanceTo(pts[0]) < 1e-6);
        Assert.True(arc.EndPoint().DistanceTo(pts[^1]) < 1e-6);

        // Arc must be tangent to the adjacent straight edges at its endpoints
        var startDelta = AngleBetweenDeg(ArcTangentAt(arc, arc.StartPoint()), new Vector(1, 0));
        var endDelta = AngleBetweenDeg(ArcTangentAt(arc, arc.EndPoint()), new Vector(0, 1));
        Assert.True(
            startDelta < 0.3,
            $"Arc start not tangent to incoming line: off by {startDelta:F3} deg"
        );
        Assert.True(
            endDelta < 0.3,
            $"Arc end not tangent to outgoing line: off by {endDelta:F3} deg"
        );
    }

    [Fact]
    public void Analyze_CompoundCurve_AdjacentArcsAreTangentAtJunction()
    {
        // Two tangent-continuous arcs of different radii (r=0.2 sweeping 60deg, then
        // r=0.6 sweeping 40deg), tessellated into chords with slight radial noise.
        // The fitted arcs must stay tangent-continuous at their junction.
        var c1 = new Vector(0, 0);
        var r1 = 0.2;
        var deltas1 = new[] { 0.0, 0.001, 0.0005, -0.0005, -0.001, -0.0005, 0.0 };
        var pts = new List<Vector>();
        for (var i = 0; i <= 6; i++)
        {
            var ang = OpenNest.Math.Angle.ToRadians(10 * i);
            var radius = r1 + deltas1[i];
            pts.Add(
                new Vector(
                    c1.X + radius * System.Math.Cos(ang),
                    c1.Y + radius * System.Math.Sin(ang)
                )
            );
        }

        // Second arc center along the junction radius so tangents match at the junction
        var junctionAngle = OpenNest.Math.Angle.ToRadians(60);
        var u = new Vector(System.Math.Cos(junctionAngle), System.Math.Sin(junctionAngle));
        var r2 = 0.6;
        var c2 = new Vector(c1.X + u.X * (r1 - r2), c1.Y + u.Y * (r1 - r2));
        var deltas2 = new[] { 0.0, 0.001, -0.001, 0.0005, -0.0005, 0.0 };
        for (var i = 1; i <= 5; i++)
        {
            var ang = OpenNest.Math.Angle.ToRadians(60 + 8 * i);
            var radius = r2 + deltas2[i];
            pts.Add(
                new Vector(
                    c2.X + radius * System.Math.Cos(ang),
                    c2.Y + radius * System.Math.Sin(ang)
                )
            );
        }

        var shape = new Shape();
        for (var i = 0; i < pts.Count - 1; i++)
            shape.Entities.Add(new Line(pts[i], pts[i + 1]));

        var simplifier = new GeometrySimplifier { Tolerance = 0.004 };
        var candidates = simplifier.Analyze(shape);

        Assert.Equal(2, candidates.Count);
        var arcA = candidates[0].FittedArc;
        var arcB = candidates[1].FittedArc;

        // Arcs must share the junction vertex exactly
        Assert.True(arcA.EndPoint().DistanceTo(arcB.StartPoint()) < 1e-6);

        // Tangent continuity across the junction
        var junctionDelta = AngleBetweenDeg(
            ArcTangentAt(arcA, arcA.EndPoint()),
            ArcTangentAt(arcB, arcB.StartPoint())
        );
        Assert.True(
            junctionDelta < 0.3,
            $"Tangent break of {junctionDelta:F3} deg at arc-arc junction"
        );
    }

    // Frozen Analyze + Apply output from 5bf3c5f. The circles are order markers;
    // the rotated ellipse also pins the two trailing lines left unfitted.
    [Theory]
    [MemberData(nameof(SignedAngleCharacterizationCases))]
    public void Apply_SignedAngleCharacterization_PreservesOrderedEntities(
        string scenario, double[][] expected, bool[] reversed, int expectedEndIndex
    )
    {
        var points = SplineConverterTests.SignedAngleCharacterizationPoints(scenario);
        var shape = new Shape();
        shape.Entities.Add(new Circle(new Vector(-30, 40), 2.5));
        for (var i = 0; i < points.Count - 1; i++)
            shape.Entities.Add(new Line(points[i], points[i + 1]));
        shape.Entities.Add(new Circle(new Vector(30, -40), 3.5));
        var simplifier = new GeometrySimplifier { Tolerance = 0.05 };

        var candidate = Assert.Single(simplifier.Analyze(shape));
        Assert.Equal(1, candidate.StartIndex);
        Assert.Equal(expectedEndIndex, candidate.EndIndex);
        var result = simplifier.Apply(shape, new List<ArcCandidate> { candidate });
        var assertions = new List<Action<Entity>>
        {
            entity => AssertCharacterizedCircle(entity, -30, 40, 2.5),
            entity => AssertCharacterizedArc(entity, expected[0], reversed[0]),
        };
        if (scenario == "rotated-partial-ellipse")
        {
            assertions.Add(entity => AssertCharacterizedLine(entity,
                new[] { 2.644054114237731, 2.288101855169011, 2.3955895038427424, 2.1920946874414766 }));
            assertions.Add(entity => AssertCharacterizedLine(entity,
                new[] { 2.3955895038427424, 2.1920946874414766, 2.148394352966971, 2.087282751199381 }));
        }
        assertions.Add(entity => AssertCharacterizedCircle(entity, 30, -40, 3.5));
        Assert.Collection(result.Entities, assertions.ToArray());
    }

    public static IEnumerable<object[]> SignedAngleCharacterizationCases()
    {
        yield return new object[]
        {
            "ccw",
            new double[][]
            {
                new[] { 2.999999999999998, -2.000000000000008, 5.000000000000004, 0.30000000000000143, 2.2999999999999985 },
            },
            new[] { false },
            24,
        };
        yield return new object[]
        {
            "cw",
            new double[][]
            {
                new[] { 3.0000000000000053, -1.9999999999999813, 4.999999999999989, 2.300000000000003, 0.2999999999999968 },
            },
            new[] { true },
            24,
        };
        yield return new object[]
        {
            "reversed",
            new double[][]
            {
                new[] { 2.999999999999998, -2.000000000000008, 5.000000000000004, 2.2999999999999985, 0.30000000000000143 },
            },
            new[] { true },
            24,
        };
        yield return new object[]
        {
            "atan-seam",
            new double[][]
            {
                new[] { 2.9999999999999436, -2.0000000000000098, 4.999999999999949, 2.799999999999994, 3.8000000000000047 },
            },
            new[] { false },
            24,
        };
        yield return new object[]
        {
            "positive-x-seam",
            new double[][]
            {
                new[] { 2.9999999999999565, -2.000000000000001, 5.00000000000004, 5.800000000000003, 0.5168146928204091 },
            },
            new[] { false },
            24,
        };
        yield return new object[]
        {
            "rotated-partial-ellipse",
            new double[][]
            {
                new[] { 4.738877816517773, -2.224586867329932, 4.975203096337214, 1.0053406925186938, 2.0054021506562583 },
            },
            new[] { false },
            22,
        };
    }

    private static void AssertCharacterizedArc(Entity entity, double[] expected, bool reversed)
    {
        var arc = Assert.IsType<Arc>(entity);
        var actual = new[]
        {
            arc.Center.X, arc.Center.Y, arc.Radius, arc.StartAngle, arc.EndAngle,
        };
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(actual[i], expected[i] - 1e-10, expected[i] + 1e-10);
        Assert.Equal(reversed, arc.IsReversed);
    }

    private static void AssertCharacterizedCircle(Entity entity, double x, double y, double radius)
    {
        var circle = Assert.IsType<Circle>(entity);
        Assert.Equal(x, circle.Center.X);
        Assert.Equal(y, circle.Center.Y);
        Assert.Equal(radius, circle.Radius);
    }

    private static void AssertCharacterizedLine(Entity entity, double[] expected)
    {
        var line = Assert.IsType<Line>(entity);
        var actual = new[] { line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y };
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(actual[i], expected[i] - 1e-10, expected[i] + 1e-10);
    }

    private static Vector ArcTangentAt(Arc arc, Vector pt)
    {
        var ang = System.Math.Atan2(pt.Y - arc.Center.Y, pt.X - arc.Center.X);
        return arc.IsReversed
            ? new Vector(System.Math.Sin(ang), -System.Math.Cos(ang))
            : new Vector(-System.Math.Sin(ang), System.Math.Cos(ang));
    }

    private static double AngleBetweenDeg(Vector v1, Vector v2)
    {
        var l1 = System.Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y);
        var l2 = System.Math.Sqrt(v2.X * v2.X + v2.Y * v2.Y);
        var dot = (v1.X * v2.X + v1.Y * v2.Y) / (l1 * l2);
        dot = System.Math.Max(-1, System.Math.Min(1, dot));
        return System.Math.Acos(dot) * 180.0 / System.Math.PI;
    }

    /// <summary>
    /// Optional real-drawing check. The DXF stays outside the repository; set
    /// "SimplifierGapDxfPath" in OpenNest.Tests/test-config.json to run it.
    /// </summary>
    [SkippableFact]
    public void Apply_RealDxf_NoGapsAfterSimplification()
    {
        var path = TestConfig.GetExistingPath("SimplifierGapDxfPath");
        Skip.If(path == null, "SimplifierGapDxfPath not configured in test-config.json or file not found");

        var result = Dxf.Import(path);
        var shapes = ShapeBuilder.GetShapes(result.Entities);

        var simplifier = new GeometrySimplifier { Tolerance = 0.004 };

        foreach (var shape in shapes)
        {
            var candidates = simplifier.Analyze(shape);
            if (candidates.Count == 0)
                continue;

            var simplified = simplifier.Apply(shape, candidates);

            // Check for gaps between consecutive entities
            for (var i = 0; i < simplified.Entities.Count - 1; i++)
            {
                var current = simplified.Entities[i];
                var next = simplified.Entities[i + 1];

                var currentEnd = current switch
                {
                    Line l => l.EndPoint,
                    Arc a => a.EndPoint(),
                    _ => Vector.Invalid,
                };
                var nextStart = next switch
                {
                    Line l => l.StartPoint,
                    Arc a => a.StartPoint(),
                    _ => Vector.Invalid,
                };

                if (!currentEnd.IsValid() || !nextStart.IsValid())
                    continue;

                var gap = currentEnd.DistanceTo(nextStart);
                Assert.True(
                    gap < 0.005,
                    $"Gap of {gap:F4} between entities {i} ({current.GetType().Name}) and {i + 1} ({next.GetType().Name})"
                );
            }
        }
    }
}
