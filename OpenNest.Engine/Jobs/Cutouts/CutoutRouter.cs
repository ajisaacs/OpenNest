#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>Internal frame-local cutout proposal. Never changes demand, stock or a live plate.</summary>
/// <remarks>Area ratio selects search order, not placement permission. The small-insert path
/// prefers a shifted partial lattice; the middle compares it with occupied-aware NFP search;
/// the large-insert path tries NFP first. All accepted poses are clearance-checked.
/// Fill's equal-score pose nondeterminism remains PM c98c21bd.</remarks>
internal static class CutoutRouter
{
    // Calibrated as search-order hints, not hard geometry cutoffs. Round-hole neutral tests
    // show a Fill win at 0.204, so the original proposed >0.20 NFP-only rule loses copies.
    internal const double SmallRatio = 0.10;
    internal const double LargeRatio = 0.35;

    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing, CancellationToken token = default) =>
        Fill(frame, cutoutIndex, inserts, spacing, null, token);

    /// <summary>Optional internal diagnostic receives only poses from the chosen proposal,
    /// after indexing; true marks a retained lattice seed, false a residual NFP pose.</summary>
    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing,
        Action<NestJobPlacement, bool>? observeAccepted, CancellationToken token = default)
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

        var accepted = new List<NestJobPlacement>();
        var occupied = new List<(JobPartGeometry Geometry, NestJobPlacement Pose)>();
        var holeArea = System.Math.Abs(hole.Area());
        // Preserve caller requirement order: priorities are a whole-job concern, not inferred here.
        foreach (var part in inserts)
        {
            token.ThrowIfCancellationRequested();
            var geometry = JobPartGeometry.Read(part.Geometry);
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
                observeAccepted?.Invoke(indexed, localIndex++ < retainedLattice);
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
        return accepted.ToArray();
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
