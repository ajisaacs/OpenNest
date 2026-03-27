# Direct Arc Conversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert DXF splines and ellipses directly to circular arcs during import, eliminating the intermediate 200-line-segment representation.

**Architecture:** Two new static classes in `OpenNest.Core/Geometry/` — `EllipseConverter` (analytical curvature-based arc fitting with normal-constrained G1 continuity) and `SplineConverter` (tangent-chained greedy arc fitting on evaluated curve points). Both produce `List<Entity>` of arcs and lines. The existing `Extensions.cs` and `DxfImporter.cs` in `OpenNest.IO` are updated to delegate to these converters.

**Tech Stack:** .NET 8, xUnit, OpenNest.Core geometry primitives (`Vector`, `Arc`, `Line`, `Entity`, `Angle`), ACadSharp `Spline.PolygonalVertexes()`.

**Spec:** `docs/superpowers/specs/2026-03-27-direct-arc-conversion-design.md`

---

### Task 1: EllipseConverter — Evaluation Helpers

**Files:**
- Create: `OpenNest.Core/Geometry/EllipseConverter.cs`
- Create: `OpenNest.Tests/EllipseConverterTests.cs`

- [ ] **Step 1: Write failing tests for ellipse evaluation methods**

```csharp
using OpenNest.Geometry;
using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests;

public class EllipseConverterTests
{
    private const double Tol = 1e-10;

    [Fact]
    public void EvaluatePoint_AtZero_ReturnsMajorAxisEnd()
    {
        // Unrotated ellipse: a=10, b=5, center=(0,0)
        var p = EllipseConverter.EvaluatePoint(10, 5, 0, new Vector(0, 0), 0);
        Assert.InRange(p.X, 10 - Tol, 10 + Tol);
        Assert.InRange(p.Y, -Tol, Tol);
    }

    [Fact]
    public void EvaluatePoint_AtHalfPi_ReturnsMinorAxisEnd()
    {
        var p = EllipseConverter.EvaluatePoint(10, 5, 0, new Vector(0, 0), System.Math.PI / 2);
        Assert.InRange(p.X, -Tol, Tol);
        Assert.InRange(p.Y, 5 - Tol, 5 + Tol);
    }

    [Fact]
    public void EvaluatePoint_WithRotation_RotatesCorrectly()
    {
        // 90-degree rotation: major axis now points up
        var p = EllipseConverter.EvaluatePoint(10, 5, System.Math.PI / 2, new Vector(0, 0), 0);
        Assert.InRange(p.X, -Tol, Tol);
        Assert.InRange(p.Y, 10 - Tol, 10 + Tol);
    }

    [Fact]
    public void EvaluatePoint_WithCenter_TranslatesCorrectly()
    {
        var p = EllipseConverter.EvaluatePoint(10, 5, 0, new Vector(100, 200), 0);
        Assert.InRange(p.X, 110 - Tol, 110 + Tol);
        Assert.InRange(p.Y, 200 - Tol, 200 + Tol);
    }

    [Fact]
    public void EvaluateTangent_AtZero_PointsUp()
    {
        // At t=0: tangent is (-a*sin(0), b*cos(0)) = (0, b) in local coords
        var t = EllipseConverter.EvaluateTangent(10, 5, 0, 0);
        Assert.InRange(t.X, -Tol, Tol);
        Assert.True(t.Y > 0);
    }

    [Fact]
    public void EvaluateNormal_AtZero_PointsInward()
    {
        // At t=0 on unrotated ellipse, inward normal points in -X direction
        var n = EllipseConverter.EvaluateNormal(10, 5, 0, 0);
        Assert.True(n.X < 0);
        Assert.InRange(n.Y, -Tol, Tol);
    }

    [Fact]
    public void IntersectNormals_PerpendicularNormals_FindsCenter()
    {
        // Two points with known normals that intersect at origin
        var p1 = new Vector(5, 0);
        var n1 = new Vector(-1, 0); // points left
        var p2 = new Vector(0, 5);
        var n2 = new Vector(0, -1); // points down
        var center = EllipseConverter.IntersectNormals(p1, n1, p2, n2);
        Assert.InRange(center.X, -Tol, Tol);
        Assert.InRange(center.Y, -Tol, Tol);
    }

    [Fact]
    public void IntersectNormals_ParallelNormals_ReturnsInvalid()
    {
        var p1 = new Vector(0, 0);
        var n1 = new Vector(1, 0);
        var p2 = new Vector(0, 5);
        var n2 = new Vector(1, 0); // same direction — parallel
        var center = EllipseConverter.IntersectNormals(p1, n1, p2, n2);
        Assert.False(center.IsValid());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~EllipseConverterTests" --no-build 2>&1 | head -5`
Expected: Build error — `EllipseConverter` does not exist.

- [ ] **Step 3: Write EllipseConverter with evaluation methods**

Create `OpenNest.Core/Geometry/EllipseConverter.cs`:

```csharp
using OpenNest.Math;
using System;
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    public static class EllipseConverter
    {
        private const int MaxSubdivisionDepth = 12;
        private const int DeviationSamples = 20;

        internal static Vector EvaluatePoint(double semiMajor, double semiMinor, double rotation, Vector center, double t)
        {
            var x = semiMajor * System.Math.Cos(t);
            var y = semiMinor * System.Math.Sin(t);

            var cos = System.Math.Cos(rotation);
            var sin = System.Math.Sin(rotation);

            return new Vector(
                center.X + x * cos - y * sin,
                center.Y + x * sin + y * cos);
        }

        internal static Vector EvaluateTangent(double semiMajor, double semiMinor, double rotation, double t)
        {
            var tx = -semiMajor * System.Math.Sin(t);
            var ty = semiMinor * System.Math.Cos(t);

            var cos = System.Math.Cos(rotation);
            var sin = System.Math.Sin(rotation);

            return new Vector(
                tx * cos - ty * sin,
                tx * sin + ty * cos);
        }

        internal static Vector EvaluateNormal(double semiMajor, double semiMinor, double rotation, double t)
        {
            // Inward normal: perpendicular to tangent, pointing toward center of curvature.
            // In local coords: N(t) = (-b*cos(t), -a*sin(t))
            var nx = -semiMinor * System.Math.Cos(t);
            var ny = -semiMajor * System.Math.Sin(t);

            var cos = System.Math.Cos(rotation);
            var sin = System.Math.Sin(rotation);

            return new Vector(
                nx * cos - ny * sin,
                nx * sin + ny * cos);
        }

        internal static Vector IntersectNormals(Vector p1, Vector n1, Vector p2, Vector n2)
        {
            // Solve: p1 + s*n1 = p2 + t*n2
            // s*n1.x - t*n2.x = p2.x - p1.x
            // s*n1.y - t*n2.y = p2.y - p1.y
            var det = n1.X * (-n2.Y) - (-n2.X) * n1.Y;
            if (System.Math.Abs(det) < 1e-10)
                return Vector.Invalid;

            var dx = p2.X - p1.X;
            var dy = p2.Y - p1.Y;
            var s = (dx * (-n2.Y) - dy * (-n2.X)) / det;

            return new Vector(p1.X + s * n1.X, p1.Y + s * n1.Y);
        }
    }
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~EllipseConverterTests" -v minimal`
Expected: All 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Core/Geometry/EllipseConverter.cs OpenNest.Tests/EllipseConverterTests.cs
git commit -m "feat: add EllipseConverter evaluation helpers with tests"
```

---

### Task 2: EllipseConverter — Arc Fitting and Convert Method

**Files:**
- Modify: `OpenNest.Core/Geometry/EllipseConverter.cs`
- Modify: `OpenNest.Tests/EllipseConverterTests.cs`

- [ ] **Step 1: Write failing tests for Convert method**

Add to `EllipseConverterTests.cs`:

```csharp
[Fact]
public void Convert_Circle_ProducesOneOrTwoArcs()
{
    // A circle is an ellipse with ratio=1 — should produce minimal arcs
    var result = EllipseConverter.Convert(
        new Vector(0, 0), semiMajor: 10, semiMinor: 10, rotation: 0,
        startParam: 0, endParam: Angle.TwoPI, tolerance: 0.001);

    Assert.All(result, e => Assert.IsType<Arc>(e));
    Assert.InRange(result.Count, 1, 4);
}

[Fact]
public void Convert_ModerateEllipse_AllArcsWithinTolerance()
{
    var a = 10.0;
    var b = 7.0;
    var tolerance = 0.001;
    var result = EllipseConverter.Convert(
        new Vector(0, 0), a, b, rotation: 0,
        startParam: 0, endParam: Angle.TwoPI, tolerance: tolerance);

    Assert.True(result.Count >= 4, $"Expected at least 4 arcs, got {result.Count}");
    Assert.All(result, e => Assert.IsType<Arc>(e));

    // Verify each arc stays within tolerance of the ellipse
    foreach (var entity in result)
    {
        var arc = (Arc)entity;
        var maxDev = MaxDeviationFromEllipse(arc, new Vector(0, 0), a, b, 0, 50);
        Assert.True(maxDev <= tolerance,
            $"Arc at center ({arc.Center.X:F4},{arc.Center.Y:F4}) r={arc.Radius:F4} " +
            $"deviates {maxDev:F6} from ellipse (tolerance={tolerance})");
    }
}

[Fact]
public void Convert_HighlyEccentricEllipse_ProducesMoreArcs()
{
    var a = 20.0;
    var b = 3.0;
    var tolerance = 0.001;
    var result = EllipseConverter.Convert(
        new Vector(0, 0), a, b, rotation: 0,
        startParam: 0, endParam: Angle.TwoPI, tolerance: tolerance);

    Assert.True(result.Count >= 8, $"Expected at least 8 arcs for eccentric ellipse, got {result.Count}");
    Assert.All(result, e => Assert.IsType<Arc>(e));

    foreach (var entity in result)
    {
        var arc = (Arc)entity;
        var maxDev = MaxDeviationFromEllipse(arc, new Vector(0, 0), a, b, 0, 50);
        Assert.True(maxDev <= tolerance,
            $"Deviation {maxDev:F6} exceeds tolerance {tolerance}");
    }
}

[Fact]
public void Convert_PartialEllipse_CoversArcOnly()
{
    var a = 10.0;
    var b = 5.0;
    var tolerance = 0.001;
    // Quarter ellipse
    var result = EllipseConverter.Convert(
        new Vector(0, 0), a, b, rotation: 0,
        startParam: 0, endParam: System.Math.PI / 2, tolerance: tolerance);

    Assert.NotEmpty(result);
    Assert.All(result, e => Assert.IsType<Arc>(e));

    // First arc should start near (a, 0)
    var firstArc = (Arc)result[0];
    var sp = firstArc.StartPoint();
    Assert.InRange(sp.X, a - 0.01, a + 0.01);
    Assert.InRange(sp.Y, -0.01, 0.01);

    // Last arc should end near (0, b)
    var lastArc = (Arc)result[^1];
    var ep = lastArc.EndPoint();
    Assert.InRange(ep.X, -0.01, 0.01);
    Assert.InRange(ep.Y, b - 0.01, b + 0.01);
}

[Fact]
public void Convert_EndpointContinuity_ArcsConnect()
{
    var result = EllipseConverter.Convert(
        new Vector(5, 10), semiMajor: 15, semiMinor: 8, rotation: 0.5,
        startParam: 0, endParam: Angle.TwoPI, tolerance: 0.001);

    for (var i = 0; i < result.Count - 1; i++)
    {
        var current = (Arc)result[i];
        var next = (Arc)result[i + 1];
        var gap = current.EndPoint().DistanceTo(next.StartPoint());
        Assert.True(gap < 0.001,
            $"Gap of {gap:F6} between arc {i} and arc {i + 1}");
    }

    // Closed ellipse: last arc should connect back to first
    var lastArc = (Arc)result[^1];
    var firstArc = (Arc)result[0];
    var closingGap = lastArc.EndPoint().DistanceTo(firstArc.StartPoint());
    Assert.True(closingGap < 0.001,
        $"Closing gap of {closingGap:F6}");
}

[Fact]
public void Convert_WithRotationAndOffset_ProducesValidArcs()
{
    var center = new Vector(50, -30);
    var rotation = System.Math.PI / 3; // 60 degrees
    var a = 12.0;
    var b = 6.0;
    var tolerance = 0.001;

    var result = EllipseConverter.Convert(center, a, b, rotation,
        startParam: 0, endParam: Angle.TwoPI, tolerance: tolerance);

    Assert.NotEmpty(result);
    foreach (var entity in result)
    {
        var arc = (Arc)entity;
        var maxDev = MaxDeviationFromEllipse(arc, center, a, b, rotation, 50);
        Assert.True(maxDev <= tolerance,
            $"Deviation {maxDev:F6} exceeds tolerance {tolerance}");
    }
}

private static double MaxDeviationFromEllipse(Arc arc, Vector ellipseCenter,
    double semiMajor, double semiMinor, double rotation, int samples)
{
    // Find which parameter range this arc covers by checking start/end points
    var sp = arc.StartPoint();
    var ep = arc.EndPoint();

    var maxDev = 0.0;

    // Sample points along the arc and measure distance to nearest ellipse point
    var sweep = arc.SweepAngle();
    var startAngle = arc.StartAngle;
    if (arc.IsReversed)
        startAngle = arc.EndAngle;

    for (var i = 0; i <= samples; i++)
    {
        var frac = (double)i / samples;
        var angle = startAngle + frac * sweep;
        var px = arc.Center.X + arc.Radius * System.Math.Cos(angle);
        var py = arc.Center.Y + arc.Radius * System.Math.Sin(angle);
        var arcPoint = new Vector(px, py);

        // Find closest point on ellipse by searching parameter space
        var minDist = double.MaxValue;
        for (var j = 0; j <= 200; j++)
        {
            var t = (double)j / 200 * Angle.TwoPI;
            var ep2 = EllipseConverter.EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, t);
            var dist = arcPoint.DistanceTo(ep2);
            if (dist < minDist) minDist = dist;
        }
        if (minDist > maxDev) maxDev = minDist;
    }

    return maxDev;
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~EllipseConverterTests" --no-build 2>&1 | head -5`
Expected: Build error — `EllipseConverter.Convert` does not exist.

- [ ] **Step 3: Implement Convert method and helpers**

Add the following methods to `EllipseConverter.cs`, inside the class body after the existing `IntersectNormals` method:

```csharp
public static List<Entity> Convert(Vector center, double semiMajor, double semiMinor,
    double rotation, double startParam, double endParam, double tolerance = 0.001)
{
    if (endParam <= startParam)
        endParam += Angle.TwoPI;

    // True circle — emit a single arc (or two for full circle)
    if (System.Math.Abs(semiMajor - semiMinor) < Tolerance.Epsilon)
        return ConvertCircle(center, semiMajor, rotation, startParam, endParam);

    // Compute initial split parameters at curvature extremes within range
    var splits = GetInitialSplits(startParam, endParam);

    var entities = new List<Entity>();
    for (var i = 0; i < splits.Count - 1; i++)
        FitSegment(center, semiMajor, semiMinor, rotation,
            splits[i], splits[i + 1], tolerance, entities, 0);

    return entities;
}

private static List<Entity> ConvertCircle(Vector center, double radius,
    double rotation, double startParam, double endParam)
{
    var sweep = endParam - startParam;
    var isFull = System.Math.Abs(sweep - Angle.TwoPI) < 0.01;

    if (isFull)
    {
        // Full circle: two semicircular arcs (a single arc can't represent 360 degrees)
        var startAngle1 = Angle.NormalizeRad(startParam + rotation);
        var midAngle = Angle.NormalizeRad(startParam + System.Math.PI + rotation);
        var endAngle2 = startAngle1; // wraps back

        return new List<Entity>
        {
            new Arc(center, radius, startAngle1, midAngle, false),
            new Arc(center, radius, midAngle, endAngle2, false)
        };
    }

    var sa = Angle.NormalizeRad(startParam + rotation);
    var ea = Angle.NormalizeRad(endParam + rotation);
    return new List<Entity> { new Arc(center, radius, sa, ea, false) };
}

private static List<double> GetInitialSplits(double startParam, double endParam)
{
    var splits = new List<double> { startParam };

    // Add quadrant boundaries (curvature extremes) that fall within range
    // These occur at multiples of PI/2
    var firstQuadrant = System.Math.Ceiling(startParam / (System.Math.PI / 2)) * (System.Math.PI / 2);
    for (var q = firstQuadrant; q < endParam; q += System.Math.PI / 2)
    {
        if (q > startParam + 1e-10 && q < endParam - 1e-10)
            splits.Add(q);
    }

    splits.Add(endParam);
    return splits;
}

private static void FitSegment(Vector center, double semiMajor, double semiMinor,
    double rotation, double t0, double t1, double tolerance, List<Entity> results, int depth)
{
    var p0 = EvaluatePoint(semiMajor, semiMinor, rotation, center, t0);
    var p1 = EvaluatePoint(semiMajor, semiMinor, rotation, center, t1);

    // Skip tiny segments
    if (p0.DistanceTo(p1) < 1e-10)
        return;

    var n0 = EvaluateNormal(semiMajor, semiMinor, rotation, t0);
    var n1 = EvaluateNormal(semiMajor, semiMinor, rotation, t1);

    var arcCenter = IntersectNormals(p0, n0, p1, n1);

    if (!arcCenter.IsValid() || depth >= MaxSubdivisionDepth)
    {
        // Normals parallel (straight segment) or max depth reached
        results.Add(new Line(p0, p1));
        return;
    }

    var radius = p0.DistanceTo(arcCenter);
    var maxDev = MeasureDeviation(center, semiMajor, semiMinor, rotation,
        t0, t1, arcCenter, radius);

    if (maxDev <= tolerance)
    {
        results.Add(CreateArc(arcCenter, radius, center, semiMajor, semiMinor, rotation, t0, t1));
    }
    else
    {
        var tMid = (t0 + t1) / 2.0;
        FitSegment(center, semiMajor, semiMinor, rotation, t0, tMid, tolerance, results, depth + 1);
        FitSegment(center, semiMajor, semiMinor, rotation, tMid, t1, tolerance, results, depth + 1);
    }
}

private static double MeasureDeviation(Vector center, double semiMajor, double semiMinor,
    double rotation, double t0, double t1, Vector arcCenter, double radius)
{
    var maxDev = 0.0;
    for (var i = 1; i < DeviationSamples; i++)
    {
        var t = t0 + (t1 - t0) * i / DeviationSamples;
        var p = EvaluatePoint(semiMajor, semiMinor, rotation, center, t);
        var dist = p.DistanceTo(arcCenter);
        var dev = System.Math.Abs(dist - radius);
        if (dev > maxDev) maxDev = dev;
    }
    return maxDev;
}

private static Arc CreateArc(Vector arcCenter, double radius,
    Vector ellipseCenter, double semiMajor, double semiMinor, double rotation,
    double t0, double t1)
{
    var p0 = EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, t0);
    var p1 = EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, t1);

    var startAngle = System.Math.Atan2(p0.Y - arcCenter.Y, p0.X - arcCenter.X);
    var endAngle = System.Math.Atan2(p1.Y - arcCenter.Y, p1.X - arcCenter.X);

    // Determine direction: sample a midpoint and check signed angles
    var pMid = EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, (t0 + t1) / 2);
    var points = new List<Vector> { p0, pMid, p1 };
    var isReversed = SumSignedAngles(arcCenter, points) < 0;

    if (startAngle < 0) startAngle += Angle.TwoPI;
    if (endAngle < 0) endAngle += Angle.TwoPI;

    return new Arc(arcCenter, radius, startAngle, endAngle, isReversed);
}

private static double SumSignedAngles(Vector center, List<Vector> points)
{
    var total = 0.0;
    for (var i = 0; i < points.Count - 1; i++)
    {
        var a1 = System.Math.Atan2(points[i].Y - center.Y, points[i].X - center.X);
        var a2 = System.Math.Atan2(points[i + 1].Y - center.Y, points[i + 1].X - center.X);
        var da = a2 - a1;
        while (da > System.Math.PI) da -= Angle.TwoPI;
        while (da < -System.Math.PI) da += Angle.TwoPI;
        total += da;
    }
    return total;
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~EllipseConverterTests" -v minimal`
Expected: All tests pass. If any tolerance tests fail, adjust `DeviationSamples` upward or debug the specific case.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Core/Geometry/EllipseConverter.cs OpenNest.Tests/EllipseConverterTests.cs
git commit -m "feat: add EllipseConverter arc fitting with normal-constrained G1 continuity"
```

---

### Task 3: SplineConverter — Tangent-Chained Arc Fitting

**Files:**
- Create: `OpenNest.Core/Geometry/SplineConverter.cs`
- Create: `OpenNest.Tests/SplineConverterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using OpenNest.Geometry;
using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests;

public class SplineConverterTests
{
    [Fact]
    public void Convert_SemicirclePoints_ProducesSingleArc()
    {
        // Generate 50 points on a semicircle of radius 10
        var points = new System.Collections.Generic.List<Vector>();
        for (var i = 0; i <= 50; i++)
        {
            var t = System.Math.PI * i / 50;
            points.Add(new Vector(10 * System.Math.Cos(t), 10 * System.Math.Sin(t)));
        }

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        Assert.Single(result);
        Assert.IsType<Arc>(result[0]);
        var arc = (Arc)result[0];
        Assert.InRange(arc.Radius, 9.99, 10.01);
    }

    [Fact]
    public void Convert_StraightLinePoints_ProducesSingleLine()
    {
        // 10 collinear points
        var points = new System.Collections.Generic.List<Vector>();
        for (var i = 0; i <= 10; i++)
            points.Add(new Vector(i, 2 * i + 1));

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        // Should produce lines, not arcs (collinear points can't form an arc)
        Assert.All(result, e => Assert.IsType<Line>(e));
    }

    [Fact]
    public void Convert_SCurve_ProducesMultipleArcs()
    {
        // S-curve: semicircle up, then semicircle down
        var points = new System.Collections.Generic.List<Vector>();
        // First half: arc centered at (0,0) radius 10, going 0 to PI
        for (var i = 0; i <= 30; i++)
        {
            var t = System.Math.PI * i / 30;
            points.Add(new Vector(10 * System.Math.Cos(t), 10 * System.Math.Sin(t)));
        }
        // Second half: arc centered at (-20,0) radius 10, going 0 to -PI
        for (var i = 1; i <= 30; i++)
        {
            var t = -System.Math.PI * i / 30;
            points.Add(new Vector(-20 + 10 * System.Math.Cos(t), 10 * System.Math.Sin(t)));
        }

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        // Should produce at least 2 arcs (one for each half)
        var arcCount = result.Count(e => e is Arc);
        Assert.True(arcCount >= 2, $"Expected at least 2 arcs, got {arcCount}");
    }

    [Fact]
    public void Convert_TwoPoints_ProducesSingleLine()
    {
        var points = new System.Collections.Generic.List<Vector>
        {
            new Vector(0, 0),
            new Vector(10, 5)
        };

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        Assert.Single(result);
        Assert.IsType<Line>(result[0]);
    }

    [Fact]
    public void Convert_EndpointContinuity_EntitiesConnect()
    {
        // Full circle of points
        var points = new System.Collections.Generic.List<Vector>();
        for (var i = 0; i <= 80; i++)
        {
            var t = Angle.TwoPI * i / 80;
            // Ellipse-like shape: different X and Y radii
            points.Add(new Vector(15 * System.Math.Cos(t), 8 * System.Math.Sin(t)));
        }

        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);

        for (var i = 0; i < result.Count - 1; i++)
        {
            var endPt = GetEndPoint(result[i]);
            var startPt = GetStartPoint(result[i + 1]);
            var gap = endPt.DistanceTo(startPt);
            Assert.True(gap < 0.001,
                $"Gap of {gap:F6} between entity {i} and {i + 1}");
        }
    }

    [Fact]
    public void Convert_EmptyPoints_ReturnsEmpty()
    {
        var result = SplineConverter.Convert(new System.Collections.Generic.List<Vector>(),
            isClosed: false, tolerance: 0.001);
        Assert.Empty(result);
    }

    [Fact]
    public void Convert_SinglePoint_ReturnsEmpty()
    {
        var points = new System.Collections.Generic.List<Vector> { new Vector(5, 5) };
        var result = SplineConverter.Convert(points, isClosed: false, tolerance: 0.001);
        Assert.Empty(result);
    }

    private static Vector GetStartPoint(Entity e)
    {
        return e switch
        {
            Arc a => a.StartPoint(),
            Line l => l.StartPoint,
            _ => throw new System.Exception("Unexpected entity type")
        };
    }

    private static Vector GetEndPoint(Entity e)
    {
        return e switch
        {
            Arc a => a.EndPoint(),
            Line l => l.EndPoint,
            _ => throw new System.Exception("Unexpected entity type")
        };
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~SplineConverterTests" --no-build 2>&1 | head -5`
Expected: Build error — `SplineConverter` does not exist.

- [ ] **Step 3: Implement SplineConverter**

Create `OpenNest.Core/Geometry/SplineConverter.cs`:

```csharp
using OpenNest.Math;
using System;
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    public static class SplineConverter
    {
        private const int MinPointsForArc = 3;

        public static List<Entity> Convert(List<Vector> points, bool isClosed, double tolerance = 0.001)
        {
            if (points == null || points.Count < 2)
                return new List<Entity>();

            var entities = new List<Entity>();
            var i = 0;
            var chainedTangent = Vector.Invalid;

            while (i < points.Count - 1)
            {
                var result = TryFitArc(points, i, chainedTangent, tolerance);
                if (result != null)
                {
                    entities.Add(result.Arc);
                    chainedTangent = result.EndTangent;
                    i = result.EndIndex;
                }
                else
                {
                    // Can't fit an arc here — emit a line and reset tangent chain
                    entities.Add(new Line(points[i], points[i + 1]));
                    chainedTangent = Vector.Invalid;
                    i++;
                }
            }

            return entities;
        }

        private static ArcFitResult TryFitArc(List<Vector> points, int start,
            Vector chainedTangent, double tolerance)
        {
            // Need at least 3 points for an arc
            var minEnd = start + MinPointsForArc - 1;
            if (minEnd >= points.Count)
                return null;

            // Compute start tangent: use chained tangent if available, otherwise from first two points
            var tangent = chainedTangent.IsValid()
                ? chainedTangent
                : new Vector(points[start + 1].X - points[start].X,
                             points[start + 1].Y - points[start].Y);

            // Try fitting with minimum points first
            var subPoints = points.GetRange(start, MinPointsForArc);
            var (center, radius, dev) = FitWithStartTangent(subPoints, tangent);
            if (!center.IsValid() || dev > tolerance)
                return null;

            // Extend the arc as far as possible
            var endIdx = minEnd;
            while (endIdx + 1 < points.Count)
            {
                var extPoints = points.GetRange(start, endIdx + 1 - start + 1);
                var (nc, nr, nd) = FitWithStartTangent(extPoints, tangent);
                if (!nc.IsValid() || nd > tolerance)
                    break;

                endIdx++;
                center = nc;
                radius = nr;
                dev = nd;
            }

            // Reject tiny arcs (nearly-straight segments that happen to fit a huge circle)
            var finalPoints = points.GetRange(start, endIdx - start + 1);
            var sweep = System.Math.Abs(SumSignedAngles(center, finalPoints));
            if (sweep < Angle.ToRadians(5))
                return null;

            var arc = CreateArc(center, radius, finalPoints);
            var endTangent = ComputeEndTangent(center, finalPoints);

            return new ArcFitResult(arc, endTangent, endIdx);
        }

        private static (Vector center, double radius, double deviation) FitWithStartTangent(
            List<Vector> points, Vector tangent)
        {
            if (points.Count < 3)
                return (Vector.Invalid, 0, double.MaxValue);

            var p1 = points[0];
            var pn = points[^1];

            // Perpendicular bisector of chord P1->Pn
            var mx = (p1.X + pn.X) / 2;
            var my = (p1.Y + pn.Y) / 2;
            var dx = pn.X - p1.X;
            var dy = pn.Y - p1.Y;
            var chordLen = System.Math.Sqrt(dx * dx + dy * dy);
            if (chordLen < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            var bx = -dy / chordLen;
            var by = dx / chordLen;

            // Normal at P1 (perpendicular to tangent)
            var tLen = System.Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
            if (tLen < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            var nx = -tangent.Y / tLen;
            var ny = tangent.X / tLen;

            // Intersect: P1 + s*N = midpoint + t*bisector
            var det = nx * by - ny * bx;
            if (System.Math.Abs(det) < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            var s = ((mx - p1.X) * by - (my - p1.Y) * bx) / det;

            var cx = p1.X + s * nx;
            var cy = p1.Y + s * ny;
            var radius = System.Math.Sqrt((cx - p1.X) * (cx - p1.X) + (cy - p1.Y) * (cy - p1.Y));

            if (radius < 1e-10)
                return (Vector.Invalid, 0, double.MaxValue);

            return (new Vector(cx, cy), radius, MaxRadialDeviation(points, cx, cy, radius));
        }

        private static double MaxRadialDeviation(List<Vector> points, double cx, double cy, double radius)
        {
            var maxDev = 0.0;
            for (var i = 1; i < points.Count - 1; i++)
            {
                var px = points[i].X - cx;
                var py = points[i].Y - cy;
                var dist = System.Math.Sqrt(px * px + py * py);
                var dev = System.Math.Abs(dist - radius);
                if (dev > maxDev) maxDev = dev;
            }
            return maxDev;
        }

        private static double SumSignedAngles(Vector center, List<Vector> points)
        {
            var total = 0.0;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a1 = System.Math.Atan2(points[i].Y - center.Y, points[i].X - center.X);
                var a2 = System.Math.Atan2(points[i + 1].Y - center.Y, points[i + 1].X - center.X);
                var da = a2 - a1;
                while (da > System.Math.PI) da -= Angle.TwoPI;
                while (da < -System.Math.PI) da += Angle.TwoPI;
                total += da;
            }
            return total;
        }

        private static Vector ComputeEndTangent(Vector center, List<Vector> points)
        {
            var lastPt = points[^1];
            var totalAngle = SumSignedAngles(center, points);

            var rx = lastPt.X - center.X;
            var ry = lastPt.Y - center.Y;

            return totalAngle >= 0
                ? new Vector(-ry, rx)
                : new Vector(ry, -rx);
        }

        private static Arc CreateArc(Vector center, double radius, List<Vector> points)
        {
            var firstPoint = points[0];
            var lastPoint = points[^1];

            var startAngle = System.Math.Atan2(firstPoint.Y - center.Y, firstPoint.X - center.X);
            var endAngle = System.Math.Atan2(lastPoint.Y - center.Y, lastPoint.X - center.X);
            var isReversed = SumSignedAngles(center, points) < 0;

            if (startAngle < 0) startAngle += Angle.TwoPI;
            if (endAngle < 0) endAngle += Angle.TwoPI;

            return new Arc(center, radius, startAngle, endAngle, isReversed);
        }

        private sealed class ArcFitResult
        {
            public Arc Arc { get; }
            public Vector EndTangent { get; }
            public int EndIndex { get; }

            public ArcFitResult(Arc arc, Vector endTangent, int endIndex)
            {
                Arc = arc;
                EndTangent = endTangent;
                EndIndex = endIndex;
            }
        }
    }
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~SplineConverterTests" -v minimal`
Expected: All 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Core/Geometry/SplineConverter.cs OpenNest.Tests/SplineConverterTests.cs
git commit -m "feat: add SplineConverter with tangent-chained arc fitting"
```

---

### Task 4: Wire Up Extensions.cs and DxfImporter.cs

**Files:**
- Modify: `OpenNest.IO/Extensions.cs:59-95` (spline method)
- Modify: `OpenNest.IO/Extensions.cs:175-239` (ellipse method)
- Modify: `OpenNest.IO/DxfImporter.cs:47-61` (spline and ellipse cases)

- [ ] **Step 1: Modify the spline extension method to use SplineConverter**

In `OpenNest.IO/Extensions.cs`, replace the `ToOpenNest(this Spline spline)` method (lines 59-95) with:

```csharp
public static List<Geometry.Entity> ToOpenNest(this Spline spline, int precision)
{
    var layer = spline.Layer.ToOpenNest();
    var color = spline.ResolveColor();
    var lineTypeName = spline.ResolveLineTypeName();

    // Evaluate actual points on the spline curve (not control points)
    List<CSMath.XYZ> curvePoints;
    if (!spline.TryPolygonalVertexes(precision > 0 ? precision : 200, out curvePoints)
        || curvePoints == null || curvePoints.Count < 2)
    {
        // Fallback: use control points if evaluation fails
        curvePoints = new List<CSMath.XYZ>(spline.ControlPoints);
        if (curvePoints.Count < 2)
            return new List<Geometry.Entity>();
    }

    var points = new List<Vector>(curvePoints.Count);
    foreach (var pt in curvePoints)
        points.Add(pt.ToOpenNest());

    var entities = SplineConverter.Convert(points, spline.IsClosed, tolerance: 0.001);

    foreach (var entity in entities)
    {
        entity.Layer = layer;
        entity.Color = color;
        entity.LineTypeName = lineTypeName;
    }

    return entities;
}

**Note:** `Geometry.Entity` prefix is required because `Extensions.cs` imports both `ACadSharp.Entities` and `OpenNest.Geometry`, making the bare `Entity` name ambiguous. This follows the existing pattern in the file (e.g., `Geometry.Arc`, `Geometry.Line`).
```

- [ ] **Step 2: Modify the ellipse extension method to use EllipseConverter**

In `OpenNest.IO/Extensions.cs`, replace the `ToOpenNest(this Ellipse ellipse, int precision)` method (lines 175-239) with:

```csharp
public static List<Geometry.Entity> ToOpenNest(this ACadSharp.Entities.Ellipse ellipse, double tolerance = 0.001)
{
    var center = new Vector(ellipse.Center.X, ellipse.Center.Y);
    var majorAxis = new Vector(ellipse.MajorAxisEndPoint.X, ellipse.MajorAxisEndPoint.Y);
    var semiMajor = System.Math.Sqrt(majorAxis.X * majorAxis.X + majorAxis.Y * majorAxis.Y);
    var semiMinor = semiMajor * ellipse.RadiusRatio;
    var rotation = System.Math.Atan2(majorAxis.Y, majorAxis.X);

    var startParam = ellipse.StartParameter;
    var endParam = ellipse.EndParameter;

    var layer = ellipse.Layer.ToOpenNest();
    var color = ellipse.ResolveColor();
    var lineTypeName = ellipse.ResolveLineTypeName();

    var entities = EllipseConverter.Convert(center, semiMajor, semiMinor, rotation,
        startParam, endParam, tolerance);

    foreach (var entity in entities)
    {
        entity.Layer = layer;
        entity.Color = color;
        entity.LineTypeName = lineTypeName;
    }

    return entities;
}
```

- [ ] **Step 3: Update DxfImporter.cs to handle mixed entity returns**

In `OpenNest.IO/DxfImporter.cs`, modify the spline and ellipse cases in `GetGeometry()` (lines 47-61):

Replace the spline case (lines 47-49). Note: `DxfImporter.cs` only imports `OpenNest.Geometry` (no `ACadSharp.Entities`), so `Line` and `Arc` are unambiguous here:
```csharp
case ACadSharp.Entities.Spline spline:
    foreach (var e in spline.ToOpenNest(SplinePrecision))
    {
        if (e is Line l) lines.Add(l);
        else if (e is Arc a) arcs.Add(a);
    }
    break;
```

Replace the ellipse case (lines 59-61):
```csharp
case ACadSharp.Entities.Ellipse ellipse:
    foreach (var e in ellipse.ToOpenNest())
    {
        if (e is Line l) lines.Add(l);
        else if (e is Arc a) arcs.Add(a);
    }
    break;
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds with no errors. There may be warnings about the unused `SplinePrecision` on the ellipse path — that's fine, the property is still used for spline precision.

- [ ] **Step 5: Run all tests**

Run: `dotnet test OpenNest.Tests -v minimal`
Expected: All tests pass, including the existing `GeometrySimplifierTests` and `DxfRoundtripTests`.

- [ ] **Step 6: Commit**

```bash
git add OpenNest.IO/Extensions.cs OpenNest.IO/DxfImporter.cs
git commit -m "feat: wire up EllipseConverter and SplineConverter in DXF import pipeline"
```

---

### Task 5: Integration Test with Real DXF

**Files:**
- Modify: `OpenNest.Tests/EllipseConverterTests.cs` (add integration-level test)

The project has test DXF files in `OpenNest.Tests/Bending/TestData/`. We can also create a programmatic integration test.

- [ ] **Step 1: Write integration test**

Add to `EllipseConverterTests.cs`:

```csharp
[Fact]
public void DxfImporter_EllipseInDxf_ProducesArcsNotLines()
{
    // Create a DXF in memory with an ellipse and verify import produces arcs
    var doc = new ACadSharp.CadDocument();
    var ellipse = new ACadSharp.Entities.Ellipse
    {
        Center = new CSMath.XYZ(0, 0, 0),
        MajorAxisEndPoint = new CSMath.XYZ(10, 0, 0),
        RadiusRatio = 0.6,
        StartParameter = 0,
        EndParameter = System.Math.PI * 2
    };
    doc.Entities.Add(ellipse);

    // Write to temp file and re-import
    var tempPath = System.IO.Path.GetTempFileName() + ".dxf";
    try
    {
        using (var writer = new ACadSharp.IO.DxfWriter(tempPath, doc, false))
            writer.Write();

        var importer = new OpenNest.IO.DxfImporter { SplinePrecision = 200 };
        var result = importer.Import(tempPath);

        var arcCount = result.Entities.Count(e => e is Arc);
        var lineCount = result.Entities.Count(e => e is Line);

        // Should have arcs, not hundreds of lines
        Assert.True(arcCount >= 4, $"Expected at least 4 arcs, got {arcCount}");
        Assert.Equal(0, lineCount);
    }
    finally
    {
        System.IO.File.Delete(tempPath);
    }
}
```

- [ ] **Step 2: Run integration test**

Run: `dotnet test OpenNest.Tests --filter "DxfImporter_EllipseInDxf_ProducesArcsNotLines" -v minimal`
Expected: Test passes. If ACadSharp has issues creating/writing the ellipse entity programmatically, adjust the test to use a simpler verification approach (call `EllipseConverter.Convert` directly with parameters matching a real-world ellipse and verify entity count and types).

- [ ] **Step 3: Run full test suite**

Run: `dotnet test OpenNest.Tests -v minimal`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Tests/EllipseConverterTests.cs
git commit -m "test: add DXF import integration test for ellipse-to-arc conversion"
```

---

### Task 6: Final Verification and Cleanup

**Files:**
- Review: All modified/created files

- [ ] **Step 1: Verify build with no warnings**

Run: `dotnet build OpenNest.sln -warnaserror 2>&1 | tail -5`
Expected: Build succeeds. Fix any warnings (e.g., unused usings, nullable warnings).

- [ ] **Step 2: Run full test suite one final time**

Run: `dotnet test OpenNest.Tests -v minimal`
Expected: All tests pass.

- [ ] **Step 3: Verify no regressions in existing DXF roundtrip tests**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~DxfRoundtrip" -v normal`
Expected: All existing roundtrip tests pass.

- [ ] **Step 4: Commit any cleanup**

Only if there were warnings or minor fixes from steps 1-3.
