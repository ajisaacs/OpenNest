using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>What a batch is doing for one plate, reported from the worker.</summary>
public enum CuttingPlanPhase
{
    /// <summary>Searching for a new whole-part order.</summary>
    Reordering,

    /// <summary>Planning with the plate's current part order.</summary>
    KeepingOrder,
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
    /// <summary>Budget of a free-order search, the service default.</summary>
    public const int ReorderExpansionBudget = 20000;

    /// <summary>
    /// Expansions allowed per part when the order is kept. Measured near 210 per part on a dense
    /// grid; the margin keeps 100-150 part plates inside the budget.
    /// </summary>
    public const int KeepOrderExpansionsPerPart = 400;

    private readonly Entry[] entries;
    private readonly CuttingParameters ownedParameters;

    private CuttingPlanBatch(Entry[] entries, CuttingParameters ownedParameters)
    {
        this.entries = entries;
        this.ownedParameters = ownedParameters;
    }

    public int PlateCount => entries.Length;

    /// <summary>
    /// Captures every plate's exact state with an owned copy of <paramref name="confirmedParameters"/>.
    /// When the order may change, the current-order request is captured too, so a free search that
    /// runs out of budget can be retried on the worker without reading live plates again.
    /// </summary>
    public static CuttingPlanBatch Capture(IReadOnlyList<Plate> plates, CuttingParameters confirmedParameters,
        bool preservePartOrder, IReadOnlyList<int> plateNumbers = null, CancellationToken token = default) =>
        Capture(plates, confirmedParameters, preservePartOrder, plateNumbers, ReorderExpansionBudget, token);

    internal static CuttingPlanBatch Capture(IReadOnlyList<Plate> plates, CuttingParameters confirmedParameters,
        bool preservePartOrder, IReadOnlyList<int> plateNumbers, int reorderBudget, CancellationToken token)
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
            var keepOrderBudget = KeepOrderBudget(plate.Parts.Count);
            var keepOrder = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate,
                expansionBudget: keepOrderBudget, confirmedParameters: confirmedParameters,
                preservePartOrder: true), token);
            var reorder = preservePartOrder ? null : CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate,
                expansionBudget: reorderBudget, confirmedParameters: confirmedParameters), token);
            entries[index] = new(plate, plateNumbers?[index] ?? index + 1, reorder, keepOrder);
        }
        return new(entries, owned);
    }

    internal static int KeepOrderBudget(int partCount) =>
        (int)System.Math.Min(int.MaxValue,
            System.Math.Max((long)ReorderExpansionBudget, (long)partCount * KeepOrderExpansionsPerPart));

    /// <summary>
    /// Plans every plate from its captured snapshot. Safe on a worker: live plates are not read.
    /// A free search that ends without a complete plan within its budget is retried with the
    /// current order; the proposal reports that it kept the order.
    /// </summary>
    public CuttingPlanProposal Plan(IProgress<CuttingPlanProgress> progress = null,
        CancellationToken token = default)
    {
        var plans = new CuttingPlanPlateResult[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            CuttingPlanResult reorder = null;
            if (entry.Reorder != null)
            {
                progress?.Report(new(index, entries.Length, entry.Number, CuttingPlanPhase.Reordering));
                reorder = CuttingPlanService.Plan(entry.Reorder, token);
                if (reorder.Status != CuttingPlanStatus.NoSolutionWithinBudget)
                {
                    plans[index] = new(entry.Plate, entry.Number, reorder, null);
                    continue;
                }
            }
            progress?.Report(new(index, entries.Length, entry.Number, CuttingPlanPhase.KeepingOrder));
            plans[index] = new(entry.Plate, entry.Number, CuttingPlanService.Plan(entry.KeepOrder, token), reorder);
        }
        return new(plans, ownedParameters);
    }

    private sealed record Entry(Plate Plate, int Number, CuttingPlanSnapshot Reorder, CuttingPlanSnapshot KeepOrder);
}

/// <summary>One plate's outcome inside a <see cref="CuttingPlanProposal"/>.</summary>
public sealed class CuttingPlanPlateResult
{
    internal CuttingPlanPlateResult(Plate plate, int plateNumber, CuttingPlanResult result,
        CuttingPlanResult reorderAttempt)
    {
        Plate = plate;
        PlateNumber = plateNumber;
        Result = result;
        ReorderAttempt = reorderAttempt;
    }

    /// <summary>The live plate. Read it only on the thread that owns it.</summary>
    public Plate Plate { get; }
    public int PlateNumber { get; }

    /// <summary>The result that would be applied.</summary>
    public CuttingPlanResult Result { get; }

    /// <summary>The free-order attempt that ran out of budget before the current order was kept; else null.</summary>
    public CuttingPlanResult ReorderAttempt { get; }

    public bool KeptCurrentOrder => ReorderAttempt != null;
    public bool IsReady => Result.Status == CuttingPlanStatus.Ready && Result.IndependentlyReplayed;
    public int PartCount => Result.ProposedOrder.Count;
    public int RegeneratedCount => Result.ProposedOrder.Count(p => p.IsRegenerated);

    public bool OrderChanged =>
        Result.ProposedOrder.Select((proposal, index) => proposal.SourceOrdinal != index).Any(changed => changed);
}

/// <summary>
/// The outcome of a batch. Apply is all-or-nothing and is offered only when every plate is ready.
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

    /// <summary>
    /// Installs every plate's replayed proposal through <see cref="CuttingPlanService.Apply"/>, on the
    /// thread that owns the plates. Only after every plate is applied does each one keep an owned copy
    /// of the confirmed parameters as its cutting settings; any other status changes nothing.
    /// </summary>
    public CuttingCommitResult Apply(CancellationToken token = default)
    {
        if (!CanApply)
            return new(CuttingCommitStatus.InvalidInput,
                "Every plate must have a ready plan before anything is applied.");
        var commit = CuttingPlanService.Apply(Plates.Select(p => p.Result), token);
        if (commit.Status == CuttingCommitStatus.Applied)
            foreach (var plate in Plates)
                plate.Plate.CuttingParameters = OwnedCuttingParameters.Copy(ownedParameters);
        return commit;
    }

    /// <summary>
    /// A detached copy of one plate for display: ready plates show the proposed order and programs,
    /// others the current parts in their current order (finding part numbers refer to it). The copy
    /// has quantity zero, so adding its parts leaves drawing quantities unchanged.
    /// </summary>
    public Plate BuildPreview(int index)
    {
        var planned = Plates[index];
        var source = planned.Plate;
        var preview = new Plate(source.Size)
        {
            Quantity = 0,
            Quadrant = source.Quadrant,
            PartSpacing = source.PartSpacing,
            EdgeSpacing = source.EdgeSpacing,
        };
        if (!planned.IsReady)
        {
            foreach (var part in source.Parts)
                preview.Parts.Add((Part)part.Clone());
            return preview;
        }
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

        yield return heading + StatusText(result.Status);
        if (plate.KeptCurrentOrder)
            yield return "  No new part order was found within the search limit, and planning with the "
                + "current order was refused:";
        foreach (var finding in result.Findings.Take(FindingsPerPlate))
            yield return "  - " + DescribeFinding(finding);
        if (result.Findings.Count > FindingsPerPlate)
            yield return $"  ... and {result.Findings.Count - FindingsPerPlate} more.";
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

    private static string Name(Part part) =>
        string.IsNullOrWhiteSpace(part?.BaseDrawing?.Name) ? string.Empty : $" ({part.BaseDrawing.Name})";

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}
