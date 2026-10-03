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
            var overlap = PlateOverlapAnalyzer.Analyze(
                PlateOverlapAnalyzer.Capture(materialParts, cancellationToken), cancellationToken);
            foreach (var pair in overlap.Pairs)
                findings.Add(new(PostVerificationKind.Overlap, plateNumber, indices[pair.PartAId],
                    indices[pair.PartBId], "Clean drawing material overlaps (holes subtracted)."));
            foreach (var issue in overlap.Issues)
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, indices[issue.PartAId],
                    issue.PartBId is { } other ? indices[other] : null, "Overlap check: " + issue.Message));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PostVerificationReport(findings);
    }

    private static bool IsInvalid(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;

}
