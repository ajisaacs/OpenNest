using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

public static class CuttingPlanService
{
    /// <summary>
    /// Read stable caller-owned sources once into privately owned programs and geometry.
    /// No private Plates, settings aliases, quantity updates or source subcall rebinding.
    /// </summary>
    public static CuttingPlanSnapshot Capture(CuttingPlanRequest request, CancellationToken token = default)
    {
        var placements = new List<FixedProgramPlacement>();
        Part source = null;
        int? ordinal = null;
        try
        {
            token.ThrowIfCancellationRequested();
            PlateCuttingState plateState = null;
            if (request?.Plate != null)
            {
                // Exact freshness record first, on the caller thread; the planned list must be it.
                plateState = PlateCuttingState.Capture(request.Plate, token);
                if (request.Parts == null || !request.Parts.SequenceEqual(plateState.Order, ReferenceEqualityComparer.Instance))
                    throw new ArgumentException("The plate's parts changed after the request was created.");
                if (request.Parts.Count == 0 && request.ExpansionBudget > 0 && request.MaxEntries > 0)
                    return new([], request.StartPoint, request.ExpansionBudget, plateState: plateState);
            }
            if (request?.Parts == null || request.Parts.Count == 0 || request.ExpansionBudget <= 0 || request.MaxEntries <= 0)
                throw new ArgumentException("A nonempty source list and positive expansion budget are required.");
            var eligible = new HashSet<Part>(ReferenceEqualityComparer.Instance);
            if (request.EligibleParts != null)
            {
                if (request.ConfirmedParameters == null)
                    throw new ArgumentException("Eligibility requires confirmed cutting parameters.");
                foreach (var part in request.EligibleParts)
                    if (part == null || !request.Parts.Any(p => ReferenceEquals(p, part)) || !eligible.Add(part))
                        throw new ArgumentException("Foreign or duplicate eligible placement.");
            }
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
                // Validate both original graphs before Clone or any virtual transform can
                // erase unsupported runtime semantics, including fixed/ineligible targets.
                OwnedProgramCopy.Validate(source.BaseDrawing.Program, token);
                OwnedProgramCopy.Validate(source.Program, token);
                var clean = ExecutionMotionReader.ReadSupported(source.BaseDrawing.Program, Vector.Zero, null, token);
                if (!clean.HasCuttingContour)
                    throw new NotSupportedException("Scribe-only or noncutting source drawings are outside this route slice.");
                var execution = ExecutionMotionReader.ReadSupported(source.Program, source.Location, request.StartPoint, token);
                if (!execution.HasCuttingContour)
                    throw new ArgumentException("Placed program has no nonzero cutting contour motions.");
                var ownedProgram = OwnedProgramCopy.Copy(source.Program, token);
                PreparedContours prepared = null;
                LeadMaterialSnapshot material = null;
                if (request.ConfirmedParameters != null)
                {
                    // Geometry-only transform: original graphs were strictly validated and
                    // clone expansion bounded above. Legacy Rotate visits per parent, so
                    // retain Clone's per-parent sharing here instead of restoring diamonds.
                    // Exact placed/proposed payloads still use lossless OwnedProgramCopy.
                    var ownedClean = (Program)source.BaseDrawing.Program.Clone();
                    ownedClean.Rotate(source.Rotation - source.BaseDrawing.Program.Rotation);
                    material = LeadMaterialSnapshot.Capture(ownedClean, source.Location, token);
                    if (!material.IsComplete)
                        throw new NotSupportedException(material.Reason);
                    if (!source.LeadInsLocked && (request.EligibleParts == null || eligible.Contains(source)))
                        prepared = PreparedContours.Capture(ownedClean, request.ConfirmedParameters, token);
                }
                placements.Add(new(source, index, source.Location, source.Rotation, source.LeadInsLocked,
                    execution, ownedProgram, prepared, material));
            }
            // The start can be invalid even when the first rapid has no cutting geometry.
            placements[0].Execution.RapidDistanceFrom(request.StartPoint);
            return new(placements, request.StartPoint, request.ExpansionBudget, regeneration: request.ConfirmedParameters != null,
                preservePartOrder: request.PreservePartOrder, maxEntries: request.MaxEntries,
                expansionObserver: request.ExpansionObserver, plateState: plateState,
                ownedParameters: request.ConfirmedParameters == null ? null
                    : OwnedCuttingParameters.Copy(request.ConfirmedParameters));
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
        var result = PlanCaptured(snapshot, token);
        result.Snapshot = snapshot;
        return result;
    }

    /// <summary>
    /// Installs exactly the replayed proposals of Ready plate-scoped results, all or nothing.
    /// Each plate must still match the state captured with its request; otherwise Stale and
    /// nothing changes. Run on the thread that owns the plates. Never replans.
    /// </summary>
    public static CuttingCommitResult Apply(IEnumerable<CuttingPlanResult> results, CancellationToken token = default) =>
        Apply(results, token, null);

    // beforeInstall is the commit's install-boundary test seam.
    internal static CuttingCommitResult Apply(IEnumerable<CuttingPlanResult> results, CancellationToken token,
        Action<Plate, Part> beforeInstall)
    {
        var plans = new List<PlateCuttingPlan>();
        foreach (var result in results ?? [])
        {
            var snapshot = result?.Snapshot;
            if (result?.Status != CuttingPlanStatus.Ready || !result.IndependentlyReplayed
                || snapshot?.PlateState == null || result.ProposedOrder.Count != snapshot.Placements.Count)
                return new(CuttingCommitStatus.InvalidInput,
                    "Only Ready, independently replayed plate-scoped proposals can be applied.");
            var programs = new List<PlannedPartProgram>();
            foreach (var proposal in result.ProposedOrder)
            {
                if (!proposal.IsRegenerated)
                    continue;
                if (snapshot.OwnedParameters == null)
                    return new(CuttingCommitStatus.InvalidInput, "A regenerated proposal has no captured settings.",
                        snapshot.PlateState.Plate);
                // Fresh owned copies: a result can be applied at most once per captured state,
                // and nothing installed aliases the proposal or another part's settings.
                programs.Add(new(proposal.SourcePart, proposal.CopyProgram(),
                    OwnedCuttingParameters.Copy(snapshot.OwnedParameters)));
            }
            plans.Add(new(snapshot.PlateState, result.ProposedOrder.Select(p => p.SourcePart), programs));
        }
        return CuttingPlanCommit.Apply(plans, token, beforeInstall);
    }

    private static CuttingPlanResult PlanCaptured(CuttingPlanSnapshot snapshot, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return new(CuttingPlanStatus.Cancelled);
        if (snapshot == null)
            return new(CuttingPlanStatus.InvalidInput);
        if (snapshot.Failure is { } failure)
            return new(failure, findings: snapshot.Findings);
        if (snapshot.Placements.Count == 0)
            return snapshot.PlateState == null ? new(CuttingPlanStatus.InvalidInput)
                : new(CuttingPlanStatus.Ready, independentlyReplayed: true); // Empty plate: unchanged no-op.
        try
        {
            var fixedFindings = new List<CuttingPlanFinding>();
            foreach (var placement in snapshot.Placements)
            {
                token.ThrowIfCancellationRequested();
                if (placement.Prepared != null)
                    continue; // An eligible old crossing is precisely what regeneration may repair.
                // Ignore only the unknown incoming rapid. Every fixed internal motion is checked.
                var findings = new ReleasedContourState().Check(placement.Execution, null,
                    placement.SourceOrdinal + 1, token);
                fixedFindings.AddRange(Map(snapshot, findings));
            }
            if (fixedFindings.Count != 0)
                return new(fixedFindings.Any(f => f.Kind == PostVerificationKind.Incomplete)
                    ? CuttingPlanStatus.UnsupportedGeometry : CuttingPlanStatus.ConstraintConflict,
                    findings: fixedFindings);

            if (snapshot.Regeneration)
            {
                var joint = JointCuttingPlanSearch.Run(snapshot, token);
                if (joint.Status != CuttingPlanStatus.Ready)
                    return new(joint.Status, findings: joint.Findings, expansions: joint.Expansions);
                return ReplayPrograms(snapshot, joint.Order, joint.Expansions, token);
            }
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

    // Re-read EXACT selected programs with a fresh checker and native lead validation.
    // No emission/regeneration or cached branch verdict is used here.
    internal static CuttingPlanResult ReplayPrograms(CuttingPlanSnapshot snapshot,
        IReadOnlyList<FixedProgramPlacement> order, int expansions, CancellationToken token)
    {
        if (order == null || order.Count != snapshot.Placements.Count
            || order.Any(p => p == null || p.SourceOrdinal < 0 || p.SourceOrdinal >= snapshot.Placements.Count)
            || order.Select(p => p.SourceOrdinal).Distinct().Count() != order.Count)
            return new(CuttingPlanStatus.InvalidInput, expansions: expansions);
        var checker = new ReleasedContourState();
        var position = snapshot.StartPoint;
        var distance = 0.0;
        var findings = new List<CuttingPlanFinding>();
        var materials = snapshot.Placements.Select(p => p.Material).ToArray();
        foreach (var proposal in order)
        {
            token.ThrowIfCancellationRequested();
            var source = snapshot.Placements[proposal.SourceOrdinal];
            if (!ReferenceEquals(source.SourcePart, proposal.SourcePart)
                || !SameBits(source.Location.X, proposal.Location.X) || !SameBits(source.Location.Y, proposal.Location.Y)
                || !SameBits(source.Rotation, proposal.Rotation) || source.LeadInsLocked != proposal.LeadInsLocked
                || !ReferenceEquals(source.Prepared, proposal.Prepared) || !ReferenceEquals(source.Material, proposal.Material)
                || snapshot.PreservePartOrder && proposal.SourceOrdinal != order.TakeWhile(p => !ReferenceEquals(p, proposal)).Count())
                return new(CuttingPlanStatus.InvalidInput, expansions: expansions);
            if (source.Prepared == null)
            {
                if (!ReferenceEquals(source, proposal))
                    return new(CuttingPlanStatus.InvalidInput, expansions: expansions);
            }
            else if (proposal.SelectedProgram == null
                || !proposal.SelectedProgram.Matches(source.Prepared, proposal.ContourChoices))
                return new(CuttingPlanStatus.InvalidInput, expansions: expansions);
            OwnedExecution execution;
            bool complete;
            try
            {
                execution = ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, position, token);
                complete = ContourProgramVerifier.Verify(execution, source.Material, proposal.SelectedProgram, token);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return new(CuttingPlanStatus.UnsupportedGeometry,
                    findings: [JointCuttingPlanSearch.Finding(source, PostVerificationKind.Incomplete, ex.Message)], expansions: expansions);
            }
            findings.AddRange(JointCuttingPlanSearch.Map(snapshot,
                checker.Check(execution, position, source.SourceOrdinal + 1, token)));
            var lead = LeadPathValidator.Check(execution, source.Material, materials, token);
            if (!lead.IsComplete || !lead.IsClear)
                findings.Add(JointCuttingPlanSearch.Finding(source,
                    lead.IsComplete ? null : PostVerificationKind.Incomplete, lead.Reason));
            if (!complete && findings.Count == 0)
                return new(CuttingPlanStatus.InvalidInput,
                    findings: [JointCuttingPlanSearch.Finding(source, null,
                        "Selected program does not preserve complete directed contours, certified tabs or selected entry/style geometry.")],
                    expansions: expansions);
            distance += execution.RapidDistanceFrom(position);
            position = execution.DeparturePoint;
        }
        token.ThrowIfCancellationRequested();
        if (findings.Count != 0)
            return new(findings.Any(f => f.Kind == PostVerificationKind.Incomplete)
                ? CuttingPlanStatus.UnsupportedGeometry : CuttingPlanStatus.ConstraintConflict,
                findings: findings, expansions: expansions);
        return new(CuttingPlanStatus.Ready, order, expansions: expansions,
            rapidDistance: distance, independentlyReplayed: true);
    }

    private static bool SameBits(double source, double proposed) =>
        BitConverter.DoubleToInt64Bits(source) == BitConverter.DoubleToInt64Bits(proposed);

    private static IEnumerable<CuttingPlanFinding> Map(CuttingPlanSnapshot snapshot,
        IEnumerable<PostVerificationFinding> findings) => findings.Select(finding =>
    {
        var source = finding.PartNumber is { } part ? snapshot.Placements[part - 1] : null;
        var other = finding.OtherPartNumber is { } otherPart ? snapshot.Placements[otherPart - 1] : null;
        return new CuttingPlanFinding(source?.SourceOrdinal, source?.SourcePart,
            other?.SourceOrdinal, other?.SourcePart, finding.Kind, finding.Message);
    });
}
