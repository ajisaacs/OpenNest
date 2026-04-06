using OpenNest.Geometry;
using OpenNest.Math;
using Xunit;
using System.Linq;

namespace OpenNest.Tests.Geometry;

public class EllipseConverterTests
{
    private const double Tol = 1e-10;

    [Fact]
    public void EvaluatePoint_AtZero_ReturnsMajorAxisEnd()
    {
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
        var t = EllipseConverter.EvaluateTangent(10, 5, 0, 0);
        Assert.InRange(t.X, -Tol, Tol);
        Assert.True(t.Y > 0);
    }

    [Fact]
    public void EvaluateNormal_AtZero_PointsInward()
    {
        var n = EllipseConverter.EvaluateNormal(10, 5, 0, 0);
        Assert.True(n.X < 0);
        Assert.InRange(n.Y, -Tol, Tol);
    }

    [Fact]
    public void IntersectNormals_PerpendicularNormals_FindsCenter()
    {
        var p1 = new Vector(5, 0);
        var n1 = new Vector(-1, 0);
        var p2 = new Vector(0, 5);
        var n2 = new Vector(0, -1);
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
        var n2 = new Vector(1, 0);
        var center = EllipseConverter.IntersectNormals(p1, n1, p2, n2);
        Assert.False(center.IsValid());
    }

    [Fact]
    public void Convert_Circle_ProducesOneOrTwoArcs()
    {
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
        var result = EllipseConverter.Convert(
            new Vector(0, 0), a, b, rotation: 0,
            startParam: 0, endParam: System.Math.PI / 2, tolerance: tolerance);

        Assert.NotEmpty(result);
        Assert.All(result, e => Assert.IsType<Arc>(e));

        var firstArc = (Arc)result[0];
        var sp = firstArc.StartPoint();
        Assert.InRange(sp.X, a - 0.01, a + 0.01);
        Assert.InRange(sp.Y, -0.01, 0.01);

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
            Assert.True(gap < 1e-6,
                $"Gap of {gap:E4} between arc {i} and arc {i + 1}");
        }

        var lastArc = (Arc)result[^1];
        var firstArc = (Arc)result[0];
        var closingGap = lastArc.EndPoint().DistanceTo(firstArc.StartPoint());
        Assert.True(closingGap < 1e-6,
            $"Closing gap of {closingGap:E4}");
    }

    [Fact]
    public void Convert_WithRotationAndOffset_ProducesValidArcs()
    {
        var center = new Vector(50, -30);
        var rotation = System.Math.PI / 3;
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
            using (var stream = System.IO.File.Create(tempPath))
            using (var writer = new ACadSharp.IO.DxfWriter(stream, doc, false))
                writer.Write();

            var importer = new OpenNest.IO.DxfImporter();
            var result = importer.Import(tempPath);

            var arcCount = result.Entities.Count(e => e is Arc);
            var lineCount = result.Entities.Count(e => e is Line);

            // Should have arcs, not hundreds of lines
            Assert.True(arcCount >= 4, $"Expected at least 4 arcs, got {arcCount}");
            Assert.Equal(0, lineCount);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }

    private static double MaxDeviationFromEllipse(Arc arc, Vector ellipseCenter,
        double semiMajor, double semiMinor, double rotation, int samples)
    {
        var maxDev = 0.0;
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

            // Coarse search over 1000 samples
            var bestT = 0.0;
            var minDist = double.MaxValue;
            for (var j = 0; j <= 1000; j++)
            {
                var t = (double)j / 1000 * Angle.TwoPI;
                var ep2 = EllipseConverter.EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, t);
                var dist = arcPoint.DistanceTo(ep2);
                if (dist < minDist) { minDist = dist; bestT = t; }
            }

            // Refine with local bisection around bestT
            var lo = bestT - Angle.TwoPI / 1000;
            var hi = bestT + Angle.TwoPI / 1000;
            for (var r = 0; r < 20; r++)
            {
                var t1 = lo + (hi - lo) / 3;
                var t2 = lo + 2 * (hi - lo) / 3;
                var d1 = arcPoint.DistanceTo(EllipseConverter.EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, t1));
                var d2 = arcPoint.DistanceTo(EllipseConverter.EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, t2));
                if (d1 < d2) hi = t2; else lo = t1;
            }
            var bestDist = arcPoint.DistanceTo(EllipseConverter.EvaluatePoint(semiMajor, semiMinor, rotation, ellipseCenter, (lo + hi) / 2));
            if (bestDist > maxDev) maxDev = bestDist;
        }

        return maxDev;
    }
}
