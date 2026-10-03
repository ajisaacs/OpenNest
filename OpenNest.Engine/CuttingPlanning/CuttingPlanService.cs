using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

public static class CuttingPlanService
{
    /// <summary>
    /// Read stable caller-owned sources once, before dispatching worker work. No Program.Clone,
    /// private Plates, settings aliases, quantity updates, or subcall rebinding are involved.
    /// </summary>
    public static CuttingPlanSnapshot Capture(CuttingPlanRequest request, CancellationToken token = default)
    {
        var placements = new List<FixedProgramPlacement>();
        Part source = null;
        int? ordinal = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (request?.Parts == null || request.Parts.Count == 0 || request.ExpansionBudget <= 0)
                throw new ArgumentException("A nonempty source list and positive expansion budget are required.");
            // Reuse the reader's coordinate validation without publishing its native kernel.
            var identities = new HashSet<Part>(ReferenceEqualityComparer.Instance);
            for (var index = 0; index < request.Parts.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                source = request.Parts[index];
                ordinal = index;
                if (source?.BaseDrawing == null || source.Program == null || !double.IsFinite(source.Rotation)
                    || !identities.Add(source))
                    throw new ArgumentException("Missing/duplicate source placement or invalid pose.");
                if (source.BaseDrawing.IsCutOff)
                    throw new NotSupportedException("Cutoff dependency ordering is outside the fixed-program route slice.");
                var clean = ExecutionMotionReader.Read(source.BaseDrawing.Program, Vector.Zero, null, token);
                if (!clean.HasCuttingContour)
                    throw new NotSupportedException("Scribe-only or noncutting source drawings are outside this route slice.");
                var execution = ExecutionMotionReader.Read(source.Program, source.Location, request.StartPoint, token);
                if (!execution.HasCuttingContour)
                    throw new ArgumentException("Placed program has no nonzero cutting contour motions.");
                placements.Add(new(source, index, source.Location, source.Rotation, source.LeadInsLocked, execution));
            }
            // The start can be invalid even when the first rapid has no cutting geometry.
            placements[0].Execution.RapidDistanceFrom(request.StartPoint);
            return new(placements, request.StartPoint, request.ExpansionBudget);
        }
        catch (OperationCanceledException)
        {
            return Failure(CuttingPlanStatus.Cancelled, "Capture cancelled.");
        }
        catch (NotSupportedException exception)
        {
            return Failure(CuttingPlanStatus.UnsupportedGeometry, exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            return Failure(CuttingPlanStatus.InvalidInput, exception.Message);
        }

        CuttingPlanSnapshot Failure(CuttingPlanStatus status, string message) =>
            new(placements, request?.StartPoint ?? Vector.Zero, request?.ExpansionBudget ?? 0, status,
                [new(ordinal, source, null, null, null, message)]);
    }

    public static CuttingPlanResult Plan(CuttingPlanRequest request, CancellationToken token = default) =>
        Plan(Capture(request, token), token);

    /// <summary>Worker-only planning uses owned values; live identities are never dereferenced.</summary>
    public static CuttingPlanResult Plan(CuttingPlanSnapshot snapshot, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return new(CuttingPlanStatus.Cancelled);
        if (snapshot == null)
            return new(CuttingPlanStatus.InvalidInput);
        if (snapshot.Failure is { } failure)
            return new(failure, findings: snapshot.Findings);
        try
        {
            var fixedFindings = new List<CuttingPlanFinding>();
            foreach (var placement in snapshot.Placements)
            {
                token.ThrowIfCancellationRequested();
                // Ignore only the unknown incoming rapid. Every fixed internal motion is checked.
                var findings = new ReleasedContourState().Check(placement.Execution, null,
                    placement.SourceOrdinal + 1, token);
                fixedFindings.AddRange(Map(snapshot, findings));
            }
            if (fixedFindings.Count != 0)
                return new(fixedFindings.Any(f => f.Kind == PostVerificationKind.Incomplete)
                    ? CuttingPlanStatus.UnsupportedGeometry : CuttingPlanStatus.ConstraintConflict,
                    findings: fixedFindings);

            var search = FixedProgramSearch.Run(snapshot, token);
            if (search.Status != CuttingPlanStatus.Ready)
                return new(search.Status, findings: Map(snapshot, search.Findings), expansions: search.Expansions);
            return Replay(snapshot, search.Order, search.Expansions, token);
        }
        catch (OperationCanceledException)
        {
            return new(CuttingPlanStatus.Cancelled);
        }
    }

    // Fresh checker and full sequence replay: no cached branch state is accepted as evidence.
    internal static CuttingPlanResult Replay(CuttingPlanSnapshot snapshot, IReadOnlyList<int> order,
        int expansions, CancellationToken token)
    {
        if (order.Count != snapshot.Placements.Count || order.Distinct().Count() != order.Count
            || order.Any(index => index < 0 || index >= snapshot.Placements.Count))
            return new(CuttingPlanStatus.InvalidInput, expansions: expansions);
        var checker = new ReleasedContourState();
        var position = snapshot.StartPoint;
        var distance = 0.0;
        var findings = new List<PostVerificationFinding>();
        foreach (var index in order)
        {
            token.ThrowIfCancellationRequested();
            var placement = snapshot.Placements[index];
            findings.AddRange(checker.Check(placement.Execution, position, placement.SourceOrdinal + 1, token));
            distance += placement.Execution.RapidDistanceFrom(position);
            position = placement.Execution.DeparturePoint;
        }
        token.ThrowIfCancellationRequested();
        if (findings.Count != 0)
            return new(findings.Any(f => f.Kind == PostVerificationKind.Incomplete)
                ? CuttingPlanStatus.UnsupportedGeometry : CuttingPlanStatus.ConstraintConflict,
                findings: Map(snapshot, findings), expansions: expansions);
        return new(CuttingPlanStatus.Ready, order.Select(index => snapshot.Placements[index]),
            expansions: expansions, rapidDistance: distance, independentlyReplayed: true);
    }

    private static IEnumerable<CuttingPlanFinding> Map(CuttingPlanSnapshot snapshot,
        IEnumerable<PostVerificationFinding> findings) => findings.Select(finding =>
    {
        var source = finding.PartNumber is { } part ? snapshot.Placements[part - 1] : null;
        var other = finding.OtherPartNumber is { } otherPart ? snapshot.Placements[otherPart - 1] : null;
        return new CuttingPlanFinding(source?.SourceOrdinal, source?.SourcePart,
            other?.SourceOrdinal, other?.SourcePart, finding.Kind, finding.Message);
    });
}
