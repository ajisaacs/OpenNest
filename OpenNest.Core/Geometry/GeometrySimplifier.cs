using System;
using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry;

public class ArcCandidate
{
    public int ShapeIndex { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public int LineCount => EndIndex - StartIndex + 1;
    public Arc FittedArc { get; set; }
    public double MaxDeviation { get; set; }
    public Box BoundingBox { get; set; }
    public bool IsSelected { get; set; } = true;
}

public class GeometrySimplifier
{
    public double Tolerance { get; set; } = 0.005;
    public int MinLines { get; set; } = 3;

    public List<ArcCandidate> Analyze(Shape shape)
    {
        throw new NotImplementedException();
    }

    public Shape Apply(Shape shape, List<ArcCandidate> candidates)
    {
        throw new NotImplementedException();
    }

    internal static (Vector center, double radius) FitCircle(List<Vector> points)
    {
        var n = points.Count;
        if (n < 3)
            return (Vector.Invalid, 0);

        double sumX = 0, sumY = 0, sumX2 = 0, sumY2 = 0, sumXY = 0;
        double sumXZ = 0, sumYZ = 0, sumZ = 0;

        for (var i = 0; i < n; i++)
        {
            var x = points[i].X;
            var y = points[i].Y;
            var z = x * x + y * y;
            sumX += x;
            sumY += y;
            sumX2 += x * x;
            sumY2 += y * y;
            sumXY += x * y;
            sumXZ += x * z;
            sumYZ += y * z;
            sumZ += z;
        }

        // Solve: [sumX2 sumXY sumX] [A]   [sumXZ]
        //        [sumXY sumY2 sumY] [B] = [sumYZ]
        //        [sumX  sumY  n   ] [C]   [sumZ ]
        var det = sumX2 * (sumY2 * n - sumY * sumY)
                - sumXY * (sumXY * n - sumY * sumX)
                + sumX * (sumXY * sumY - sumY2 * sumX);

        if (System.Math.Abs(det) < 1e-10)
            return (Vector.Invalid, 0);

        var detA = sumXZ * (sumY2 * n - sumY * sumY)
                 - sumXY * (sumYZ * n - sumY * sumZ)
                 + sumX * (sumYZ * sumY - sumY2 * sumZ);

        var detB = sumX2 * (sumYZ * n - sumY * sumZ)
                 - sumXZ * (sumXY * n - sumY * sumX)
                 + sumX * (sumXY * sumZ - sumYZ * sumX);

        var detC = sumX2 * (sumY2 * sumZ - sumYZ * sumY)
                 - sumXY * (sumXY * sumZ - sumYZ * sumX)
                 + sumXZ * (sumXY * sumY - sumY2 * sumX);

        var a = detA / det;
        var b = detB / det;
        var c = detC / det;

        var cx = a / 2.0;
        var cy = b / 2.0;
        var rSquared = cx * cx + cy * cy + c;

        if (rSquared <= 0)
            return (Vector.Invalid, 0);

        return (new Vector(cx, cy), System.Math.Sqrt(rSquared));
    }
}
