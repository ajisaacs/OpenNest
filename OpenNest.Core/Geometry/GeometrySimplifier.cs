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
    /// <summary>First point of the original line segments this candidate covers.</summary>
    public Vector FirstPoint { get; set; }
    /// <summary>Last point of the original line segments this candidate covers.</summary>
    public Vector LastPoint { get; set; }
}

/// <summary>
/// A mirror axis defined by a point on the axis and a unit direction vector.
/// </summary>
public class MirrorAxisResult
{
    public static readonly MirrorAxisResult None = new(Vector.Invalid, Vector.Invalid, 0);

    public Vector Point { get; }
    public Vector Direction { get; }
    public double Score { get; }
    public bool IsValid => Point.IsValid();

    public MirrorAxisResult(Vector point, Vector direction, double score)
    {
        Point = point;
        Direction = direction;
        Score = score;
    }

    /// <summary>Reflects a point across this axis.</summary>
    public Vector Reflect(Vector p)
    {
        var dx = p.X - Point.X;
        var dy = p.Y - Point.Y;
        var dot = dx * Direction.X + dy * Direction.Y;
        return new Vector(
            p.X - 2 * (dx - dot * Direction.X),
            p.Y - 2 * (dy - dot * Direction.Y));
    }
}

public class GeometrySimplifier
{
    public double Tolerance { get; set; } = 0.004;
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

            var runStart = i;
            var layerName = entities[i].Layer?.Name;
            var lineCount = 0;
            while (i < entities.Count && (entities[i] is Line || entities[i] is Arc) && entities[i].Layer?.Name == layerName)
            {
                if (entities[i] is Line) lineCount++;
                i++;
            }
            var runEnd = i - 1;

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
            while (i < candidate.StartIndex)
            {
                newEntities.Add(shape.Entities[i]);
                i++;
            }

            newEntities.Add(candidate.FittedArc);
            i = candidate.EndIndex + 1;
        }

        while (i < shape.Entities.Count)
        {
            newEntities.Add(shape.Entities[i]);
            i++;
        }

        var result = new Shape();
        result.Entities.AddRange(newEntities);
        return result;
    }

    /// <summary>
    /// Detects the mirror axis of a shape by testing candidate axes through the
    /// centroid. Uses PCA to find principal directions, then also tests horizontal
    /// and vertical. Works for shapes rotated at any angle.
    /// </summary>
    public static MirrorAxisResult DetectMirrorAxis(Shape shape)
    {
        var midpoints = new List<Vector>();
        foreach (var e in shape.Entities)
            midpoints.Add(e.BoundingBox.Center);

        if (midpoints.Count < 4) return MirrorAxisResult.None;

        var centroid = new Vector(
            midpoints.Average(p => p.X),
            midpoints.Average(p => p.Y));
        var cx = centroid.X;
        var cy = centroid.Y;

        // Covariance matrix for PCA
        var cxx = 0.0;
        var cxy = 0.0;
        var cyy = 0.0;
        foreach (var p in midpoints)
        {
            var dx = p.X - cx;
            var dy = p.Y - cy;
            cxx += dx * dx;
            cxy += dx * dy;
            cyy += dy * dy;
        }

        // Eigenvectors of 2x2 symmetric matrix via analytic formula
        var trace = cxx + cyy;
        var det = cxx * cyy - cxy * cxy;
        var disc = System.Math.Sqrt(System.Math.Max(0, trace * trace / 4 - det));
        var lambda1 = trace / 2 + disc;
        var lambda2 = trace / 2 - disc;

        var candidates = new List<Vector>();

        // PCA eigenvectors (major and minor axes)
        if (System.Math.Abs(cxy) > 1e-10)
        {
            candidates.Add(Normalize(new Vector(lambda1 - cyy, cxy)));
            candidates.Add(Normalize(new Vector(lambda2 - cyy, cxy)));
        }
        else
        {
            candidates.Add(new Vector(1, 0));
            candidates.Add(new Vector(0, 1));
        }

        // Also always test pure horizontal and vertical
        candidates.Add(new Vector(1, 0));
        candidates.Add(new Vector(0, 1));

        // Score each candidate axis
        var bestResult = MirrorAxisResult.None;
        foreach (var dir in candidates)
        {
            var score = MirrorMatchScore(midpoints, centroid, dir);
            if (score > bestResult.Score)
                bestResult = new MirrorAxisResult(centroid, dir, score);
        }

        return bestResult.Score >= 0.8 ? bestResult : MirrorAxisResult.None;
    }

    private static double NormalizeAngle(double angle) =>
        angle < 0 ? angle + Angle.TwoPI : angle;

    private static Vector Normalize(Vector v)
    {
        var len = System.Math.Sqrt(v.X * v.X + v.Y * v.Y);
        return len < 1e-10 ? new Vector(1, 0) : new Vector(v.X / len, v.Y / len);
    }

    private static double PerpendicularDistance(Vector point, Vector axisPoint, Vector axisDir)
    {
        var dx = point.X - axisPoint.X;
        var dy = point.Y - axisPoint.Y;
        var dot = dx * axisDir.X + dy * axisDir.Y;
        var px = dx - dot * axisDir.X;
        var py = dy - dot * axisDir.Y;
        return System.Math.Sqrt(px * px + py * py);
    }

    private static double MirrorMatchScore(List<Vector> points, Vector axisPoint, Vector axisDir)
    {
        var matchTol = 0.1;
        var matched = 0;

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var dist = PerpendicularDistance(p, axisPoint, axisDir);

            // Points on the axis count as matched
            if (dist < matchTol)
            {
                matched++;
                continue;
            }

            // Reflect across axis and look for partner
            var reflected = new MirrorAxisResult(axisPoint, axisDir, 0).Reflect(p);

            for (var j = 0; j < points.Count; j++)
            {
                if (i == j) continue;
                var d = reflected.DistanceTo(points[j]);
                if (d < matchTol)
                {
                    matched++;
                    break;
                }
            }
        }

        return (double)matched / points.Count;
    }

    /// <summary>
    /// Pairs candidates across a mirror axis and forces each pair to use
    /// the same arc (mirrored). The candidate with more lines or lower
    /// deviation is kept as the source.
    /// </summary>
    public void Symmetrize(List<ArcCandidate> candidates, MirrorAxisResult axis)
    {
        if (!axis.IsValid || candidates.Count < 2) return;

        var paired = new HashSet<int>();

        for (var i = 0; i < candidates.Count; i++)
        {
            if (paired.Contains(i)) continue;

            var ci = candidates[i];
            var ciCenter = ci.BoundingBox.Center;
            if (PerpendicularDistance(ciCenter, axis.Point, axis.Direction) < 0.1) continue; // on the axis

            var mirrorCenter = axis.Reflect(ciCenter);

            var bestJ = -1;
            var bestDist = double.MaxValue;
            for (var j = i + 1; j < candidates.Count; j++)
            {
                if (paired.Contains(j)) continue;
                var d = mirrorCenter.DistanceTo(candidates[j].BoundingBox.Center);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestJ = j;
                }
            }

            var matchTol = System.Math.Max(ci.BoundingBox.Width, ci.BoundingBox.Length) * 0.5;
            if (bestJ < 0 || bestDist > matchTol) continue;

            paired.Add(i);
            paired.Add(bestJ);

            var cj = candidates[bestJ];
            var sourceIdx = i;
            var targetIdx = bestJ;
            if (cj.LineCount > ci.LineCount || (cj.LineCount == ci.LineCount && cj.MaxDeviation < ci.MaxDeviation))
            {
                sourceIdx = bestJ;
                targetIdx = i;
            }

            var source = candidates[sourceIdx];
            var target = candidates[targetIdx];
            var mirrored = MirrorArc(source.FittedArc, axis);

            // Only apply the mirrored arc if its endpoints are close enough to the
            // target's actual boundary points. Otherwise the mirror introduces gaps.
            var mirroredStart = mirrored.StartPoint();
            var mirroredEnd = mirrored.EndPoint();
            var startDist = mirroredStart.DistanceTo(target.FirstPoint);
            var endDist = mirroredEnd.DistanceTo(target.LastPoint);

            if (startDist <= Tolerance && endDist <= Tolerance)
            {
                target.FittedArc = mirrored;
                target.MaxDeviation = source.MaxDeviation;
            }
        }
    }

    private static Arc MirrorArc(Arc arc, MirrorAxisResult axis)
    {
        var mirrorCenter = axis.Reflect(arc.Center);

        // Reflect start and end points, then compute new angles
        var sp = arc.StartPoint();
        var ep = arc.EndPoint();
        var mirrorSp = axis.Reflect(sp);
        var mirrorEp = axis.Reflect(ep);

        // Mirroring reverses winding — swap start/end to preserve arc direction
        var mirrorStart = NormalizeAngle(System.Math.Atan2(mirrorEp.Y - mirrorCenter.Y, mirrorEp.X - mirrorCenter.X));
        var mirrorEnd = NormalizeAngle(System.Math.Atan2(mirrorSp.Y - mirrorCenter.Y, mirrorSp.X - mirrorCenter.X));

        var result = new Arc(mirrorCenter, arc.Radius, mirrorStart, mirrorEnd, arc.IsReversed);
        result.Layer = arc.Layer;
        result.Color = arc.Color;
        return result;
    }

    private void FindCandidatesInRun(List<Entity> entities, int runStart, int runEnd, List<ArcCandidate> candidates)
    {
        var j = runStart;
        var chainedTangent = Vector.Invalid;

        while (j <= runEnd - MinLines + 1)
        {
            var result = TryFitArcAt(entities, j, runEnd, chainedTangent);
            if (result == null)
            {
                j++;
                chainedTangent = Vector.Invalid;
                continue;
            }

            chainedTangent = ComputeEndTangent(result.Center, result.Points);
            var arc = CreateArc(result.Center, result.Radius, result.Points, entities[j]);
            candidates.Add(new ArcCandidate
            {
                StartIndex = j,
                EndIndex = result.EndIndex,
                FittedArc = arc,
                MaxDeviation = result.Deviation,
                BoundingBox = result.Points.GetBoundingBox(),
                FirstPoint = arc.StartPoint(),
                LastPoint = arc.EndPoint(),
            });

            j = result.EndIndex + 1;
        }
    }

    private record ArcFitResult(Vector Center, double Radius, double Deviation, List<Vector> Points, int EndIndex);

    private ArcFitResult TryFitArcAt(List<Entity> entities, int start, int runEnd, Vector chainedTangent)
    {
        var k = start + MinLines - 1;
        if (k > runEnd) return null;

        var points = CollectPoints(entities, start, k);
        if (points.Count < 3) return null;

        var startTangent = EstimateStartTangent(entities, start, points, chainedTangent);
        var endTangent = EstimateEndTangent(entities, k, points);
        var (center, radius, dev) = TryFit(points, startTangent, endTangent);
        if (!center.IsValid()) return null;

        // Extend the arc as far as possible
        while (k + 1 <= runEnd)
        {
            var extPoints = CollectPoints(entities, start, k + 1);
            if (extPoints.Count < 3) break;

            var extEndTangent = EstimateEndTangent(entities, k + 1, extPoints);
            var (nc, nr, nd) = TryFit(extPoints, startTangent, extEndTangent);
            if (!nc.IsValid()) break;

            k++;
            center = nc;
            radius = nr;
            dev = nd;
            points = extPoints;
        }

        // Reject arcs that subtend a tiny angle — these are nearly-straight lines
        // that happen to fit a huge circle. Applied after extension so that many small
        // segments can accumulate enough sweep to qualify.
        var sweep = System.Math.Abs(SumSignedAngles(center, points));
        if (sweep < Angle.ToRadians(5))
            return null;

        return new ArcFitResult(center, radius, dev, points, k);
    }

    private (Vector center, double radius, double deviation) TryFit(
        List<Vector> points, TangentEstimate start, TangentEstimate end)
    {
        foreach (var (center, radius, dev) in FitAttempts(points, start, end))
        {
            if (!center.IsValid() || dev > Tolerance)
                continue;

            // Check that the arc doesn't bulge away from the original line segments
            var isReversed = SumSignedAngles(center, points) < 0;
            var arcDev = MaxArcToSegmentDeviation(points, center, radius, isReversed);
            if (arcDev > Tolerance)
                continue;

            return (center, radius, System.Math.Max(dev, arcDev));
        }

        return (Vector.Invalid, 0, 0);
    }

    /// <summary>
    /// Yields fit attempts in preference order. A trusted tangent (chained from the
    /// previous arc, an adjacent original arc, or a long straight edge) is enforced
    /// exactly on its side; otherwise the tangency error is balanced between both
    /// endpoints. The unconstrained mirror-axis fit is the last resort. Every attempt
    /// passes exactly through both endpoints, so no gaps are introduced.
    /// </summary>
    private IEnumerable<(Vector center, double radius, double deviation)> FitAttempts(
        List<Vector> points, TangentEstimate start, TangentEstimate end)
    {
        if (start.Trusted && !end.Trusted)
        {
            yield return ArcFit.FitWithStartTangent(points, start.Direction);
            yield return ArcFit.FitThroughEndpointsWithTangents(points, start.Direction, end.Direction);
            yield return FitWithEndTangent(points, end.Direction);
        }
        else if (end.Trusted && !start.Trusted)
        {
            yield return FitWithEndTangent(points, end.Direction);
            yield return ArcFit.FitThroughEndpointsWithTangents(points, start.Direction, end.Direction);
            yield return ArcFit.FitWithStartTangent(points, start.Direction);
        }
        else
        {
            yield return ArcFit.FitThroughEndpointsWithTangents(points, start.Direction, end.Direction);
            yield return ArcFit.FitWithStartTangent(points, start.Direction);
            yield return FitWithEndTangent(points, end.Direction);
        }

        yield return FitMirrorAxis(points);
    }

    /// <summary>
    /// Fits an arc through both endpoints with an exact tangent at the last point,
    /// by running the start-tangent fit on the reversed point sequence.
    /// </summary>
    private static (Vector center, double radius, double deviation) FitWithEndTangent(
        List<Vector> points, Vector endTangent)
    {
        var reversed = new List<Vector>(points);
        reversed.Reverse();
        return ArcFit.FitWithStartTangent(reversed, new Vector(-endTangent.X, -endTangent.Y));
    }

    /// <summary>
    /// An estimated tangent direction at a fit endpoint. Trusted estimates come from
    /// exact geometry (a chained arc, an adjacent original arc, or a long straight
    /// edge) and are enforced exactly; untrusted ones are derived from the polyline
    /// vertices and only guide the fit.
    /// </summary>
    private readonly record struct TangentEstimate(Vector Direction, bool Trusted);

    /// <summary>Segment-length ratio above which a neighboring line counts as a true
    /// straight edge (rather than another chord of the tessellated curve).</summary>
    private const double NeighborEdgeFactor = 3.0;

    private static TangentEstimate EstimateStartTangent(
        List<Entity> entities, int start, List<Vector> points, Vector chainedTangent)
    {
        if (chainedTangent.IsValid())
            return new TangentEstimate(chainedTangent, true);

        if (entities[start] is Arc startArc)
            return new TangentEstimate(GetEntryDirection(startArc), true);

        var firstChordLen = points[0].DistanceTo(points[1]);
        if (start > 0)
        {
            var prev = entities[start - 1];
            var prevEnd = prev switch { Line l => l.EndPoint, Arc a => a.EndPoint(), _ => Vector.Invalid };
            if (prevEnd.IsValid() && prevEnd.DistanceTo(points[0]) < 1e-6)
            {
                if (prev is Arc)
                    return new TangentEstimate(GetExitDirection(prev), true);
                if (prev is Line prevLine && prevLine.StartPoint.DistanceTo(prevLine.EndPoint) >= NeighborEdgeFactor * firstChordLen)
                    return new TangentEstimate(GetExitDirection(prevLine), true);
            }
        }

        var chord = new Vector(points[1].X - points[0].X, points[1].Y - points[0].Y);
        if (points.Count >= 3)
            return new TangentEstimate(EstimateVertexTangent(points[0], points[1], points[2], chord), false);
        return new TangentEstimate(chord, false);
    }

    private static TangentEstimate EstimateEndTangent(List<Entity> entities, int k, List<Vector> points)
    {
        if (entities[k] is Arc endArc)
            return new TangentEstimate(GetExitDirection(endArc), true);

        var lastChordLen = points[^1].DistanceTo(points[^2]);
        if (k + 1 < entities.Count)
        {
            var next = entities[k + 1];
            var nextStart = next switch { Line l => l.StartPoint, Arc a => a.StartPoint(), _ => Vector.Invalid };
            if (nextStart.IsValid() && nextStart.DistanceTo(points[^1]) < 1e-6)
            {
                if (next is Arc nextArc)
                    return new TangentEstimate(GetEntryDirection(nextArc), true);
                if (next is Line nextLine && nextLine.StartPoint.DistanceTo(nextLine.EndPoint) >= NeighborEdgeFactor * lastChordLen)
                    return new TangentEstimate(GetExitDirection(nextLine), true);
            }
        }

        var chord = new Vector(points[^1].X - points[^2].X, points[^1].Y - points[^2].Y);
        if (points.Count >= 3)
            return new TangentEstimate(EstimateVertexTangent(points[^1], points[^2], points[^3], chord), false);
        return new TangentEstimate(chord, false);
    }

    /// <summary>
    /// Estimates the curve tangent at a polyline vertex from the circle through it and
    /// its two nearest neighbors. A raw chord direction is off from the true tangent by
    /// half the chord's subtended angle; the circumcircle estimate removes that bias.
    /// Falls back to the travel direction when the three points are collinear.
    /// </summary>
    private static Vector EstimateVertexTangent(Vector at, Vector b, Vector c, Vector travel)
    {
        var d = 2 * (at.X * (b.Y - c.Y) + b.X * (c.Y - at.Y) + c.X * (at.Y - b.Y));
        if (System.Math.Abs(d) < 1e-14)
            return travel;

        var sqA = at.X * at.X + at.Y * at.Y;
        var sqB = b.X * b.X + b.Y * b.Y;
        var sqC = c.X * c.X + c.Y * c.Y;
        var cx = (sqA * (b.Y - c.Y) + sqB * (c.Y - at.Y) + sqC * (at.Y - b.Y)) / d;
        var cy = (sqA * (c.X - b.X) + sqB * (at.X - c.X) + sqC * (b.X - at.X)) / d;

        var tangent = new Vector(-(at.Y - cy), at.X - cx);
        if (tangent.X * travel.X + tangent.Y * travel.Y < 0)
            tangent = new Vector(-tangent.X, -tangent.Y);
        return tangent;
    }

    /// <summary>
    /// Returns the entry direction (tangent at start point) of an entity.
    /// </summary>
    private static Vector GetEntryDirection(Entity entity) => entity switch
    {
        Line line => new Vector(line.EndPoint.X - line.StartPoint.X, line.EndPoint.Y - line.StartPoint.Y),
        Arc arc => arc.IsReversed
            ? new Vector(System.Math.Sin(arc.StartAngle), -System.Math.Cos(arc.StartAngle))
            : new Vector(-System.Math.Sin(arc.StartAngle), System.Math.Cos(arc.StartAngle)),
        _ => Vector.Invalid,
    };

    /// <summary>
    /// Computes the tangent direction at the last point of a fitted arc,
    /// used to chain tangent continuity to the next arc.
    /// </summary>
    private static Vector ComputeEndTangent(Vector center, List<Vector> points)
    {
        var lastPt = points[^1];
        var rx = lastPt.X - center.X;
        var ry = lastPt.Y - center.Y;
        var sign = SumSignedAngles(center, points) >= 0 ? 1 : -1;
        return new Vector(-sign * ry, sign * rx);
    }

    /// <summary>
    /// Fits a circular arc using the mirror axis approach. The center is constrained
    /// to the perpendicular bisector of the chord (P1->Pn), guaranteeing the arc
    /// passes exactly through both endpoints. Golden section search optimizes position.
    /// </summary>
    private (Vector center, double radius, double deviation) FitMirrorAxis(List<Vector> points)
    {
        if (points.Count < 3)
            return (Vector.Invalid, 0, double.MaxValue);

        var p1 = points[0];
        var pn = points[^1];
        var mx = (p1.X + pn.X) / 2;
        var my = (p1.Y + pn.Y) / 2;
        var dx = pn.X - p1.X;
        var dy = pn.Y - p1.Y;
        var chordLen = System.Math.Sqrt(dx * dx + dy * dy);
        if (chordLen < 1e-10)
            return (Vector.Invalid, 0, double.MaxValue);

        var halfChord = chordLen / 2;
        var nx = -dy / chordLen;
        var ny = dx / chordLen;

        var maxSagitta = 0.0;
        for (var i = 1; i < points.Count - 1; i++)
        {
            var proj = (points[i].X - mx) * nx + (points[i].Y - my) * ny;
            if (System.Math.Abs(proj) > System.Math.Abs(maxSagitta))
                maxSagitta = proj;
        }
        if (System.Math.Abs(maxSagitta) < 1e-10)
            return (Vector.Invalid, 0, double.MaxValue);

        var dInit = (maxSagitta * maxSagitta - halfChord * halfChord) / (2 * maxSagitta);
        var range = System.Math.Max(System.Math.Abs(dInit) * 2, halfChord);

        var dOpt = GoldenSectionMin(dInit - range, dInit + range,
            d => ArcFit.MaxRadialDeviation(points, mx + d * nx, my + d * ny,
                System.Math.Sqrt(halfChord * halfChord + d * d)));

        var center = new Vector(mx + dOpt * nx, my + dOpt * ny);
        var radius = System.Math.Sqrt(halfChord * halfChord + dOpt * dOpt);
        return (center, radius, ArcFit.MaxRadialDeviation(points, center.X, center.Y, radius));
    }

    private static double GoldenSectionMin(double low, double high, Func<double, double> eval)
    {
        var phi = (System.Math.Sqrt(5) - 1) / 2;
        for (var iter = 0; iter < 30; iter++)
        {
            var d1 = high - phi * (high - low);
            var d2 = low + phi * (high - low);
            if (eval(d1) < eval(d2))
                high = d2;
            else
                low = d1;
            if (high - low < 1e-6)
                break;
        }
        return (low + high) / 2;
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
                    var segments = System.Math.Max(2, arc.SegmentsForTolerance(0.1));
                    var arcPoints = arc.ToPoints(segments);
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

        var startAngle = NormalizeAngle(System.Math.Atan2(firstPoint.Y - center.Y, firstPoint.X - center.X));
        var endAngle = NormalizeAngle(System.Math.Atan2(lastPoint.Y - center.Y, lastPoint.X - center.X));
        var isReversed = SumSignedAngles(center, points) < 0;

        var arc = new Arc(center, radius, startAngle, endAngle, isReversed);
        arc.Layer = sourceEntity.Layer;
        arc.Color = sourceEntity.Color;
        return arc;
    }

    /// <summary>
    /// Returns the exit direction (tangent at endpoint) of an entity.
    /// </summary>
    private static Vector GetExitDirection(Entity entity) => entity switch
    {
        Line line => new Vector(line.EndPoint.X - line.StartPoint.X, line.EndPoint.Y - line.StartPoint.Y),
        Arc arc => arc.IsReversed
            ? new Vector(System.Math.Sin(arc.EndAngle), -System.Math.Cos(arc.EndAngle))
            : new Vector(-System.Math.Sin(arc.EndAngle), System.Math.Cos(arc.EndAngle)),
        _ => Vector.Invalid,
    };

    /// <summary>
    /// Sums signed angular change traversing consecutive points around a center.
    /// Positive = CCW, negative = CW.
    /// </summary>
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

    /// <summary>
    /// Measures the maximum distance from sampled points along the fitted arc
    /// back to the original line segments. This catches cases where points lie
    /// on a large circle but the arc bulges far from the original straight geometry.
    /// </summary>
    private static double MaxArcToSegmentDeviation(List<Vector> points, Vector center, double radius, bool isReversed)
    {
        var startAngle = System.Math.Atan2(points[0].Y - center.Y, points[0].X - center.X);
        var endAngle = System.Math.Atan2(points[^1].Y - center.Y, points[^1].X - center.X);

        var sweep = endAngle - startAngle;
        if (isReversed)
        {
            if (sweep > 0) sweep -= Angle.TwoPI;
        }
        else
        {
            if (sweep < 0) sweep += Angle.TwoPI;
        }

        var sampleCount = System.Math.Max(10, (int)(System.Math.Abs(sweep) * radius * 10));
        sampleCount = System.Math.Min(sampleCount, 100);

        var maxDev = 0.0;
        for (var i = 1; i < sampleCount; i++)
        {
            var t = (double)i / sampleCount;
            var angle = startAngle + sweep * t;
            var px = center.X + radius * System.Math.Cos(angle);
            var py = center.Y + radius * System.Math.Sin(angle);
            var arcPt = new Vector(px, py);

            var minDist = double.MaxValue;
            for (var j = 0; j < points.Count - 1; j++)
            {
                var dist = DistanceToSegment(arcPt, points[j], points[j + 1]);
                if (dist < minDist) minDist = dist;
            }
            if (minDist > maxDev) maxDev = minDist;
        }
        return maxDev;
    }

    private static double DistanceToSegment(Vector p, Vector a, Vector b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-20)
            return System.Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = System.Math.Max(0, System.Math.Min(1, t));
        var projX = a.X + t * dx;
        var projY = a.Y + t * dy;
        return System.Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
    }
}
