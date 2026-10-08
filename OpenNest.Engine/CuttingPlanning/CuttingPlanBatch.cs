using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>What a batch is doing for one plate, reported from the worker.</summary>
public enum CuttingPlanPhase
{
    /// <summary>Searching for a new whole-part order.</summary>
    Reordering,

    /// <summary>Planning with the plate's current part order.</summary>
    KeepingOrder,

    /// <summary>Checking the plate's clean part material for overlaps.</summary>
    CheckingOverlap,
}

/// <summary>Worker progress: zero-based position in the batch, the plate's display number and phase.</summary>
public sealed record CuttingPlanProgress(int PlateIndex, int PlateCount, int PlateNumber, CuttingPlanPhase Phase);

/// <summary>
/// One desktop planning attempt over one or more plates with caller-confirmed cutting parameters.
/// Capture on the thread that owns the plates, <see cref="Plan"/> on a worker, then apply the
/// returned proposal back on the owner thread. Every plate goes through
/// <see cref="CuttingPlanService"/>; nothing here installs programs or reorders parts itself.
/// </summary>
public sealed class CuttingPlanBatch
{
    /// <summary>The smallest budget a plate gets, the service default.</summary>
    public const int MinimumExpansionBudget = 20000;

    /// <summary>
    /// Expansions allowed per part, whether or not the order is kept: both still choose contour
    /// order and entries for every part. Dense grids measured near 260 per part with a new order
    /// and 210 when kept; the margin keeps 100-150 part plates inside the budget.
    /// </summary>
    public const int ExpansionsPerPart = 400;

    private readonly Entry[] entries;
    private readonly CuttingParameters ownedParameters;

    private CuttingPlanBatch(Entry[] entries, CuttingParameters ownedParameters)
    {
        this.entries = entries;
        this.ownedParameters = ownedParameters;
    }

    public int PlateCount => entries.Length;

    /// <summary>
    /// Captures every plate's exact state with an owned copy of <paramref name="confirmedParameters"/>,
    /// and its clean part material for the overlap check. When the order may change, the
    /// current-order request is captured too, so a free search that runs out of budget can be
    /// retried on the worker without reading live plates again.
    /// </summary>
    public static CuttingPlanBatch Capture(IReadOnlyList<Plate> plates, CuttingParameters confirmedParameters,
        bool preservePartOrder, IReadOnlyList<int> plateNumbers = null, CancellationToken token = default) =>
        Capture(plates, confirmedParameters, preservePartOrder, plateNumbers, null, token);

    /// <param name="reorderBudget">Overrides the new-order budget (tests); null gives <see cref="PlateBudget"/>.</param>
    internal static CuttingPlanBatch Capture(IReadOnlyList<Plate> plates, CuttingParameters confirmedParameters,
        bool preservePartOrder, IReadOnlyList<int> plateNumbers, int? reorderBudget, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(confirmedParameters);
        if (plates.Count == 0)
            throw new ArgumentException("At least one plate is required.", nameof(plates));
        if (plates.Any(plate => plate == null)
            || plates.Distinct(ReferenceEqualityComparer.Instance).Count() != plates.Count)
            throw new ArgumentException("Plates must be distinct and not null.", nameof(plates));
        if (plateNumbers != null && plateNumbers.Count != plates.Count)
            throw new ArgumentException("Give one display number per plate.", nameof(plateNumbers));

        CuttingParameters owned;
        try
        {
            owned = OwnedCuttingParameters.Copy(confirmedParameters);
        }
        catch (NotSupportedException)
        {
            owned = null; // Each plate's capture reports the unsupported settings itself.
        }

        var entries = new Entry[plates.Count];
        for (var index = 0; index < plates.Count; index++)
        {
            var plate = plates[index];
            var budget = PlateBudget(plate.Parts.Count);
            var keepOrder = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate,
                expansionBudget: budget, confirmedParameters: confirmedParameters,
                preservePartOrder: true), token);
            var reorder = preservePartOrder ? null : CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate,
                expansionBudget: reorderBudget ?? budget, confirmedParameters: confirmedParameters), token);
            var overlap = PlateOverlapAnalyzer.Capture(plate.Parts.ToArray(), token);
            var fallback = reorder ?? keepOrder;
            if (fallback.Failure == CuttingPlanStatus.UnsupportedGeometry)
                fallback = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate,
                    confirmedParameters: confirmedParameters, preservePartOrder: preservePartOrder), token, bestEffort: true);
            entries[index] = new(plate, plateNumbers?[index] ?? index + 1, reorder, keepOrder, overlap, fallback);
        }
        return new(entries, owned);
    }

    internal static int PlateBudget(int partCount) =>
        (int)System.Math.Min(int.MaxValue,
            System.Math.Max((long)MinimumExpansionBudget, (long)partCount * ExpansionsPerPart));

    /// <summary>
    /// Checks and plans every plate from its captured snapshots. Safe on a worker: live plates are
    /// not read. Overlapping material blocks a plate whatever its route. A free search that ends
    /// without a complete plan within its budget is retried with the current order; the proposal
    /// reports that it kept the order. Incomplete geometry also gets a separately labelled,
    /// unverified fallback when supported closed contours can still be emitted.
    /// </summary>
    public CuttingPlanProposal Plan(IProgress<CuttingPlanProgress> progress = null,
        CancellationToken token = default)
    {
        var plans = new CuttingPlanPlateResult[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            progress?.Report(new(index, entries.Length, entry.Number, CuttingPlanPhase.CheckingOverlap));
            PlateOverlapReport overlap;
            try
            {
                overlap = PlateOverlapAnalyzer.Analyze(entry.Overlap, token);
            }
            catch (OperationCanceledException)
            {
                plans[index] = new(entry.Plate, entry.Number, new(CuttingPlanStatus.Cancelled), null, null);
                continue;
            }

            CuttingPlanResult reorder = null;
            if (entry.Reorder != null)
            {
                progress?.Report(new(index, entries.Length, entry.Number, CuttingPlanPhase.Reordering));
                reorder = CuttingPlanService.Plan(entry.Reorder, token);
                if (reorder.Status != CuttingPlanStatus.NoSolutionWithinBudget)
                {
                    plans[index] = new(entry.Plate, entry.Number, WithFallback(entry, reorder, token), null, overlap);
                    continue;
                }
            }
            progress?.Report(new(index, entries.Length, entry.Number, CuttingPlanPhase.KeepingOrder));
            plans[index] = new(entry.Plate, entry.Number,
                WithFallback(entry, CuttingPlanService.Plan(entry.KeepOrder, token), token), reorder, overlap);
        }
        return new(plans, ownedParameters);
    }

    private static CuttingPlanResult WithFallback(Entry entry, CuttingPlanResult strict, CancellationToken token)
    {
        if (strict.Status != CuttingPlanStatus.UnsupportedGeometry || entry.Fallback.Failure != null)
            return strict;
        var fallback = BestEffortCuttingPlan.Plan(entry.Fallback, token);
        if (fallback.Status != CuttingPlanStatus.BestEffort)
            return fallback.Status == CuttingPlanStatus.Cancelled ? fallback : strict;
        return new(CuttingPlanStatus.BestEffort, fallback.ProposedOrder,
            strict.Findings.Concat(fallback.Findings).Distinct(), rapidDistance: fallback.RapidDistance)
        { Snapshot = entry.Fallback };
    }

    private sealed record Entry(Plate Plate, int Number, CuttingPlanSnapshot Reorder, CuttingPlanSnapshot KeepOrder,
        PlateOverlapSnapshot Overlap, CuttingPlanSnapshot Fallback);
}

/// <summary>One plate's outcome inside a <see cref="CuttingPlanProposal"/>.</summary>
public sealed class CuttingPlanPlateResult
{
    internal CuttingPlanPlateResult(Plate plate, int plateNumber, CuttingPlanResult result,
        CuttingPlanResult reorderAttempt, PlateOverlapReport overlap)
    {
        Plate = plate;
        PlateNumber = plateNumber;
        Result = result;
        ReorderAttempt = reorderAttempt;
        Overlap = overlap;
    }

    /// <summary>The live plate. Read it only on the thread that owns it.</summary>
    public Plate Plate { get; }
    public int PlateNumber { get; }

    /// <summary>The result that would be applied.</summary>
    public CuttingPlanResult Result { get; }

    /// <summary>The free-order attempt that ran out of budget before the current order was kept; else null.</summary>
    public CuttingPlanResult ReorderAttempt { get; }

    /// <summary>The clean-material overlap check (part ids are current part positions); null if cancelled first.</summary>
    public PlateOverlapReport Overlap { get; }

    public bool KeptCurrentOrder => ReorderAttempt != null;

    /// <summary>The route was replayed and the overlap check completed without overlaps.</summary>
    public bool IsRouteReady => Result.Status == CuttingPlanStatus.Ready && Result.IndependentlyReplayed;

    public bool IsOverlapClear => Overlap is { IsComplete: true } && Overlap.Pairs.Count == 0;
    public bool IsReady => IsRouteReady && IsOverlapClear;

    /// <summary>Owned readable output is available, but not all geometric checks passed.</summary>
    public bool CanApplyWithWarnings => Overlap != null && Overlap.Pairs.Count == 0
        && (IsRouteReady || Result.Status == CuttingPlanStatus.BestEffort);
    public int PartCount => Result.ProposedOrder.Count;
    public int RegeneratedCount => Result.ProposedOrder.Count(p => p.IsRegenerated);

    public bool OrderChanged =>
        Result.ProposedOrder.Select((proposal, index) => proposal.SourceOrdinal != index).Any(changed => changed);
}

/// <summary>
/// The outcome of a batch. Apply is all-or-nothing. Unverified output needs explicit acceptance.
/// </summary>
public sealed class CuttingPlanProposal
{
    private const int FindingsPerPlate = 5;
    private readonly CuttingParameters ownedParameters;

    internal CuttingPlanProposal(IReadOnlyList<CuttingPlanPlateResult> plates, CuttingParameters ownedParameters)
    {
        Plates = plates;
        this.ownedParameters = ownedParameters;
    }

    public IReadOnlyList<CuttingPlanPlateResult> Plates { get; }

    public bool IsCancelled => Plates.Any(p => p.Result.Status == CuttingPlanStatus.Cancelled);

    public bool CanApply => Plates.Count > 0 && !IsCancelled && ownedParameters != null
        && Plates.All(p => p.IsReady);

    /// <summary>All plates have usable output, possibly requiring explicit warning acceptance.</summary>
    public bool CanApplyWithWarnings => Plates.Count > 0 && !IsCancelled && ownedParameters != null
        && Plates.All(p => p.CanApplyWithWarnings);

    public bool RequiresWarningAcceptance => CanApplyWithWarnings && !CanApply;

    /// <summary>
    /// Installs every plate's replayed proposal through <see cref="CuttingPlanService.Apply"/>, on the
    /// thread that owns the plates. Only after every plate is applied does each one keep an owned copy
    /// of the confirmed parameters as its cutting settings; any other status changes nothing.
    /// </summary>
    public CuttingCommitResult Apply(CancellationToken token = default) => Apply(false, token);

    /// <summary>Explicit per-proposal acceptance; never grants consent to post CNC output.</summary>
    public CuttingCommitResult Apply(bool acceptWarnings, CancellationToken token = default)
    {
        if (!(CanApply || acceptWarnings && CanApplyWithWarnings))
            return new(CuttingCommitStatus.InvalidInput,
                "Every plate must have a ready plan before anything is applied.");
        var commit = CuttingPlanService.Apply(Plates.Select(p => p.Result), token, null, acceptWarnings);
        if (commit.Status == CuttingCommitStatus.Applied)
            foreach (var plate in Plates)
                plate.Plate.CuttingParameters = OwnedCuttingParameters.Copy(ownedParameters);
        return commit;
    }

    /// <summary>
    /// A detached copy of a ready or explicitly unverified proposal for display, or null.
    /// Refused graphs are never cloned. Changed plates are not previewed at uncaptured poses.
    /// The copy has quantity zero, so drawing quantities stay put.
    /// Call it on the thread that owns the plates.
    /// </summary>
    public Plate BuildPreview(int index)
    {
        var planned = Plates[index];
        if (!planned.CanApplyWithWarnings || planned.Result.Snapshot?.PlateState?.IsCurrent() != true)
            return null;
        // Unchanged since capture: graphs are supported and the preview uses the exact proposal.
        var source = planned.Plate;
        var preview = new Plate(source.Size)
        {
            Quantity = 0,
            Quadrant = source.Quadrant,
            PartSpacing = source.PartSpacing,
            EdgeSpacing = source.EdgeSpacing,
        };
        foreach (var proposal in planned.Result.ProposedOrder)
        {
            var part = (Part)proposal.SourcePart.Clone();
            part.RestoreLeadInProgram(proposal.CopyProgram(), proposal.LeadInsLocked);
            preview.Parts.Add(part);
        }
        return preview;
    }

    /// <summary>Operator-facing summary: one overall line, then each plate and its findings.</summary>
    public IReadOnlyList<string> Describe(string unit)
    {
        var lines = new List<string>();
        var ready = Plates.Count(p => p.IsReady);
        if (IsCancelled)
            lines.Add("Planning was cancelled. Nothing has changed.");
        else if (CanApply)
            lines.Add($"Ready to apply to {Count(Plates.Count, "plate")}: "
                + $"{Count(Plates.Count(p => p.OrderChanged), "plate")} with a new part order, "
                + $"{Count(Plates.Sum(p => p.RegeneratedCount), "part program")} regenerated.");
        else if (RequiresWarningAcceptance)
            lines.Add("Best-effort plan available. Review the warnings and accept the unverified plan to apply. "
                + "This is not approval to cut or post CNC output.");
        else
            lines.Add($"Apply is unavailable: {Plates.Count - ready} of {Count(Plates.Count, "plate")} could not "
                + "be planned. No plate changes until every plate is ready.");

        foreach (var plate in Plates)
        {
            lines.Add(string.Empty);
            lines.AddRange(DescribePlate(plate, unit));
        }
        return lines;
    }

    private static IEnumerable<string> DescribePlate(CuttingPlanPlateResult plate, string unit)
    {
        var heading = $"Plate {plate.PlateNumber}: ";
        var result = plate.Result;
        if (plate.IsReady && plate.PartCount == 0)
        {
            yield return heading + "no parts.";
            yield break;
        }
        if (plate.IsReady)
        {
            var kept = plate.PartCount - plate.RegeneratedCount;
            var order = plate.KeptCurrentOrder
                ? "No new part order was found within the search limit, so the current order is kept."
                : plate.OrderChanged ? "The part order changes." : "The part order is unchanged.";
            yield return heading + $"ready. {Count(plate.PartCount, "part")}, {plate.RegeneratedCount} "
                + $"regenerated, {kept} kept as is; rapid travel "
                + $"{result.RapidDistance.ToString("0.##", CultureInfo.CurrentCulture)} {unit}. {order}";
            yield break;
        }

        if (plate.CanApplyWithWarnings)
        {
            yield return heading + $"best-effort, unverified. {Count(plate.PartCount, "part")}, "
                + $"{plate.RegeneratedCount} regenerated; review lead-ins and cutting order.";
            foreach (var line in Limit(DescribeOverlap(plate.Overlap)))
                yield return line;
            foreach (var line in Limit(result.Findings.Select(DescribeFinding)))
                yield return line;
            yield break;
        }

        var missingLead = !plate.IsRouteReady && result.Status != CuttingPlanStatus.Cancelled
            && result.Findings.Any(f => f.Kind == PostVerificationKind.MissingLeadIn);
        yield return heading + (plate.IsRouteReady
            ? "blocked: parts overlap or could not be checked for overlap."
            : missingLead ? "blocked: missing or zero-length lead-in." : StatusText(result.Status));
        foreach (var line in Limit(DescribeOverlap(plate.Overlap)))
            yield return line;
        if (plate.IsRouteReady)
            yield break;
        if (missingLead)
        {
            yield return "  Open Cutting Settings... and select a lead-in type other than None with "
                + "nonzero length for the affected contour (External, Internal, or Arc / Circle), then replan.";
            yield return "  If the affected part is locked, edit its lead-ins or unlock it before replanning.";
        }
        else if (plate.KeptCurrentOrder)
            yield return "  No new part order was found within the search limit, and planning with the "
                + "current order was refused:";
        foreach (var line in Limit(result.Findings.Select(DescribeFinding)))
            yield return line;
    }

    private static IEnumerable<string> Limit(IEnumerable<string> lines)
    {
        var all = lines.ToList();
        foreach (var line in all.Take(FindingsPerPlate))
            yield return "  - " + line;
        if (all.Count > FindingsPerPlate)
            yield return $"  ... and {all.Count - FindingsPerPlate} more.";
    }

    private static IEnumerable<string> DescribeOverlap(PlateOverlapReport overlap)
    {
        if (overlap == null)
            yield break;
        foreach (var pair in overlap.Pairs)
            yield return $"Part {pair.PartAId + 1}{Name(pair.PartAName)} overlaps part "
                + $"{pair.PartBId + 1}{Name(pair.PartBName)}.";
        foreach (var issue in overlap.Issues)
            yield return $"Overlap check incomplete for part {issue.PartAId + 1}"
                + (issue.PartBId is int other ? $" and part {other + 1}" : string.Empty) + $": {issue.Message}";
    }

    private static string StatusText(CuttingPlanStatus status) => status switch
    {
        CuttingPlanStatus.Ready => "ready, but the plan was not independently replayed.",
        CuttingPlanStatus.ConstraintConflict => "blocked: the fixed programs or required cut order conflict.",
        CuttingPlanStatus.UnsupportedGeometry => "blocked: unsupported geometry or an incomplete check.",
        CuttingPlanStatus.InvalidInput => "blocked: invalid input.",
        CuttingPlanStatus.NoSolutionWithinBudget => "no complete plan was found within the search limit.",
        CuttingPlanStatus.Cancelled => "cancelled.",
        _ => status.ToString(),
    };

    private static string DescribeFinding(CuttingPlanFinding finding)
    {
        var text = finding.SourceOrdinal is int ordinal
            ? $"Part {ordinal + 1}{Name(finding.SourcePart)}: {finding.Message}"
            : finding.Message;
        if (finding.OtherSourceOrdinal is int other)
            text += $" (with part {other + 1}{Name(finding.OtherSourcePart)})";
        return text;
    }

    private static string Name(Part part) => Name(part?.BaseDrawing?.Name);

    private static string Name(string name) => string.IsNullOrWhiteSpace(name) ? string.Empty : $" ({name})";

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}
