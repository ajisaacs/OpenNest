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
/// first sampled point that clears. Each of the engine's strategy variants packs the hole, then
/// largest-first candidates give big parts the first move (see <see cref="LargestFirst"/>); the
/// candidate seating the most area wins, ties going to the tighter layout. Every pose is
/// certified with <see cref="NestLayoutCheck.Clears"/> against the frame, the occupants and the
/// other accepted poses, exactly as the lattice and NFP proposals are. Never changes a job.
/// </summary>
internal static class CutoutPackerProposal
{
    private const double MaxSpacing = 100;
    private const double Tie = 1e-6;

    /// <summary>Requirements, largest first, that each open a largest-first candidate.</summary>
    private const int MaxHeads = 12;

    /// <summary>Copy counts tried for a head: as many as fit, then fewer, leaving room for
    /// another part beside them.</summary>
    private const int MaxHeadCopies = 4;

    /// <summary>Region updates the largest-first candidates of one hole may spend.</summary>
    internal const long LargestFirstWork = 60_000;

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
        if (frameGeometry == null || !CutoutCoordinates.WithinRange(frameGeometry.Bounds))
            return Array.Empty<NestJobPlacement>();
        ArgumentOutOfRangeException.ThrowIfNegative(cutoutIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cutoutIndex, frameGeometry.Cutouts.Count);
        var cutout = frameGeometry.Cutouts[cutoutIndex];
        if (!cutout.IsClosed() || !CutoutCoordinates.WithinRange(cutout.BoundingBox)
            || spacing * 2 >= System.Math.Min(cutout.BoundingBox.Length, cutout.BoundingBox.Width))
            return Array.Empty<NestJobPlacement>();
        var geometries = new Dictionary<string, JobPartGeometry>(StringComparer.Ordinal);
        foreach (var insert in inserts)
        {
            var geometry = memo?.Read(insert.Geometry) ?? JobPartGeometry.TryRead(insert.Geometry);
            if (geometry == null || !CutoutCoordinates.WithinRange(geometry.Bounds))
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
            var poses = new List<NestJobPlacement>();
            Seat(packer, demand, new List<Placed>(), poses);
            Offer(poses);
        }
        LargestFirst(types, demand, System.Math.Abs(cutout.Area()), counter, Seat, Offer,
            (axis, beta) => new FrontierPacker(types, nfps, pairs, stock, axis, beta, counter, boundary: boundary),
            token);
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

        // Packs the offered demand around what is seated; only certified poses join it.
        void Seat(FrontierPacker packer, IReadOnlyList<int> offered, List<Placed> seated, List<NestJobPlacement> poses)
        {
            var fill = packer.Fill(offered, token, seated);
            foreach (var placed in fill.Parts)
            {
                token.ThrowIfCancellationRequested();
                var part = types[placed.Orientation.TypeIndex].Part;
                var pose = new NestJobPlacement(part.Id, 0, System.Math.Round(placed.X, 8),
                    System.Math.Round(placed.Y, 8), placed.Orientation.Rotation);
                if (!Clears(pose, geometries[part.Id], preparedFrame, preparedOccupied, poses, geometries, spacing, memo, token))
                    continue;
                poses.Add(pose);
                seated.Add(placed);
            }
        }

        void Offer(List<NestJobPlacement> poses)
        {
            if (poses.Count > 0 && (best == null || CutoutSeatedArea.Better(poses, best, id => geometries[id])))
                best = poses;
        }
    }

    /// <summary>The frontier rule's first move in an empty hole is the least advance per area, so
    /// narrow small parts go first and a big part may never find its room. These candidates let
    /// each of the largest requirements open the hole, then offer the other requirements one at a
    /// time, largest first, around what is seated. Bounded by counted region updates; the
    /// order is fixed, so the outcome does not depend on the machine.</summary>
    private static void LargestFirst(IReadOnlyList<OpenNest.Engine.NestingEngines.Irregular.PartType> types, IReadOnlyList<int> demand, double holeArea,
        WorkCounter counter, Action<FrontierPacker, IReadOnlyList<int>, List<Placed>, List<NestJobPlacement>> seat,
        Action<List<NestJobPlacement>> offer, Func<PackAxis, double, FrontierPacker> packerFor, CancellationToken token)
    {
        var order = types.Where(t => t.Orientations.Count > 0 && demand[t.Index] > 0)
            .OrderByDescending(t => t.Area).ThenBy(t => t.Index).ToArray();
        // One requirement alone has no order to try: the variants already pack it.
        if (order.Length < 2)
            return;
        var limit = counter.Value + LargestFirstWork;
        var packers = new[] { PackAxis.X, PackAxis.Y }.Select(axis => packerFor(axis, 1.0)).ToArray();
        foreach (var head in order.Take(MaxHeads))
            foreach (var packer in packers)
            {
                var copies = demand[head.Index];
                for (var trial = 0; trial < MaxHeadCopies && copies > 0; trial++, copies--)
                {
                    token.ThrowIfCancellationRequested();
                    if (counter.Value >= limit)
                        return;
                    var seatedHead = Cascade(packer, head, copies);
                    if (seatedHead == 0)
                        break;
                    copies = System.Math.Min(copies, seatedHead);
                }
            }

        int Cascade(FrontierPacker packer, OpenNest.Engine.NestingEngines.Irregular.PartType head, int headCopies)
        {
            var left = demand.ToArray();
            var seated = new List<Placed>();
            var poses = new List<NestJobPlacement>();
            var free = holeArea;
            Stage(head, headCopies);
            var seatedHead = poses.Count;
            if (seatedHead == 0)
                return 0;
            foreach (var type in order)
                Stage(type, left[type.Index]);
            offer(poses);
            return seatedHead;

            void Stage(OpenNest.Engine.NestingEngines.Irregular.PartType type, int copies)
            {
                if (copies <= 0 || type.Area > free)
                    return;
                var offered = new int[demand.Count];
                offered[type.Index] = copies;
                var before = seated.Count;
                seat(packer, offered, seated, poses);
                var added = seated.Count - before;
                left[type.Index] -= added;
                free -= added * type.Area;
            }
        }
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
}
