#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>Internal frame-local cutout proposal. Never changes demand, stock or a live plate.</summary>
/// <remarks>Tries shifted partial Fill for a type with at least three requested copies,
/// prefers it only when it beats a single NFP pose, then fills residual demand
/// with NFP poses. No tuned ratio cutoff is claimed: the
/// historical 0.10/0.20 routing thresholds still require measured acceptance.
/// Fill's equal-score pose nondeterminism is tracked as PM c98c21bd.</remarks>
internal static class CutoutRouter
{
    internal static IReadOnlyList<NestJobPlacement> Fill(NestJobPart frame, int cutoutIndex,
        IReadOnlyList<NestJobPart> inserts, double spacing, CancellationToken token = default)
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
        if (!hole.IsClosed() || !Supported(frameGeometry.Bounds)
            || !Supported(hole.BoundingBox))
            return Array.Empty<NestJobPlacement>();
        if (inserts.Any(p => p == null || !SupportedGeometry(p)))
            return Array.Empty<NestJobPlacement>();
        if (inserts.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != inserts.Count
            || inserts.Any(p => p.Id == frame.Id))
            throw new ArgumentException("Requirement IDs must be distinct from the frame and each other.",
                nameof(inserts));

        var accepted = new List<NestJobPlacement>();
        var occupied = new List<(JobPartGeometry Geometry, NestJobPlacement Pose)>();
        // Preserve caller requirement order: priorities are a whole-job concern, not inferred here.
        foreach (var part in inserts)
        {
            token.ThrowIfCancellationRequested();
            var geometry = JobPartGeometry.Read(part.Geometry);
            if (part.Quantity >= 3)
            {
                // Geometric work is bounded inside Fill. Do not infer a ratio threshold
                // from material area before measuring cutout jobs; try both proposals.
                var usable = CutoutLatticeFill.Fill(frame, cutoutIndex, part, part.Quantity,
                    spacing, CutoutLatticeFill.DefaultShiftSteps, occupied, token);
                // A one-copy lattice adds no value over the geometry-aware NFP search;
                // retain the latter's stable candidate order for that tie.
                if (usable.Count > 1 || (usable.Count == 1
                    && CutoutNfpProposal.Find(frame, cutoutIndex, part, spacing, occupied, token) == null))
                    foreach (var pose in usable)
                    {
                        token.ThrowIfCancellationRequested();
                        Accept(pose);
                    }
            }
            while (accepted.Count(p => p.PartId == part.Id) < part.Quantity)
            {
                token.ThrowIfCancellationRequested();
                var pose = CutoutNfpProposal.Find(frame, cutoutIndex, part, spacing, occupied, token);
                if (pose == null)
                    break;
                Accept(pose with { InstanceIndex = accepted.Count(p => p.PartId == part.Id) });
            }

            void Accept(NestJobPlacement pose)
            {
                var indexed = pose with { InstanceIndex = accepted.Count(p => p.PartId == part.Id) };
                accepted.Add(indexed);
                occupied.Add((geometry, indexed));
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
