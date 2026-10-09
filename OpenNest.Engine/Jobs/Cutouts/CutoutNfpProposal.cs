#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>Test-only single-insert inner-fit proposal; never changes a job or a live plate.</summary>
internal static class CutoutNfpProposal
{
    // Fixed chord tolerance in the shared round offset otherwise makes extremely large
    // spacing values allocate an unbounded number of arc vertices. This conservative
    // test-only kernel declines scales it cannot process safely.
    private const double MaxCoordinate = 1e8;
    private const double MaxSpacing = 100;

    /// <summary>Returns one frame-local pose inside a closed cutout, or null if no pose is certified.
    /// A null result is not a proof that the insert cannot fit elsewhere in this cutout.</summary>
    internal static NestJobPlacement? Find(NestJobPart frame, int cutoutIndex, NestJobPart insert,
        double spacing, CancellationToken token = default) =>
        Find(frame, cutoutIndex, insert, spacing,
            Array.Empty<(JobPartGeometry Geometry, NestJobPlacement Pose)>(), token);

    /// <summary>Find a pose clearing already accepted inserts as well as the enclosing frame.</summary>
    internal static NestJobPlacement? Find(NestJobPart frame, int cutoutIndex, NestJobPart insert,
        double spacing, IReadOnlyList<(JobPartGeometry Geometry, NestJobPlacement Pose)> occupied,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(occupied);
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(spacing) || spacing < 0 || spacing > MaxSpacing)
            return null;
        var frameGeometry = JobPartGeometry.TryRead(frame.Geometry);
        var insertGeometry = JobPartGeometry.TryRead(insert.Geometry);
        if (frameGeometry == null || insertGeometry == null
            || !WithinRange(frameGeometry.Bounds) || !WithinRange(insertGeometry.Bounds))
            return null;
        ArgumentOutOfRangeException.ThrowIfNegative(cutoutIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cutoutIndex, frameGeometry.Cutouts.Count);
        var cutout = frameGeometry.Cutouts[cutoutIndex];
        if (!cutout.IsClosed() || !WithinRange(cutout.BoundingBox)
            || spacing * 2 >= System.Math.Min(cutout.BoundingBox.Length, cutout.BoundingBox.Width))
            return null;
        var hole = CutoutLatticeFill.Positive(ClipperBridge.ToPath(
            ClipperBridge.Flatten(cutout, CutoutLatticeFill.FlattenTolerance, circumscribe: false),
            new Vector()));
        if (hole.Count < 3)
            return null;
        var framePose = new NestJobPlacement(frame.Id, 0, 0, 0, 0);
        foreach (var rotation in insert.Rotation.EnumerateAngles())
        {
            token.ThrowIfCancellationRequested();
            var region = CutoutLatticeFill.InnerFit(insertGeometry, rotation, hole, spacing);
            token.ThrowIfCancellationRequested();
            foreach (var (x, y) in Candidates(region))
            {
                token.ThrowIfCancellationRequested();
                if (!CutoutLatticeFill.Inside(region, x, y))
                    continue;
                var pose = new NestJobPlacement(insert.Id, 0, x, y, rotation);
                if (!NestLayoutCheck.Clears(frameGeometry, framePose, insertGeometry, pose, spacing))
                    continue;
                var clears = true;
                foreach (var other in occupied)
                {
                    token.ThrowIfCancellationRequested();
                    if (NestLayoutCheck.Clears(other.Geometry, other.Pose, insertGeometry, pose, spacing))
                        continue;
                    clears = false;
                    break;
                }
                token.ThrowIfCancellationRequested();
                if (clears)
                    return pose;
            }
        }
        token.ThrowIfCancellationRequested();
        return null;
    }

    private static bool WithinRange(Box box) =>
        double.IsFinite(box.Left) && double.IsFinite(box.Right)
        && double.IsFinite(box.Bottom) && double.IsFinite(box.Top)
        && System.Math.Abs(box.Left) <= MaxCoordinate && System.Math.Abs(box.Right) <= MaxCoordinate
        && System.Math.Abs(box.Bottom) <= MaxCoordinate && System.Math.Abs(box.Top) <= MaxCoordinate;

    /// <summary>Sample component interiors, then points just inside each boundary toward
    /// its component's center. This bounded search is not a complete inner-fit solver.</summary>
    private static IEnumerable<(double X, double Y)> Candidates(PathsD region)
    {
        foreach (var path in region.Where(p => p.Count >= 3 && Clipper.IsPositive(p)))
        {
            var bounds = Clipper.GetBounds(new PathsD { path });
            var cx = (bounds.left + bounds.right) / 2;
            var cy = (bounds.top + bounds.bottom) / 2;
            yield return (cx, cy);
            foreach (var point in path)
                yield return (point.x * 0.99 + cx * 0.01, point.y * 0.99 + cy * 0.01);
            // Bounded interior samples let a residual insert land beside a partial lattice;
            // a center/vertex-only sample often lands directly on an earlier copy.
            for (var y = 1; y <= 9; y++)
                for (var x = 1; x <= 9; x++)
                    yield return (bounds.left + (bounds.right - bounds.left) * x / 10,
                        bounds.top + (bounds.bottom - bounds.top) * y / 10);
        }
    }
}
