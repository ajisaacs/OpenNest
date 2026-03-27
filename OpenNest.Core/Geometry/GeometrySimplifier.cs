using System;
using System.Collections.Generic;
using System.Linq;
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
    public double Tolerance { get; set; } = 0.5;
    public int MinLines { get; set; } = 3;

    public List<ArcCandidate> Analyze(Shape shape)
    {
        var candidates = new List<ArcCandidate>();
        var entities = shape.Entities;
        var i = 0;

        while (i < entities.Count)
        {
            if (entities[i] is not Line and not Arc)
            {
                i++;
                continue;
            }

            // Collect consecutive lines and arcs on the same layer
            var runStart = i;
            var layer = entities[i].Layer;
            var lineCount = 0;
            while (i < entities.Count && (entities[i] is Line || entities[i] is Arc) && entities[i].Layer == layer)
            {
                if (entities[i] is Line) lineCount++;
                i++;
            }
            var runEnd = i - 1;

            // Only analyze runs that have enough line entities to simplify
            if (lineCount >= MinLines)
                FindCandidatesInRun(entities, runStart, runEnd, candidates);
        }

        return candidates;
    }

    public Shape Apply(Shape shape, List<ArcCandidate> candidates)
    {
        var selected = candidates
            .Where(c => c.IsSelected)
            .OrderBy(c => c.StartIndex)
            .ToList();

        var newEntities = new List<Entity>();
        var i = 0;

        foreach (var candidate in selected)
        {
            // Copy entities before this candidate
            while (i < candidate.StartIndex)
            {
                newEntities.Add(shape.Entities[i]);
                i++;
            }

            // Insert the fitted arc
            newEntities.Add(candidate.FittedArc);

            // Skip past the replaced lines
            i = candidate.EndIndex + 1;
        }

        // Copy remaining entities
        while (i < shape.Entities.Count)
        {
            newEntities.Add(shape.Entities[i]);
            i++;
        }

        var result = new Shape();
        result.Entities.AddRange(newEntities);
        return result;
    }

    private void FindCandidatesInRun(List<Entity> entities, int runStart, int runEnd, List<ArcCandidate> candidates)
    {
        var j = runStart;

        while (j <= runEnd - MinLines + 1)
        {
            // Need at least MinLines entities ahead
            var k = j + MinLines - 1;
            if (k > runEnd) break;

            var points = CollectPoints(entities, j, k);
            if (points.Count < 3)
            {
                j++;
                continue;
            }

            var (center, radius, dev) = FitMirrorAxis(points);

            if (!center.IsValid() || dev > Tolerance)
            {
                j++;
                continue;
            }

            // Extend as far as possible
            var prevCenter = center;
            var prevRadius = radius;
            var prevDev = dev;

            while (k + 1 <= runEnd)
            {
                k++;
                points = CollectPoints(entities, j, k);
                if (points.Count < 3)
                {
                    k--;
                    break;
                }

                var (nc, nr, nd) = FitMirrorAxis(points);

                if (!nc.IsValid() || nd > Tolerance)
                {
                    k--;
                    break;
                }

                prevCenter = nc;
                prevRadius = nr;
                prevDev = nd;
            }

            var finalPoints = CollectPoints(entities, j, k);
            var arc = CreateArc(prevCenter, prevRadius, finalPoints, entities[j]);
            var bbox = ComputeBoundingBox(finalPoints);

            candidates.Add(new ArcCandidate
            {
                StartIndex = j,
                EndIndex = k,
                FittedArc = arc,
                MaxDeviation = prevDev,
                BoundingBox = bbox,
            });

            j = k + 1;
        }
    }

    /// <summary>
    /// Fits a circular arc through a set of points using the mirror axis approach.
    /// The center is constrained to lie on the perpendicular bisector of the chord
    /// (P1→Pn), guaranteeing the arc passes exactly through both endpoints.
    /// Golden section search finds the optimal position along this axis.
    /// </summary>
    private (Vector center, double radius, double deviation) FitMirrorAxis(List<Vector> points)
    {
        if (points.Count < 3)
            return (Vector.Invalid, 0, double.MaxValue);

        var p1 = points[0];
        var pn = points[^1];

        // Chord midpoint and length
        var mx = (p1.X + pn.X) / 2;
        var my = (p1.Y + pn.Y) / 2;
        var dx = pn.X - p1.X;
        var dy = pn.Y - p1.Y;
        var chordLen = System.Math.Sqrt(dx * dx + dy * dy);

        if (chordLen < 1e-10)
            return (Vector.Invalid, 0, double.MaxValue);

        var halfChord = chordLen / 2;

        // Unit normal (mirror axis direction, perpendicular to chord)
        var nx = -dy / chordLen;
        var ny = dx / chordLen;

        // Find max signed projection onto the normal (sagitta with sign)
        var maxSagitta = 0.0;
        for (var i = 1; i < points.Count - 1; i++)
        {
            var proj = (points[i].X - mx) * nx + (points[i].Y - my) * ny;
            if (System.Math.Abs(proj) > System.Math.Abs(maxSagitta))
                maxSagitta = proj;
        }

        if (System.Math.Abs(maxSagitta) < 1e-10)
            return (Vector.Invalid, 0, double.MaxValue); // collinear

        // Initial d estimate from sagitta geometry:
        // Center at M + d*N, radius R = sqrt(halfChord² + d²)
        // For a point on the arc at perpendicular distance s from chord:
        //   (d - s)² = halfChord² + d²  →  d = (s² - halfChord²) / (2s)
        var dInit = (maxSagitta * maxSagitta - halfChord * halfChord) / (2 * maxSagitta);

        // Golden section search around initial estimate
        var range = System.Math.Max(System.Math.Abs(dInit) * 2, halfChord);
        var dLow = dInit - range;
        var dHigh = dInit + range;

        var phi = (System.Math.Sqrt(5) - 1) / 2;
        for (var iter = 0; iter < 50; iter++)
        {
            var d1 = dHigh - phi * (dHigh - dLow);
            var d2 = dLow + phi * (dHigh - dLow);

            var dev1 = EvalDeviation(points, mx, my, nx, ny, halfChord, d1);
            var dev2 = EvalDeviation(points, mx, my, nx, ny, halfChord, d2);

            if (dev1 < dev2)
                dHigh = d2;
            else
                dLow = d1;

            if (dHigh - dLow < 1e-12)
                break;
        }

        var dOpt = (dLow + dHigh) / 2;
        var center = new Vector(mx + dOpt * nx, my + dOpt * ny);
        var radius = System.Math.Sqrt(halfChord * halfChord + dOpt * dOpt);
        var deviation = EvalDeviation(points, mx, my, nx, ny, halfChord, dOpt);

        return (center, radius, deviation);
    }

    /// <summary>
    /// Evaluates the max deviation of intermediate points from the circle
    /// defined by center = M + d*N, radius = sqrt(halfChord² + d²).
    /// Endpoints are excluded since they're on the circle by construction.
    /// </summary>
    private static double EvalDeviation(List<Vector> points,
        double mx, double my, double nx, double ny, double halfChord, double d)
    {
        var cx = mx + d * nx;
        var cy = my + d * ny;
        var r = System.Math.Sqrt(halfChord * halfChord + d * d);

        var maxDev = 0.0;
        for (var i = 1; i < points.Count - 1; i++)
        {
            var px = points[i].X - cx;
            var py = points[i].Y - cy;
            var dist = System.Math.Sqrt(px * px + py * py);
            var dev = System.Math.Abs(dist - r);
            if (dev > maxDev)
                maxDev = dev;
        }
        return maxDev;
    }

    private static List<Vector> CollectPoints(List<Entity> entities, int start, int end)
    {
        var points = new List<Vector>();

        for (var i = start; i <= end; i++)
        {
            switch (entities[i])
            {
                case Line line:
                    if (i == start)
                        points.Add(line.StartPoint);
                    points.Add(line.EndPoint);
                    break;

                case Arc arc:
                    if (i == start)
                        points.Add(arc.StartPoint());
                    // Sample intermediate points so deviation is measured
                    // accurately across the full arc span
                    var segments = System.Math.Max(2, arc.SegmentsForTolerance(0.1));
                    var arcPoints = arc.ToPoints(segments);
                    // Skip first (already added or connects to previous) and add the rest
                    for (var j = 1; j < arcPoints.Count; j++)
                        points.Add(arcPoints[j]);
                    break;
            }
        }

        return points;
    }

    private static Arc CreateArc(Vector center, double radius, List<Vector> points, Entity sourceEntity)
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
