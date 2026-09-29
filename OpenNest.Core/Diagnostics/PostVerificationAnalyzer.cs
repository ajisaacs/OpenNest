using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
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

    public static PostVerificationReport Analyze(Nest nest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nest);
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
            var obstacles = new List<Obstacle>();
            Vector? position = Vector.Zero;
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
                        var clean = Read(part?.BaseDrawing?.Program, Vector.Zero, null, cancellationToken);
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
                    var moves = Read(part.Program, part.Location, position, cancellationToken);
                    if (expectsCuts && !moves.Any(move => !move.Rapid
                        && move.Layer is LayerType.Cut or LayerType.Display
                        && move.Curve.Length > PostVerificationGeometry.Epsilon))
                        Incomplete("Placed program has no cutting contour motions for this drawing.");
                    AnalyzeMoves(moves, cutoff, obstacles, findings, plateNumber, partNumber, cancellationToken);
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

    private static List<Move> Read(Program program, Vector origin, Vector? previous,
        CancellationToken token)
    {
        var moves = new List<Move>();
        var visiting = new HashSet<Program>(ReferenceEqualityComparer.Instance);
        var budget = 1000000;
        Walk(program, origin, previous);
        return moves;

        Vector Walk(Program current, Vector frame, Vector? arrival)
        {
            token.ThrowIfCancellationRequested();
            PostVerificationGeometry.Validate(frame);
            if (current?.Codes == null || !visiting.Add(current) || visiting.Count > 64)
                throw new ArgumentException("Missing, recursive or excessively nested program.");
            var pos = frame;
            var first = true;
            var countBefore = moves.Count;
            foreach (var code in current.Codes)
            {
                token.ThrowIfCancellationRequested();
                if (--budget < 0)
                    throw new ArgumentException("Program expansion exceeds the verification limit.");
                if (code == null)
                    throw new ArgumentException("Program contains a missing instruction.");
                if (code is SubProgramCall call)
                {
                    if (!double.IsFinite(call.Rotation))
                        throw new ArgumentException("Subprogram rotation is not finite.");
                    // Call rotation is baked into the shared program by its setter. Do not
                    // rotate again; offsets are frame-relative even in incremental mode.
                    pos = Walk(call.Program, frame + call.Offset, first ? arrival : pos);
                    first = false;
                    continue;
                }
                if (code is not Motion motion)
                {
                    if (code is not (Comment or Feedrate or Kerf))
                        throw new NotSupportedException("Unsupported program instruction.");
                    continue;
                }
                // Posts disagree about incremental position after suppressed instructions.
                // Never silently certify a trajectory whose semantics are ambiguous.
                if (motion.Suppressed)
                    throw new NotSupportedException("Suppressed motion requires post-specific verification.");
                if (motion is not (RapidMove or LinearMove or ArcMove))
                    throw new NotSupportedException("Unsupported motion.");
                var reference = current.Mode == Mode.Incremental ? pos : frame;
                var end = reference + motion.EndPoint;
                PostVerificationGeometry.Validate(end);
                var rapid = motion is RapidMove;
                if (first && !rapid)
                    moves.Add(new(arrival, frame, true, LayerType.Display, null));
                var start = first && rapid ? arrival : pos;
                var layer = motion switch
                {
                    LinearMove line => line.Layer,
                    ArcMove arc => arc.Layer,
                    _ => LayerType.Display
                };
                if (!Enum.IsDefined(layer))
                    throw new NotSupportedException("Unsupported motion layer.");
                if (motion is ArcMove direction && !Enum.IsDefined(direction.Rotation))
                    throw new NotSupportedException("Unsupported arc direction.");
                var curve = rapid ? null : PostVerificationGeometry.Curve.Create(pos, end,
                    motion is ArcMove arcMove ? reference + arcMove.CenterPoint : null,
                    motion is ArcMove { Rotation: RotationType.CW });
                moves.Add(new(start, end, rapid, layer, curve));
                pos = end;
                first = false;
            }
            visiting.Remove(current);
            if (moves.Count == countBefore)
                throw new ArgumentException("Program has no motions.");
            return pos;
        }
    }

    private static void AnalyzeMoves(List<Move> moves, bool cutoff, List<Obstacle> obstacles,
        List<PostVerificationFinding> findings, int plate, int part, CancellationToken token)
    {
        var contour = new List<PostVerificationGeometry.Curve>();
        var unfinished = new List<PostVerificationGeometry.Curve[]>();
        var hasLead = false;
        var contourNumber = 0;
        foreach (var move in moves)
        {
            token.ThrowIfCancellationRequested();
            if (move.Rapid)
            {
                Finish();
                hasLead = false;
                if (move.Start is not { } start || start.DistanceTo(move.End) <= PostVerificationGeometry.Epsilon)
                    continue;
                foreach (var obstacle in obstacles)
                {
                    token.ThrowIfCancellationRequested();
                    if (PostVerificationGeometry.Crosses(start, move.End, obstacle.Curves, token))
                        findings.Add(new(PostVerificationKind.RapidCrossing, plate, part, obstacle.Part,
                            $"Direct XY rapid crosses or touches completed untabbed contour {obstacle.Contour} " +
                            $"of part {obstacle.Part}."));
                }
            }
            else if (move.Layer == LayerType.Leadin)
            {
                Finish();
                hasLead |= move.Curve.Length > PostVerificationGeometry.Epsilon;
            }
            else if (move.Layer is LayerType.Leadout or LayerType.Scribe)
            {
                if (move.Layer == LayerType.Leadout && contour.Count > 0
                    && !PostVerificationGeometry.Closed(contour)
                    && move.Curve.Length > PostVerificationGeometry.Epsilon)
                    findings.Add(new(PostVerificationKind.Incomplete, plate, part, null,
                        "A lead-out follows an open cutting contour and may cut through its retention gap. " +
                        "Rapid safety for that contour requires manual review."));
                Finish();
                hasLead = false;
            }
            else if (move.Curve.Length > PostVerificationGeometry.Epsilon)
            {
                if (contour.Count == 0)
                {
                    contourNumber++;
                    if (!cutoff && !hasLead)
                        findings.Add(new(PostVerificationKind.MissingLeadIn, plate, part, null,
                            $"Cutting contour {contourNumber} has no nonzero placed lead-in motion."));
                    hasLead = false;
                    // A rapid can pause/reposition without leaving any material gap.
                    // Retain already-cut fragments, but do not turn them into obstacles
                    // until an actually continuous chain closes.
                    var previous = unfinished.FindIndex(chain =>
                        chain[^1].End.DistanceTo(move.Curve.Start) <= PostVerificationGeometry.Epsilon);
                    if (previous >= 0)
                    {
                        contour.AddRange(unfinished[previous]);
                        unfinished.RemoveAt(previous);
                    }
                }
                contour.Add(move.Curve);
                // A completed contour becomes an obstacle immediately, not at part end.
                if (PostVerificationGeometry.Closed(contour))
                    Finish();
            }
        }
        Finish();
        if (unfinished.Count > 1)
            findings.Add(new(PostVerificationKind.Incomplete, plate, part, null,
                "Multiple interrupted/open cutting fragments remain. Their combined cuts may release material; " +
                "they cannot be assumed to be retained by tabs. Review rapid travel manually."));

        void Finish()
        {
            if (contour.Count == 0)
                return;
            // A real uncut gap leaves the contour attached. CuttingParameters can be stale;
            // no flag or tab configuration is used as evidence of retention.
            if (!cutoff && PostVerificationGeometry.Closed(contour))
                obstacles.Add(new(part, contourNumber, contour.ToArray()));
            else if (!cutoff)
                unfinished.Add(contour.ToArray());
            contour.Clear();
        }
    }

    private sealed record Move(Vector? Start, Vector End, bool Rapid, LayerType Layer,
        PostVerificationGeometry.Curve Curve);
    private sealed record Obstacle(int Part, int Contour, IReadOnlyList<PostVerificationGeometry.Curve> Curves);
}
