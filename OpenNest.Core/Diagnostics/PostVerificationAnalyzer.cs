using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>
/// Read-only pre-post diagnostics. The caller must keep the nest stable for the entire call.
/// Placed programs are already rotated; only their placement translation is applied here.
/// This intentionally does not invoke a post or promise machine collision avoidance.
/// </summary>
public static class PostVerificationAnalyzer
{
    public static PostVerificationReport AnalyzeForPost(Nest nest, IPostProcessor postProcessor,
        CancellationToken cancellationToken = default)
    {
        var report = Analyze(nest, cancellationToken);
        if (postProcessor is IPostVerificationSupport { PreservesPlacedProgramOrder: true })
            return report;
        var findings = report.Findings.ToList();
        findings.Add(new(PostVerificationKind.Incomplete, 0, null, null,
            $"Post '{postProcessor?.Name ?? "unknown"}' does not declare that it preserves placed part/contour order " +
            "and pierce positions. Nest-level checks ran, but the final rapid sequence requires manual review."));
        return new PostVerificationReport(findings);
    }

    public static PostVerificationReport Analyze(Nest nest, CancellationToken cancellationToken = default) =>
        Analyze(nest, Vector.Zero, cancellationToken);

    public static PostVerificationReport Analyze(Nest nest, Vector startPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nest);
        PostVerificationGeometry.Validate(startPoint);
        cancellationToken.ThrowIfCancellationRequested();
        var findings = new List<PostVerificationFinding>();
        if (nest.Plates == null)
        {
            findings.Add(new(PostVerificationKind.Incomplete, 1, null, null, "Nest has no plate collection."));
            return new PostVerificationReport(findings);
        }
        for (var plateIndex = 0; plateIndex < nest.Plates.Count; plateIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plate = nest.Plates[plateIndex];
            var plateNumber = plateIndex + 1;
            if (plate?.Parts == null)
            {
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, null, null,
                    "Plate has no part collection."));
                continue;
            }
            var materialParts = new List<Part>();
            var indices = new List<int>();
            // Stored zones use the sheet's lower-left frame; motions and parts are signed.
            // Validate them before any early exit: zone bounds are publicly mutable.
            IReadOnlyList<ClampZone> clamps;
            try
            {
                clamps = ClampZoneFrame.ToPlacement(plate);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                clamps = null;
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, null, null,
                    "Clamp check: " + exception.Message));
            }
            // One clamp warning per cut-off part and clamp: the placed-program check below
            // reports only what the nominal definition check has not already reported.
            var cutOffClampContacts = new HashSet<(int PartNumber, string ClampId)>();
            if (clamps != null)
            {
                try
                {
                    foreach (var contact in ClampCutDiagnostics.Find(plate))
                    {
                        var cut = plate.CutOffs[contact.CutOffIndex];
                        var partIndex = -1;
                        for (var partSlot = 0; partSlot < plate.Parts.Count; partSlot++)
                            if (ReferenceEquals(plate.Parts[partSlot]?.BaseDrawing, cut.Drawing))
                            {
                                partIndex = partSlot;
                                break;
                            }
                        if (partIndex >= 0)
                            cutOffClampContacts.Add((partIndex + 1, contact.ClampId));
                        findings.Add(new(PostVerificationKind.Overlap, plateNumber,
                            partIndex < 0 ? null : partIndex + 1, null,
                            $"Manual cut-off {contact.CutOffIndex + 1} crosses active clamp '{contact.ClampId}'. "
                            + "Review the clamp and cut before posting; this warning is not a repair.")
                        { Location = contact.Location, ClampId = contact.ClampId });
                    }
                }
                catch (Exception exception) when (IsInvalid(exception))
                {
                    findings.Add(new(PostVerificationKind.Incomplete, plateNumber, null, null,
                        "Clamp/cut-off check: " + exception.Message));
                }
            }
            var obstacles = new ReleasedContourState();
            Vector? position = startPoint;
            for (var index = 0; index < plate.Parts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var part = plate.Parts[index];
                var partNumber = index + 1;
                var cutoff = part?.BaseDrawing?.IsCutOff == true;
                var expectsCuts = cutoff;
                // Preflight even clean programs before calling the overlap converter: malformed
                // recursive graphs must never reach Program.Clone or unguarded conversion.
                if (!cutoff)
                {
                    try
                    {
                        var clean = ExecutionMotionReader.Read(part?.BaseDrawing?.Program, Vector.Zero, null, cancellationToken).Motions;
                        expectsCuts = clean.Any(move => !move.Rapid && move.Layer != LayerType.Scribe);
                        if (!clean.Any(move => move.Layer == LayerType.Scribe)
                            || expectsCuts)
                        {
                            materialParts.Add(part);
                            indices.Add(partNumber);
                        }
                    }
                    catch (Exception exception) when (IsInvalid(exception))
                    {
                        Incomplete("Overlap check: " + exception.Message);
                    }
                }
                try
                {
                    if (part == null || !double.IsFinite(part.Rotation))
                        throw new ArgumentException("Missing part or invalid rotation.");
                    var moves = ExecutionMotionReader.Read(part.Program, part.Location, position, cancellationToken).Motions;
                    if (expectsCuts && !moves.Any(move => !move.Rapid
                        && move.Layer is LayerType.Cut or LayerType.Display
                        && move.Curve.Length > PostVerificationGeometry.Epsilon))
                        Incomplete("Placed program has no cutting contour motions for this drawing.");
                    obstacles.AnalyzeMoves(moves, cutoff, findings, plateNumber, partNumber, cancellationToken);
                    if (clamps is { Count: > 0 })
                        foreach (var move in moves)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!move.Rapid || move.Start is not { } from
                                || from.X == move.End.X && from.Y == move.End.Y)
                                continue;
                            foreach (var zone in clamps)
                                if (ClampCutDiagnostics.Intersects(from, move.End, zone.Bounds, out var contact))
                                    findings.Add(new(PostVerificationKind.RapidCrossing, plateNumber,
                                        partNumber, null,
                                        $"Direct XY rapid crosses active clamp '{zone.Id}'. "
                                        + "Head height is not modeled; review the posted route and clamp setup.")
                                    {
                                        Location = (from + move.End) * 0.5,
                                        RapidStart = from,
                                        RapidEnd = move.End,
                                        ContactPoints = new[] { contact },
                                        ClampId = zone.Id
                                    });
                        }
                    // The placed program is what posts: a definition edited without
                    // regenerating it can still cut through a clamp its nominal line misses.
                    if (cutoff && clamps is { Count: > 0 })
                        foreach (var move in moves)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (move.Rapid || move.Start is not { } from)
                                continue;
                            foreach (var zone in clamps)
                                if (!cutOffClampContacts.Contains((partNumber, zone.Id))
                                    && CutTouches(move, from, zone.Bounds, out var contact))
                                {
                                    cutOffClampContacts.Add((partNumber, zone.Id));
                                    findings.Add(new(PostVerificationKind.Overlap, plateNumber, partNumber, null,
                                        $"The placed cut-off program crosses active clamp '{zone.Id}'. "
                                        + "Regenerate or move the cut-off before posting; this warning is not a repair.")
                                    { Location = contact, ClampId = zone.Id });
                                }
                        }
                    position = moves[^1].End;
                }
                catch (Exception exception) when (IsInvalid(exception))
                {
                    Incomplete("Lead-in/rapid check: " + exception.Message);
                    // Subsequent internal moves can still be checked, but the incoming segment
                    // cannot be reconstructed after an invalid program.
                    position = null;
                }

                void Incomplete(string message) => findings.Add(new(PostVerificationKind.Incomplete,
                    plateNumber, partNumber, null, message));
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Leads Plan Cutting accepted as given (locked programs) are checked again here.
            FixedLeadReview.Add(plate, plateNumber, findings, cancellationToken);
            var overlap = PlateOverlapAnalyzer.Analyze(
                PlateOverlapAnalyzer.Capture(materialParts, (clamps ?? Array.Empty<ClampZone>()).ToArray(),
                    cancellationToken),
                cancellationToken);
            foreach (var pair in overlap.Pairs)
                findings.Add(new(PostVerificationKind.Overlap, plateNumber, indices[pair.PartAId],
                    indices[pair.PartBId], "Clean drawing material overlaps (holes subtracted).")
                { Location = pair.Marker, Overlap = pair });
            foreach (var pair in overlap.ClampFindings)
                findings.Add(new(PostVerificationKind.Overlap, plateNumber, indices[pair.PartAId],
                    null, $"Clean drawing material overlaps {pair.PartBName} (holes subtracted).")
                { Location = pair.Marker, Overlap = pair });
            foreach (var issue in overlap.Issues)
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, indices[issue.PartAId],
                    issue.PartBId is { } other ? indices[other] : null, "Overlap check: " + issue.Message));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PostVerificationReport(findings);
    }

    // Lines are tested exactly; an arc conservatively by its whole supporting extent.
    private static bool CutTouches(ExecutionMotion move, Vector from, Box zone, out Vector contact)
    {
        if (move.Curve == null || move.Curve.IsLine)
            return ClampCutDiagnostics.Intersects(from, move.End, zone, out contact);
        var extent = move.Curve.Extent;
        contact = new Vector(
            System.Math.Clamp(move.Curve.Midpoint.X, zone.Left, zone.Right),
            System.Math.Clamp(move.Curve.Midpoint.Y, zone.Bottom, zone.Top));
        return !(extent.MinX > zone.Right || extent.MaxX < zone.Left
            || extent.MinY > zone.Top || extent.MaxY < zone.Bottom);
    }

    private static bool IsInvalid(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;

}
