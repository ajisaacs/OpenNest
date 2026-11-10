#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Finds a free rotation for a part that fits no sampled orientation, from the contacts it makes
/// rather than from more sampled angles.
///
/// A part on a sheet moves three ways: x, y and rotation. Its pose starts at a catalog
/// orientation with its center on the largest free pocket. Each pass collects its contacts:
/// every footprint vertex inside or near a neighbor's footprint, every neighbor vertex inside or
/// near its own, and every outline vertex at or past the work area. A contact is a linear
/// constraint on the small move (dx, dy, d-theta) that clears it with its normal, and nearby
/// clear contacts keep the move from opening new overlaps. Cyclic projection finds the least move
/// meeting them all; the pose takes it (at most one unit) and the pass repeats until nothing
/// overlaps. Three contacts then hold the part, so its angle is set by the neighbors around it.
///
/// The solver only proposes a pose. The caller checks it on the exact free region of an
/// orientation flattened at that angle, the same no-fit polygons every other placement uses,
/// so spacing and validation are unchanged. Deterministic: no clocks or randomness.
/// </summary>
internal static class ContactFit
{
    /// <summary>Most passes from one starting pose.</summary>
    private const int MaxPasses = 400;

    /// <summary>Projection sweeps over the contacts of one pass.</summary>
    private const int Sweeps = 60;

    /// <summary>Clear contacts nearer than this still constrain the move.</summary>
    private const double Margin = 0.3;

    /// <summary>Penetration still counted as touching.</summary>
    private const double Touch = 1e-4;

    /// <summary>Passes without a smaller worst overlap before the move is halved.</summary>
    private const int StallPasses = 8;

    /// <summary>Shortest move cap before the solve gives up.</summary>
    private const double MinStep = 1e-3;

    /// <summary>Worst overlap, as a share of the part's half size, of a pose still handed back
    /// for the exact check.</summary>
    private const double NearlyClear = 0.01;

    /// <summary>Largest move of one pass, in job units (rotation measured at the part's half size).</summary>
    private const double MaxStep = 1.0;

    /// <summary>An obstacle polygon and whether the moving footprint (true) or bare outline
    /// (false) must clear it: placed parts are footprints, clamp keep-outs already hold their
    /// clearance.</summary>
    internal readonly record struct Obstacle(PathD Polygon, bool AgainstFootprint);

    /// <summary>A solved pose: the absolute rotation and the translation of the snapshot origin.</summary>
    internal readonly record struct Pose(double Rotation, double X, double Y);

    /// <summary>
    /// Solves from <paramref name="start"/> at the given center; <paramref name="footprint"/>
    /// belongs to <paramref name="start"/>. Returns the clear pose, else the least-overlapping one
    /// when its worst overlap is under one percent of the part's half size, else null.
    /// </summary>
    public static Pose? Solve(
        Orientation start,
        PathD footprint,
        IReadOnlyList<Obstacle> obstacles,
        Box work,
        double centerX,
        double centerY,
        WorkCounter counter)
    {
        var bounds = Clipper.GetBounds(start.Outline);
        var pivotX = (bounds.left + bounds.right) / 2;
        var pivotY = (bounds.top + bounds.bottom) / 2;
        var radius = System.Math.Max(1e-6, System.Math.Max(bounds.right - bounds.left, bounds.bottom - bounds.top) / 2);
        var foot = Local(footprint, pivotX, pivotY);
        var line = Local(start.Outline, pivotX, pivotY);
        var near = obstacles.Select(o => (o.Polygon, o.AgainstFootprint, Bounds: Clipper.GetBounds(o.Polygon))).ToArray();

        double theta = 0, x = centerX, y = centerY;
        Pose At() => new(start.Rotation + theta,
            x - (PortableMath.Cos(theta) * pivotX - PortableMath.Sin(theta) * pivotY),
            y - (PortableMath.Sin(theta) * pivotX + PortableMath.Cos(theta) * pivotY));
        Pose? best = null;
        var bestWorst = double.PositiveInfinity;
        var cap = MaxStep;
        var stalled = 0;
        var constraints = new List<(double Gx, double Gy, double Gt, double Depth)>();
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var movingFoot = Place(foot, theta, x, y);
            var movingLine = Place(line, theta, x, y);
            constraints.Clear();
            void Add(PointD at, double nx, double ny, double depth)
            {
                var rx = at.x - x;
                var ry = at.y - y;
                constraints.Add((nx, ny, (rx * ny - ry * nx) / radius, depth));
            }

            var footBounds = Clipper.GetBounds(movingFoot);
            var lineBounds = Clipper.GetBounds(movingLine);
            foreach (var (polygon, againstFoot, box) in near)
            {
                var moving = againstFoot ? movingFoot : movingLine;
                var movingBox = againstFoot ? footBounds : lineBounds;
                if (box.left > movingBox.right + Margin || box.right < movingBox.left - Margin
                    || box.top > movingBox.bottom + Margin || box.bottom < movingBox.top - Margin)
                    continue;
                foreach (var q in moving)
                {
                    if (q.x < box.left - Margin || q.x > box.right + Margin || q.y < box.top - Margin || q.y > box.bottom + Margin)
                        continue;
                    var (distance, nx, ny) = Nearest(polygon, q);
                    if (Clipper.PointInPolygon(q, polygon) == PointInPolygonResult.IsInside)
                        Add(q, nx, ny, distance);
                    else if (distance < Margin)
                        Add(q, -nx, -ny, -distance);
                }
                foreach (var w in polygon)
                {
                    if (w.x < movingBox.left - Margin || w.x > movingBox.right + Margin
                        || w.y < movingBox.top - Margin || w.y > movingBox.bottom + Margin)
                        continue;
                    var (distance, nx, ny) = Nearest(moving, w);
                    if (Clipper.PointInPolygon(w, moving) == PointInPolygonResult.IsInside)
                        Add(w, -nx, -ny, distance);
                    else if (distance < Margin)
                        Add(w, nx, ny, -distance);
                }
                counter.Add(1);
            }
            foreach (var v in movingLine)
            {
                if (v.x - work.Left < Margin)
                    Add(v, 1, 0, work.Left - v.x);
                if (work.Right - v.x < Margin)
                    Add(v, -1, 0, v.x - work.Right);
                if (v.y - work.Bottom < Margin)
                    Add(v, 0, 1, work.Bottom - v.y);
                if (work.Top - v.y < Margin)
                    Add(v, 0, -1, v.y - work.Top);
            }

            var worst = constraints.Count == 0 ? 0 : constraints.Max(c => c.Depth);
            if (worst <= Touch)
                return At();
            if (worst < bestWorst - Touch)
            {
                best = At();
                bestWorst = worst;
                stalled = 0;
            }
            else if (++stalled >= StallPasses)
            {
                // Linearized contacts overshoot near the end; settle with shorter moves.
                cap /= 2;
                stalled = 0;
                if (cap < MinStep)
                    break;
            }

            double dx = 0, dy = 0, du = 0;
            for (var sweep = 0; sweep < Sweeps; sweep++)
                foreach (var (gx, gy, gt, depth) in constraints)
                {
                    var need = depth + Touch;
                    var have = gx * dx + gy * dy + gt * du;
                    if (have >= need)
                        continue;
                    var scale = (need - have) / (gx * gx + gy * gy + gt * gt);
                    dx += scale * gx;
                    dy += scale * gy;
                    du += scale * gt;
                }
            var step = System.Math.Sqrt(dx * dx + dy * dy + du * du);
            if (step < 1e-9)
                break; // The contacts pull against each other: no move clears them.
            if (step > cap)
            {
                dx *= cap / step;
                dy *= cap / step;
                du *= cap / step;
            }
            x += dx;
            y += dy;
            theta += du / radius;
        }
        // Nearly clear: the angle is what the caller needs, and its exact free region finds the
        // position, so a pose still overlapping slightly is worth checking.
        return bestWorst <= NearlyClear * radius ? best : null;
    }

    private static PathD Local(PathD path, double pivotX, double pivotY)
    {
        var result = new PathD(path.Count);
        foreach (var p in path)
            result.Add(new PointD(p.x - pivotX, p.y - pivotY));
        return result;
    }

    private static PathD Place(PathD local, double theta, double x, double y)
    {
        var cos = PortableMath.Cos(theta);
        var sin = PortableMath.Sin(theta);
        var result = new PathD(local.Count);
        foreach (var p in local)
            result.Add(new PointD(cos * p.x - sin * p.y + x, sin * p.x + cos * p.y + y));
        return result;
    }

    /// <summary>Distance from <paramref name="q"/> to the nearest edge of a CCW polygon, with
    /// that edge's outward normal.</summary>
    private static (double Distance, double Nx, double Ny) Nearest(PathD polygon, PointD q)
    {
        double best = double.PositiveInfinity, bestNx = 0, bestNy = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var ex = b.x - a.x;
            var ey = b.y - a.y;
            var lengthSquared = ex * ex + ey * ey;
            if (lengthSquared < 1e-18)
                continue;
            var t = System.Math.Clamp(((q.x - a.x) * ex + (q.y - a.y) * ey) / lengthSquared, 0, 1);
            var px = a.x + t * ex - q.x;
            var py = a.y + t * ey - q.y;
            var distance = System.Math.Sqrt(px * px + py * py);
            if (distance >= best)
                continue;
            var length = System.Math.Sqrt(lengthSquared);
            best = distance;
            bestNx = ey / length;
            bestNy = -ex / length;
        }
        return (best, bestNx, bestNy);
    }
}
