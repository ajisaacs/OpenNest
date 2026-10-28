#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>Internal frame-local cutout proposal. Never changes demand, stock or a live plate.</summary>
/// <remarks>Area ratio selects search order, not placement permission. The small-insert path
/// prefers a shifted partial lattice; the middle compares it with occupied-aware NFP search;
/// the large-insert path tries NFP first. A round hole whose inserts are all round gets only the
/// circle-in-circle proposal for the whole list. Every other hole is also packed by
/// <see cref="CutoutPackerProposal"/>, which wins with more copies or an equal count packed tighter.
/// All accepted poses are clearance-checked.
/// Fill's equal-score pose nondeterminism remains PM c98c21bd.</remarks>
internal static class CutoutRouter
{
    // Calibrated as search-order hints, not hard geometry cutoffs. Round-hole neutral tests
    // show a Fill win at 0.204, so the original proposed >0.20 NFP-only rule loses copies.
    internal const double SmallRatio = 0.10;
    internal const double LargeRatio = 0.35;

    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing, CancellationToken token = default) =>
        Fill(frame, cutoutIndex, inserts, spacing, null, null, token);

    /// <summary>Same proposal; a pre-pass passes its <paramref name="memo"/> so circle-packed
    /// holes replay geometry reads and clearance checks it has already made.</summary>
    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing, CutoutMemo memo,
        CancellationToken token = default) =>
        Fill(frame, cutoutIndex, inserts, spacing, null, memo, token);

    /// <summary>Optional internal diagnostic receives only poses from the chosen proposal,
    /// after indexing; true marks a retained lattice seed, false a residual NFP, circle-packer or
    /// frontier-packer pose.</summary>
    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing,
        Action<NestJobPlacement, bool>? observeAccepted, CancellationToken token = default) =>
        Fill(frame, cutoutIndex, inserts, spacing, observeAccepted, null, token);

    private static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing,
        Action<NestJobPlacement, bool>? observeAccepted, CutoutMemo? memo, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(inserts);
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(spacing) || spacing < 0 || spacing > 100)
            return Array.Empty<NestJobPlacement>();
        var frameGeometry = JobPartGeometry.TryRead(frame.Geometry);
        if (frameGeometry == null)
            return Array.Empty<NestJobPlacement>();
        ArgumentOutOfRangeException.ThrowIfNegative(cutoutIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cutoutIndex, frameGeometry.Cutouts.Count);
        var hole = frameGeometry.Cutouts[cutoutIndex];
        if (!hole.IsClosed() || !Supported(frameGeometry.Bounds) || !Supported(hole.BoundingBox))
            return Array.Empty<NestJobPlacement>();
        if (inserts.Any(p => p == null || !SupportedGeometry(p)))
            return Array.Empty<NestJobPlacement>();
        if (inserts.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != inserts.Count
            || inserts.Any(p => p.Id == frame.Id))
            throw new ArgumentException("Requirement IDs must be distinct from the frame and each other.",
                nameof(inserts));

        // A round hole holding only round inserts is a circle packing problem: the lattice and
        // NFP searches never placed more there, and they are slow on round inserts, most of all
        // when demand exceeds what fits (every surplus copy is searched for and not found).
        if (CutoutCirclePacker.Applies(hole, inserts))
            return Observe(CutoutCirclePacker.Fill(frame, cutoutIndex, inserts, spacing, memo, token));

        var accepted = new List<NestJobPlacement>();
        var geometries = new Dictionary<string, JobPartGeometry>(StringComparer.Ordinal);
        var observed = new List<(NestJobPlacement Pose, bool Lattice)>();
        var occupied = new List<(JobPartGeometry Geometry, NestJobPlacement Pose)>();
        var holeArea = System.Math.Abs(hole.Area());
        // Preserve caller requirement order: priorities are a whole-job concern, not inferred here.
        foreach (var part in inserts)
        {
            token.ThrowIfCancellationRequested();
            var geometry = JobPartGeometry.Read(part.Geometry);
            geometries[part.Id] = geometry;
            var ratio = geometry.MaterialArea / holeArea;
            // Unsupported or degenerate material cannot establish a useful routing ratio;
            // NFP retains its own fail-closed checks and the bounded lattice is not tried.
            var tryLattice = part.Quantity >= 3 && double.IsFinite(ratio) && ratio > 0;
            var nfpFirst = !tryLattice || ratio >= LargeRatio;
            var compareBoth = tryLattice && ratio >= SmallRatio && ratio < LargeRatio;
            var nfp = nfpFirst ? Nfp(Array.Empty<NestJobPlacement>()) : null;
            var lattice = tryLattice && (!nfpFirst || nfp!.Count < part.Quantity)
                ? CutoutLatticeFill.Fill(frame, cutoutIndex, part, part.Quantity, spacing,
                    CutoutLatticeFill.DefaultShiftSteps, occupied, token)
                : Array.Empty<NestJobPlacement>();
            IReadOnlyList<NestJobPlacement> chosen;
            var retainedLattice = 0;
            if (compareBoth || (nfpFirst && lattice.Count > 0))
            {
                nfp ??= Nfp(Array.Empty<NestJobPlacement>());
                var fromLattice = Complete(lattice);
                chosen = fromLattice.Count > nfp.Count ? fromLattice : nfp;
                if (ReferenceEquals(chosen, fromLattice))
                    retainedLattice = lattice.Count;
            }
            else if (nfpFirst)
                chosen = nfp!;
            else
            {
                // A single Fill copy ties one NFP pose; prefer NFP's stable candidate
                // order, but retain the lattice copy if bounded NFP found nothing.
                var seed = lattice.Count > 1 ? lattice : Array.Empty<NestJobPlacement>();
                chosen = Complete(seed);
                if (chosen.Count == 0 && lattice.Count == 1)
                    chosen = lattice;
                retainedLattice = ReferenceEquals(chosen, lattice) ? lattice.Count : seed.Count;
            }

            var localIndex = 0;
            foreach (var pose in chosen)
            {
                token.ThrowIfCancellationRequested();
                var indexed = pose with { InstanceIndex = accepted.Count(p => p.PartId == part.Id) };
                accepted.Add(indexed);
                occupied.Add((geometry, indexed));
                observed.Add((indexed, localIndex++ < retainedLattice));
            }

            IReadOnlyList<NestJobPlacement> Complete(IReadOnlyList<NestJobPlacement> seed)
            {
                if (seed.Count >= part.Quantity)
                    return seed;
                var result = new List<NestJobPlacement>(seed);
                result.AddRange(Nfp(seed));
                return result;
            }

            IReadOnlyList<NestJobPlacement> Nfp(IReadOnlyList<NestJobPlacement> seed)
            {
                var result = new List<NestJobPlacement>();
                var local = new List<(JobPartGeometry Geometry, NestJobPlacement Pose)>(occupied);
                local.AddRange(seed.Select(p => (geometry, p)));
                while (seed.Count + result.Count < part.Quantity)
                {
                    token.ThrowIfCancellationRequested();
                    var pose = CutoutNfpProposal.Find(frame, cutoutIndex, part, spacing, local, token);
                    if (pose == null)
                        break;
                    result.Add(pose);
                    local.Add((geometry, pose));
                }
                return result;
            }
        }
        token.ThrowIfCancellationRequested();
        // The frontier packer sees every requirement at once and settles parts against the
        // walls and each other; it replaces the sampled proposal when it seats more copies, or
        // the same number more compactly.
        var packed = CutoutPackerProposal.Fill(frame, cutoutIndex, inserts, spacing,
            Array.Empty<(JobPartGeometry Geometry, NestJobPlacement Pose)>(), memo, token);
        if (packed.Count > accepted.Count
            || (packed.Count == accepted.Count
                && CutoutPackerProposal.Tighter(packed, accepted, id => geometries[id])))
            return Observe(packed);
        foreach (var (pose, lattice) in observed)
            observeAccepted?.Invoke(pose, lattice);
        return accepted.ToArray();

        IReadOnlyList<NestJobPlacement> Observe(IReadOnlyList<NestJobPlacement> poses)
        {
            foreach (var pose in poses)
                observeAccepted?.Invoke(pose, false);
            return poses;
        }
    }

    private static bool SupportedGeometry(NestJobPart part)
    {
        var geometry = JobPartGeometry.TryRead(part.Geometry);
        return geometry != null && Supported(geometry.Bounds);
    }

    private static bool Supported(OpenNest.Geometry.Box box) =>
        double.IsFinite(box.Left) && double.IsFinite(box.Right)
        && double.IsFinite(box.Bottom) && double.IsFinite(box.Top)
        && System.Math.Abs(box.Left) <= 1e8 && System.Math.Abs(box.Right) <= 1e8
        && System.Math.Abs(box.Bottom) <= 1e8 && System.Math.Abs(box.Top) <= 1e8;
}
