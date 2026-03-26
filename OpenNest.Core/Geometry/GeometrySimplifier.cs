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
        var candidates = new List<ArcCandidate>();
        var entities = shape.Entities;
        var i = 0;

        while (i < entities.Count)
        {
            if (entities[i] is not Line firstLine)
            {
                i++;
                continue;
            }

            // Collect consecutive lines on the same layer
            var runStart = i;
            var layer = firstLine.Layer;
            while (i < entities.Count && entities[i] is Line line && line.Layer == layer)
                i++;
            var runEnd = i - 1;

            // Try to find arc candidates within this run
            FindCandidatesInRun(entities, runStart, runEnd, candidates);
        }

        return candidates;
    }

    public Shape Apply(Shape shape, List<ArcCandidate> candidates)
    {
        throw new NotImplementedException();
    }

    private void FindCandidatesInRun(List<Entity> entities, int runStart, int runEnd, List<ArcCandidate> candidates)
    {
        var j = runStart;

        while (j <= runEnd - MinLines + 1)
        {
            // Start with MinLines lines
            var k = j + MinLines - 1;
            var points = CollectPoints(entities, j, k);
            var (center, radius) = FitCircle(points);

            if (!center.IsValid() || MaxDeviation(points, center, radius) > Tolerance)
            {
                j++;
                continue;
            }

            // Extend as far as possible
            var prevCenter = center;
            var prevRadius = radius;
            var prevMaxDev = MaxDeviation(points, center, radius);

            while (k + 1 <= runEnd)
            {
                k++;
                points = CollectPoints(entities, j, k);
                var (newCenter, newRadius) = FitCircle(points);
                if (!newCenter.IsValid())
                {
                    k--;
                    break;
                }

                var newMaxDev = MaxDeviation(points, newCenter, newRadius);
                if (newMaxDev > Tolerance)
                {
                    k--;
                    break;
                }

                prevCenter = newCenter;
                prevRadius = newRadius;
                prevMaxDev = newMaxDev;
            }

            // Build the candidate
            var finalPoints = CollectPoints(entities, j, k);
            var arc = BuildArc(prevCenter, prevRadius, finalPoints, entities[j]);
            var bbox = ComputeBoundingBox(finalPoints);

            candidates.Add(new ArcCandidate
            {
                StartIndex = j,
                EndIndex = k,
                FittedArc = arc,
                MaxDeviation = prevMaxDev,
                BoundingBox = bbox,
            });

            j = k + 1;
        }
    }

    private static List<Vector> CollectPoints(List<Entity> entities, int start, int end)
    {
        var points = new List<Vector>();
        points.Add(((Line)entities[start]).StartPoint);
        for (var i = start; i <= end; i++)
            points.Add(((Line)entities[i]).EndPoint);
        return points;
    }

    private static double MaxDeviation(List<Vector> points, Vector center, double radius)
    {
        var maxDev = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var dev = System.Math.Abs(points[i].DistanceTo(center) - radius);
            if (dev > maxDev)
                maxDev = dev;
        }
        return maxDev;
    }

    private static Arc BuildArc(Vector center, double radius, List<Vector> points, Entity sourceEntity)
    {
        var firstPoint = points[0];
        var lastPoint = points[^1];

        var startAngle = System.Math.Atan2(firstPoint.Y - center.Y, firstPoint.X - center.X);
        var endAngle = System.Math.Atan2(lastPoint.Y - center.Y, lastPoint.X - center.X);

        // Determine direction by summing signed angular changes
        var totalAngle = 0.0;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a1 = System.Math.Atan2(points[i].Y - center.Y, points[i].X - center.X);
            var a2 = System.Math.Atan2(points[i + 1].Y - center.Y, points[i + 1].X - center.X);
            var da = a2 - a1;
            while (da > System.Math.PI) da -= Angle.TwoPI;
            while (da < -System.Math.PI) da += Angle.TwoPI;
            totalAngle += da;
        }

        var isReversed = totalAngle < 0;

        // Normalize angles to [0, 2pi)
        if (startAngle < 0) startAngle += Angle.TwoPI;
        if (endAngle < 0) endAngle += Angle.TwoPI;

        var arc = new Arc(center, radius, startAngle, endAngle, isReversed);
        arc.Layer = sourceEntity.Layer;
        arc.Color = sourceEntity.Color;
        return arc;
    }

    private static Box ComputeBoundingBox(List<Vector> points)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        for (var i = 0; i < points.Count; i++)
        {
            if (points[i].X < minX) minX = points[i].X;
            if (points[i].Y < minY) minY = points[i].Y;
            if (points[i].X > maxX) maxX = points[i].X;
            if (points[i].Y > maxY) maxY = points[i].Y;
        }

        return new Box(minX, minY, maxX - minX, maxY - minY);
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
