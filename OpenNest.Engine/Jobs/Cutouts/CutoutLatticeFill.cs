#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>
/// Fills one closed cutout of a frame part with copies of one insert part. A Fill lattice is
/// built over the cutout's bounds plus one part step on every side and shifted across a grid of
/// offsets of up to half a step each way. At each offset the copies whose spacing-grown outline
/// lies inside the cutout (a point test against the insert's inner-fit region of the cutout)
/// are counted; the offset keeping the most copies wins. Every kept pose is then certified with
/// <see cref="NestLayoutCheck.Clears"/> against the frame and the other copies.
/// </summary>
/// <remarks>
/// Suited to many small copies in a large cutout; a few large inserts belong to NFP placement.
/// Poses are in the frame's own coordinates: frame at the origin, unrotated. Not wired into
/// any engine or pipeline yet: placing parts in cutouts waits on containment-aware cutting order.
/// </remarks>
internal static class CutoutLatticeFill
{
    /// <summary>Offsets tried per side on each axis; the search covers (2n + 1)^2 offsets.</summary>
    internal const int DefaultShiftSteps = 8;

    internal const double FlattenTolerance = 0.001;

    /// <summary>Extra growth beyond the spacing, covering flattening and Clipper rounding.</summary>
    private const double Margin = FlattenTolerance + 0.001;

    private const int Precision = NestTolerances.ClipperPrecision;
    // Fill materializes an entire grid before quantity trimming. Bound work even for a
    // tiny insert with demand of only three, rather than allocating millions of clones.
    private const int MaxLatticePositions = 1000;

    /// <summary>Returns up to <paramref name="maxQuantity"/> insert poses inside the cutout,
    /// or none when no copy fits.</summary>
    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        NestJobPart insert, int maxQuantity, double spacing, CancellationToken token = default) =>
        Fill(frame, cutoutIndex, insert, maxQuantity, spacing, DefaultShiftSteps, token);

    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        NestJobPart insert, int maxQuantity, double spacing, int shiftSteps, CancellationToken token)
        => Fill(frame, cutoutIndex, insert, maxQuantity, spacing, shiftSteps,
            Array.Empty<(JobPartGeometry Geometry, NestJobPlacement Pose)>(), token);

    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        NestJobPart insert, int maxQuantity, double spacing, int shiftSteps,
        IReadOnlyList<(JobPartGeometry Geometry, NestJobPlacement Pose)> occupied, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(occupied);
        token.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(shiftSteps);
        if (maxQuantity <= 0 || !double.IsFinite(spacing) || spacing < 0)
            return Array.Empty<NestJobPlacement>();
        var frameGeometry = JobPartGeometry.TryRead(frame.Geometry);
        var insertGeometry = JobPartGeometry.TryRead(insert.Geometry);
        if (frameGeometry == null || insertGeometry == null)
            return Array.Empty<NestJobPlacement>();
        ArgumentOutOfRangeException.ThrowIfNegative(cutoutIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cutoutIndex, frameGeometry.Cutouts.Count);
        var cutout = frameGeometry.Cutouts[cutoutIndex];
        if (!cutout.IsClosed())
            return Array.Empty<NestJobPlacement>();

        // Inscribed: the flattened cutout never extends past the real one.
        var hole = Positive(ClipperBridge.ToPath(ClipperBridge.Flatten(cutout, FlattenTolerance, circumscribe: false),
            new Vector()));
        if (hole.Count < 3)
            return Array.Empty<NestJobPlacement>();
        var bounds = cutout.BoundingBox;
        var insertBounds = insertGeometry.Bounds;
        var step = System.Math.Max(insertBounds.Length, insertBounds.Width) + spacing;
        var estimated = (System.Math.Ceiling(bounds.Length / step) + 4)
            * (System.Math.Ceiling(bounds.Width / step) + 4);
        if (!(step > 0) || !double.IsFinite(estimated) || estimated > MaxLatticePositions)
            return Array.Empty<NestJobPlacement>();

        var lattice = Lattice(insert, bounds, step, spacing, token);
        if (lattice.Count == 0)
            return Array.Empty<NestJobPlacement>();
        var regions = new Dictionary<double, PathsD>();
        foreach (var rotation in lattice.Select(p => p.Rotation).Distinct())
            regions[rotation] = InnerFit(insertGeometry, rotation, hole, spacing);

        var best = (Count: 0, I: 0, J: 0);
        for (var i = -shiftSteps; i <= shiftSteps; i++)
            for (var j = -shiftSteps; j <= shiftSteps; j++)
            {
                token.ThrowIfCancellationRequested();
                var (dx, dy) = Shift(step, shiftSteps, i, j);
                var count = lattice.Count(p => Inside(regions[p.Rotation], p.X + dx, p.Y + dy)
                    && ClearsOccupied(p, dx, dy));
                if (Better(count, i, j, best))
                    best = (count, i, j);
            }
        if (best.Count == 0)
            return Array.Empty<NestJobPlacement>();

        var (sx, sy) = Shift(step, shiftSteps, best.I, best.J);
        var kept = lattice.Where(p => Inside(regions[p.Rotation], p.X + sx, p.Y + sy)
                && ClearsOccupied(p, sx, sy))
            .Select(p => new NestJobPlacement(insert.Id, 0, System.Math.Round(p.X + sx, 8),
                System.Math.Round(p.Y + sy, 8), p.Rotation))
            .OrderBy(p => p.Y).ThenBy(p => p.X).ThenBy(p => p.Rotation)
            .ToList();
        return Certify(frame.Id, frameGeometry, insertGeometry, kept, spacing, token).Take(maxQuantity)
            .Select((p, index) => p with { InstanceIndex = index }).ToArray();

        bool ClearsOccupied(NestJobPlacement pose, double dx, double dy)
        {
            token.ThrowIfCancellationRequested();
            if (occupied.Count == 0)
                return true;
            var moved = pose with
            {
                X = System.Math.Round(pose.X + dx, 8),
                Y = System.Math.Round(pose.Y + dy, 8)
            };
            foreach (var other in occupied)
            {
                token.ThrowIfCancellationRequested();
                if (!NestLayoutCheck.Clears(other.Geometry, other.Pose, insertGeometry, moved, spacing))
                    return false;
            }
            return true;
        }
    }

    /// <summary>Fill over the cutout's bounds grown by one step on every side, in frame
    /// coordinates. Fill places whole parts only, so the margin keeps every offset covered.</summary>
    private static List<NestJobPlacement> Lattice(NestJobPart insert, Box bounds, double step, double spacing,
        CancellationToken token)
    {
        var drawing = DrawingJobMapper.CreateDrawing(insert);
        try
        {
            var parts = PrivatePlateFill.Run(drawing, insert.Rotation, spacing,
                bounds.Length + 2 * step, bounds.Width + 2 * step, 0, token);
            token.ThrowIfCancellationRequested();
            var left = bounds.Left - step;
            var bottom = bounds.Bottom - step;
            return parts.Select(p => new NestJobPlacement(insert.Id, 0, p.Location.X + left, p.Location.Y + bottom,
                    System.Math.Round(Angle.NormalizeRad(p.Rotation), 10)))
                .Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && insert.Rotation.Allows(p.Rotation))
                .ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or NotSupportedException or ArithmeticException)
        {
            return new List<NestJobPlacement>();
        }
        finally
        {
            BestFitCache.Invalidate(drawing);
        }
    }

    /// <summary>
    /// Reference points t at which the insert, rotated and grown by the spacing, lies inside the
    /// cutout: some point b0 of the grown outline is inside (t in hole - b0) and the grown outline
    /// never meets the cutout boundary (t outside boundary + reflected outline).
    /// </summary>
    internal static PathsD InnerFit(JobPartGeometry insert, double rotation, PathD hole, double spacing)
    {
        var entities = insert.Perimeter.Entities.Select(e => e.Clone()).ToList();
        foreach (var entity in entities)
            entity.Rotate(rotation);
        var perimeter = new ShapeProfile(entities).Perimeter;
        var outline = Positive(ClipperBridge.ToPath(ClipperBridge.Flatten(perimeter, FlattenTolerance, circumscribe: true),
            new Vector()));
        var grown = Clipper.InflatePaths(new PathsD { outline }, spacing + Margin, JoinType.Round, EndType.Polygon,
                2.0, Precision, FlattenTolerance)
            .OrderByDescending(p => System.Math.Abs(Clipper.Area(p))).FirstOrDefault();
        if (grown == null || grown.Count < 3)
            return new PathsD();
        var reflected = new PathD(grown.Select(p => new PointD(-p.x, -p.y)));
        // Clipper.MinkowskiSum rounds to two decimals; pass the job precision explicitly.
        var forbidden = Clipper.Union(Minkowski.Sum(reflected, hole, true, Precision),
            new PathsD { Clipper.TranslatePath(reflected, hole[0].x, hole[0].y) }, FillRule.NonZero, Precision);
        var anchor = grown[0];
        return Clipper.Difference(new PathsD { Clipper.TranslatePath(hole, -anchor.x, -anchor.y) }, forbidden,
            FillRule.NonZero, Precision);
    }

    /// <summary>Drops copies that fail the production clearance check against the frame; any
    /// failing pair of copies rejects the whole fill, since the lattice itself is then unsound.</summary>
    private static IReadOnlyList<NestJobPlacement> Certify(string frameId, JobPartGeometry frame,
        JobPartGeometry insertGeometry, List<NestJobPlacement> poses, double spacing, CancellationToken token)
    {
        var framePose = new NestJobPlacement(frameId, 0, 0, 0, 0);
        var clear = new List<NestJobPlacement>();
        foreach (var pose in poses)
        {
            token.ThrowIfCancellationRequested();
            if (NestLayoutCheck.Clears(frame, framePose, insertGeometry, pose, spacing))
                clear.Add(pose);
        }
        // Rotation is about the reference point, so every copy's material lies within this
        // distance of its pose whatever its rotation.
        var b = insertGeometry.Bounds;
        var reach = new[] { (b.Left, b.Bottom), (b.Right, b.Bottom), (b.Right, b.Top), (b.Left, b.Top) }
            .Max(c => System.Math.Sqrt(c.Item1 * c.Item1 + c.Item2 * c.Item2));
        var apart = 2 * reach + spacing;
        for (var i = 0; i < clear.Count; i++)
            for (var j = i + 1; j < clear.Count; j++)
            {
                token.ThrowIfCancellationRequested();
                if (System.Math.Abs(clear[i].X - clear[j].X) > apart || System.Math.Abs(clear[i].Y - clear[j].Y) > apart)
                    continue;
                if (!NestLayoutCheck.Clears(insertGeometry, clear[i], insertGeometry, clear[j], spacing))
                    return Array.Empty<NestJobPlacement>();
            }
        return clear;
    }

    private static (double Dx, double Dy) Shift(double step, int steps, int i, int j) =>
        steps == 0 ? (0, 0) : (step * i / (2.0 * steps), step * j / (2.0 * steps));

    /// <summary>More copies win; ties keep the smaller offset, then the lower i, then j.</summary>
    private static bool Better(int count, int i, int j, (int Count, int I, int J) best)
    {
        if (count != best.Count)
            return count > best.Count;
        var distance = System.Math.Abs(i) + System.Math.Abs(j);
        var bestDistance = System.Math.Abs(best.I) + System.Math.Abs(best.J);
        if (distance != bestDistance)
            return distance < bestDistance;
        return i != best.I ? i < best.I : j < best.J;
    }

    /// <summary>Strictly inside an even-odd region; boundary points count as outside.</summary>
    internal static bool Inside(PathsD region, double x, double y)
    {
        var point = new PointD(x, y);
        var inside = false;
        foreach (var path in region)
        {
            var result = Clipper.PointInPolygon(point, path, Precision);
            if (result == PointInPolygonResult.IsOn)
                return false;
            if (result == PointInPolygonResult.IsInside)
                inside = !inside;
        }
        return inside;
    }

    internal static PathD Positive(PathD path)
    {
        if (path.Count >= 3 && !Clipper.IsPositive(path))
            path.Reverse();
        return path;
    }
}
