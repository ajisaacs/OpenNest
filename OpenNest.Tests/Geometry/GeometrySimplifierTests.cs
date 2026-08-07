using OpenNest.Geometry;
using OpenNest.IO;
using System.IO;
using System.Linq;
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
        Assert.True(startDelta < 0.3, $"Arc start not tangent to incoming line: off by {startDelta:F3} deg");
        Assert.True(endDelta < 0.3, $"Arc end not tangent to outgoing line: off by {endDelta:F3} deg");
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
            pts.Add(new Vector(c1.X + radius * System.Math.Cos(ang), c1.Y + radius * System.Math.Sin(ang)));
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
            pts.Add(new Vector(c2.X + radius * System.Math.Cos(ang), c2.Y + radius * System.Math.Sin(ang)));
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
        var junctionDelta = AngleBetweenDeg(ArcTangentAt(arcA, arcA.EndPoint()), ArcTangentAt(arcB, arcB.StartPoint()));
        Assert.True(junctionDelta < 0.3, $"Tangent break of {junctionDelta:F3} deg at arc-arc junction");
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

    [Fact]
    public void Apply_DynaPanDxf_NoGapsAfterSimplification()
    {
        var path = @"C:\Users\aisaacs\Desktop\Sullys Q29 DXFs\SULLYS-031 Dyna Pan.dxf";
        if (!File.Exists(path))
            return; // skip if file not available

        var result = Dxf.Import(path);
        var shapes = ShapeBuilder.GetShapes(result.Entities);

        var simplifier = new GeometrySimplifier { Tolerance = 0.004 };

        foreach (var shape in shapes)
        {
            var candidates = simplifier.Analyze(shape);
            if (candidates.Count == 0) continue;

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
                    _ => Vector.Invalid
                };
                var nextStart = next switch
                {
                    Line l => l.StartPoint,
                    Arc a => a.StartPoint(),
                    _ => Vector.Invalid
                };

                if (!currentEnd.IsValid() || !nextStart.IsValid()) continue;

                var gap = currentEnd.DistanceTo(nextStart);
                Assert.True(gap < 0.005,
                    $"Gap of {gap:F4} between entities {i} ({current.GetType().Name}) and {i + 1} ({next.GetType().Name})");
            }
        }
    }
}
