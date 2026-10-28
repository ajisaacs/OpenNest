#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>
/// Frame-local cutout proposal that packs one closed cutout with the Irregular engine's
/// <see cref="FrontierPacker"/>, taking the cutout as the packing boundary and every insert
/// requirement at once: parts settle against the cutout wall and each other instead of at the
/// first sampled point that clears. Each of the engine's strategy variants packs the hole; the
/// variant seating the most copies wins, ties going to the tighter layout. Every pose is then
/// certified with <see cref="NestLayoutCheck.Clears"/> against the frame, the occupants and the
/// other accepted poses, exactly as the lattice and NFP proposals are. Never changes a job.
/// </summary>
internal static class CutoutPackerProposal
{
    private const double MaxCoordinate = 1e8;
    private const double MaxSpacing = 100;
    private const double Tie = 1e-6;

    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing, CancellationToken token = default) =>
        Fill(frame, cutoutIndex, inserts, spacing,
            Array.Empty<(JobPartGeometry Geometry, NestJobPlacement Pose)>(), null, token);

    /// <summary>Poses in the frame's own coordinates, in packing order, clearing the frame,
    /// the <paramref name="occupied"/> inserts and each other; instance indices count per
    /// requirement.</summary>
    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing,
        IReadOnlyList<(JobPartGeometry Geometry, NestJobPlacement Pose)> occupied, CutoutMemo? memo,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(inserts);
        ArgumentNullException.ThrowIfNull(occupied);
        token.ThrowIfCancellationRequested();
        if (inserts.Count == 0 || !double.IsFinite(spacing) || spacing < 0 || spacing > MaxSpacing)
            return Array.Empty<NestJobPlacement>();
        var frameGeometry = memo?.Read(frame.Geometry) ?? JobPartGeometry.TryRead(frame.Geometry);
        if (frameGeometry == null || !WithinRange(frameGeometry.Bounds))
            return Array.Empty<NestJobPlacement>();
        ArgumentOutOfRangeException.ThrowIfNegative(cutoutIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cutoutIndex, frameGeometry.Cutouts.Count);
        var cutout = frameGeometry.Cutouts[cutoutIndex];
        if (!cutout.IsClosed() || !WithinRange(cutout.BoundingBox)
            || spacing * 2 >= System.Math.Min(cutout.BoundingBox.Length, cutout.BoundingBox.Width))
            return Array.Empty<NestJobPlacement>();
        var geometries = new Dictionary<string, JobPartGeometry>(StringComparer.Ordinal);
        foreach (var insert in inserts)
        {
            var geometry = memo?.Read(insert.Geometry) ?? JobPartGeometry.TryRead(insert.Geometry);
            if (geometry == null || !WithinRange(geometry.Bounds))
                return Array.Empty<NestJobPlacement>();
            geometries[insert.Id] = geometry;
        }

        // Inscribed: the flattened cutout never extends past the real one.
        var hole = CutoutLatticeFill.Positive(ClipperBridge.ToPath(
            ClipperBridge.Flatten(cutout, CutoutLatticeFill.FlattenTolerance, circumscribe: false), new Vector()));
        if (hole.Count < 3)
            return Array.Empty<NestJobPlacement>();
        var box = cutout.BoundingBox;
        var stock = new NestPlateStock("cutout", new Size(box.Width, box.Length), 1, spacing);
        var types = PartCatalog.Build(new NestJob(inserts, new[] { stock }));
        if (types.All(t => t.Orientations.Count == 0))
            return Array.Empty<NestJobPlacement>();
        token.ThrowIfCancellationRequested();

        var nfps = new NoFitCache(spacing);
        var obstacles = occupied.Select(o => Outline(o.Geometry, o.Pose)).ToList();
        var boundary = new PackBoundary(hole, obstacles, nfps);
        var pairs = new Dictionary<int, IReadOnlyList<PairPose>>();
        var counter = new WorkCounter();
        var demand = types.Select(t => t.Part.Quantity).ToArray();
        var framePose = new NestJobPlacement(frame.Id, 0, 0, 0, 0);
        var preparedFrame = NestLayoutCheck.Prepare(frameGeometry, framePose, spacing);
        var preparedOccupied = new NestLayoutCheck.PreparedPlacement[occupied.Count];
        for (var i = 0; i < occupied.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            preparedOccupied[i] = NestLayoutCheck.Prepare(occupied[i].Geometry, occupied[i].Pose, spacing);
        }

        List<NestJobPlacement>? best = null;
        foreach (var (axis, beta) in IrregularNestingEngine.Variants)
        {
            token.ThrowIfCancellationRequested();
            var packer = new FrontierPacker(types, nfps, pairs, stock, axis, beta, counter, boundary: boundary);
            var fill = packer.Fill(demand, token);
            var poses = new List<NestJobPlacement>(fill.Parts.Count);
            foreach (var placed in fill.Parts)
            {
                token.ThrowIfCancellationRequested();
                var part = types[placed.Orientation.TypeIndex].Part;
                var pose = new NestJobPlacement(part.Id, 0, System.Math.Round(placed.X, 8),
                    System.Math.Round(placed.Y, 8), placed.Orientation.Rotation);
                if (Clears(pose, geometries[part.Id], preparedFrame, preparedOccupied, poses, geometries, spacing, memo, token))
                    poses.Add(pose);
            }
            if (poses.Count == 0)
                continue;
            if (best == null || poses.Count > best.Count
                || (poses.Count == best.Count && Tighter(poses, best, id => geometries[id])))
                best = poses;
        }
        token.ThrowIfCancellationRequested();
        if (best == null)
            return Array.Empty<NestJobPlacement>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        return best.Select(p =>
        {
            var index = counts.GetValueOrDefault(p.PartId);
            counts[p.PartId] = index + 1;
            return p with { InstanceIndex = index };
        }).ToArray();
    }

    /// <summary>True when <paramref name="candidate"/> is the more compact layout: a smaller
    /// bounding box of the placed inserts, then one lower, then one further left.</summary>
    internal static bool Tighter(IReadOnlyList<NestJobPlacement> candidate,
        IReadOnlyList<NestJobPlacement> incumbent, Func<string, JobPartGeometry> geometry)
    {
        if (candidate.Count == 0)
            return false;
        if (incumbent.Count == 0)
            return true;
        var a = Envelope(candidate, geometry);
        var b = Envelope(incumbent, geometry);
        var areaA = a.Length * a.Width;
        var areaB = b.Length * b.Width;
        if (System.Math.Abs(areaA - areaB) > Tie * System.Math.Max(1, System.Math.Max(areaA, areaB)))
            return areaA < areaB;
        if (System.Math.Abs(a.Bottom - b.Bottom) > Tie)
            return a.Bottom < b.Bottom;
        return a.Left < b.Left - Tie;
    }

    private static Box Envelope(IReadOnlyList<NestJobPlacement> poses, Func<string, JobPartGeometry> geometry)
    {
        var left = double.PositiveInfinity;
        var bottom = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var top = double.NegativeInfinity;
        foreach (var pose in poses)
        {
            var shape = (Shape)geometry(pose.PartId).Perimeter.Clone();
            shape.Rotate(pose.Rotation);
            var bounds = shape.BoundingBox;
            left = System.Math.Min(left, bounds.Left + pose.X);
            bottom = System.Math.Min(bottom, bounds.Bottom + pose.Y);
            right = System.Math.Max(right, bounds.Right + pose.X);
            top = System.Math.Max(top, bounds.Top + pose.Y);
        }
        return new Box(left, bottom, right - left, top - bottom);
    }

    private static bool Clears(NestJobPlacement pose, JobPartGeometry geometry,
        NestLayoutCheck.PreparedPlacement frame, NestLayoutCheck.PreparedPlacement[] occupied,
        List<NestJobPlacement> accepted, IReadOnlyDictionary<string, JobPartGeometry> geometries,
        double spacing, CutoutMemo? memo, CancellationToken token)
    {
        foreach (var other in occupied)
        {
            token.ThrowIfCancellationRequested();
            if (!NestLayoutCheck.Clears(other, geometry, pose))
                return false;
        }
        foreach (var other in accepted)
        {
            token.ThrowIfCancellationRequested();
            var otherGeometry = geometries[other.PartId];
            var clears = memo?.Clears(otherGeometry, other, geometry, pose, spacing)
                ?? NestLayoutCheck.Clears(otherGeometry, other, geometry, pose, spacing);
            if (!clears)
                return false;
        }
        token.ThrowIfCancellationRequested();
        return NestLayoutCheck.Clears(frame, geometry, pose);
    }

    /// <summary>The occupant's perimeter at its pose, circumscribed so it never loses material.</summary>
    private static (PathD Outline, double Tolerance) Outline(JobPartGeometry geometry, NestJobPlacement pose)
    {
        var shape = (Shape)geometry.Perimeter.Clone();
        shape.Rotate(pose.Rotation);
        var path = ClipperBridge.ToPath(ClipperBridge.Flatten(shape, CutoutLatticeFill.FlattenTolerance, circumscribe: true),
            new Vector(pose.X, pose.Y));
        return (CutoutLatticeFill.Positive(path), CutoutLatticeFill.FlattenTolerance);
    }

    private static bool WithinRange(Box box) =>
        double.IsFinite(box.Left) && double.IsFinite(box.Right)
        && double.IsFinite(box.Bottom) && double.IsFinite(box.Top)
        && System.Math.Abs(box.Left) <= MaxCoordinate && System.Math.Abs(box.Right) <= MaxCoordinate
        && System.Math.Abs(box.Bottom) <= MaxCoordinate && System.Math.Abs(box.Top) <= MaxCoordinate;
}
