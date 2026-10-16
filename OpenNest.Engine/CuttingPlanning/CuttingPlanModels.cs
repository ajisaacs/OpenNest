using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Caller-side direct-XY input. Regeneration requires explicit confirmed parameters.
/// Keep sources/settings stable during Capture. Only plate-scoped requests (<see cref="ForPlate"/>)
/// record the state that CuttingPlanService.Apply checks; a detached part list cannot be applied.
/// </summary>
public sealed class CuttingPlanRequest
{
    public CuttingPlanRequest(IEnumerable<Part> parts, Vector startPoint = default, int expansionBudget = 20000,
        CuttingParameters confirmedParameters = null, IEnumerable<Part> eligibleParts = null,
        bool preservePartOrder = false, int maxEntries = 16)
    {
        Parts = parts == null ? null : Array.AsReadOnly(parts.ToArray());
        StartPoint = startPoint;
        ExpansionBudget = expansionBudget;
        ConfirmedParameters = confirmedParameters;
        EligibleParts = eligibleParts == null ? null : Array.AsReadOnly(eligibleParts.ToArray());
        PreservePartOrder = preservePartOrder;
        MaxEntries = maxEntries;
    }

    /// <summary>
    /// Plate scope: plans the plate's current parts and captures its exact state, so a Ready
    /// result can later be applied through CuttingPlanService.Apply. A factory rather than a
    /// constructor overload, so <c>new CuttingPlanRequest(null)</c> stays unambiguous.
    /// </summary>
    public static CuttingPlanRequest ForPlate(Plate plate, Vector startPoint = default, int expansionBudget = 20000,
        CuttingParameters confirmedParameters = null, IEnumerable<Part> eligibleParts = null,
        bool preservePartOrder = false, int maxEntries = 16) =>
        new(plate?.Parts, startPoint, expansionBudget, confirmedParameters, eligibleParts,
            preservePartOrder, maxEntries)
        { Plate = plate };

    /// <summary>The plate scope, or null for a detached part list that cannot be applied.</summary>
    public Plate Plate { get; private init; }
    public CuttingParameters ConfirmedParameters { get; }
    public IReadOnlyList<Part> EligibleParts { get; }
    public bool PreservePartOrder { get; }
    public int MaxEntries { get; }
    internal Action<int> ExpansionObserver { get; init; }
    public IReadOnlyList<Part> Parts { get; }
    public Vector StartPoint { get; }
    public int ExpansionBudget { get; }
}

/// <summary>Owned worker input. SourcePart references are identity handles only, never worker data.</summary>
public sealed class CuttingPlanSnapshot
{
    internal CuttingPlanSnapshot(IEnumerable<FixedProgramPlacement> placements, Vector startPoint,
        int expansionBudget, CuttingPlanStatus? failure = null, IEnumerable<CuttingPlanFinding> findings = null,
        bool regeneration = false, bool preservePartOrder = false, int maxEntries = 16,
        Action<int> expansionObserver = null, PlateCuttingState plateState = null,
        CuttingParameters ownedParameters = null, CuttingDependencyGraph dependencies = null, bool bestEffort = false)
    {
        BestEffort = bestEffort;
        PlateState = plateState;
        OwnedParameters = ownedParameters;
        Placements = Array.AsReadOnly(placements.ToArray());
        Dependencies = dependencies ?? CuttingDependencyGraph.Empty(Placements.Count);
        StartPoint = startPoint;
        ExpansionBudget = expansionBudget;
        Failure = failure;
        Findings = Array.AsReadOnly((findings ?? []).ToArray());
        Regeneration = regeneration;
        PreservePartOrder = preservePartOrder;
        MaxEntries = maxEntries;
        ExpansionObserver = expansionObserver;
    }

    /// <summary>Whole-part prerequisites by source ordinal (cutoffs, inner parts before hosts).</summary>
    internal CuttingDependencyGraph Dependencies { get; }
    /// <summary>Exact captured plate state for plate-scoped requests; null for detached part lists.</summary>
    internal PlateCuttingState PlateState { get; }
    /// <summary>Owned copy of the confirmed parameters taken at capture; never the caller's object.</summary>
    internal CuttingParameters OwnedParameters { get; }
    internal bool BestEffort { get; }
    internal bool Regeneration { get; }
    internal bool PreservePartOrder { get; }
    internal int MaxEntries { get; }
    internal Action<int> ExpansionObserver { get; }
    public IReadOnlyList<FixedProgramPlacement> Placements { get; }
    public Vector StartPoint { get; }
    public int ExpansionBudget { get; }
    internal CuttingPlanStatus? Failure { get; }
    internal IReadOnlyList<CuttingPlanFinding> Findings { get; }

    // A worker-only search variant over the same owned capture. Never recapture the live plate:
    // both proposals retain the identical freshness record and confirmed settings.
    internal CuttingPlanSnapshot WithSearchLimits(int expansionBudget, int maxEntries) =>
        new(Placements, StartPoint, expansionBudget, Failure, Findings, Regeneration,
            PreservePartOrder, maxEntries, ExpansionObserver, PlateState, OwnedParameters,
            Dependencies, BestEffort);
}

public sealed class FixedProgramPlacement
{
    internal FixedProgramPlacement(Part sourcePart, int sourceOrdinal, Vector location,
        double rotation, bool leadInsLocked, OwnedExecution execution, Program program = null,
        PreparedContours prepared = null, LeadMaterialSnapshot material = null,
        IReadOnlyList<ContourChoice> choices = null)
    {
        SourcePart = sourcePart;
        SourceOrdinal = sourceOrdinal;
        Location = location;
        Rotation = rotation;
        LeadInsLocked = leadInsLocked;
        Execution = execution;
        this.program = program;
        Prepared = prepared;
        Material = material;
        ContourChoices = Array.AsReadOnly((choices ?? []).ToArray());
    }

    private readonly Program program;
    internal PreparedContours Prepared { get; }
    internal LeadMaterialSnapshot Material { get; }
    public IReadOnlyList<ContourChoice> ContourChoices { get; }
    public bool IsRegenerated => ContourChoices.Count != 0;
    /// <summary>Returns an independent deep copy; never an alias to captured/proposed code.</summary>
    public Program CopyProgram() => program == null ? null : OwnedProgramCopy.Copy(program);
    internal SelectedContourProgram SelectedProgram { get; private init; }

    /// <summary>
    /// The motions of <paramref name="execution"/> (this placement's own program read with its
    /// incoming rapid first) that are a fixed emission, accepted as given in regeneration mode:
    /// everything a locked program does after the planner's rapid into it, and the lead-in, cut,
    /// lead-out and tab of every drawing-pinned contour in a regenerated program, whose
    /// <paramref name="choices"/> (default: this proposal's) were emitted. The rapids between
    /// contours stay the planner's. Null when nothing is fixed. Cutoffs and unlocked programs a
    /// caller left ineligible keep their strict checks.
    /// </summary>
    internal Func<int, bool> FixedMotions(OwnedExecution execution, IReadOnlyList<ContourChoice> choices = null)
    {
        if (IsCutOff)
            return null;
        if (Prepared != null)
        {
            var pins = PinsByMotion(execution, choices);
            return pins == null ? null : motion => pins[motion] != null;
        }
        if (!LeadInsLocked)
            return null;
        var opensWithRapid = execution.Motions.Count != 0 && execution.Motions[0].Rapid;
        return motion => motion > 0 || !opensWithRapid;
    }

    /// <summary>
    /// For each motion of a regenerated program emitted from <paramref name="choices"/>, the
    /// one-based drawing pin whose contour it cuts (lead-ins, cuts, lead-outs; never the rapid
    /// into the contour), or null. Null overall when no choice is pinned, or when the motions do
    /// not split into one group per choice after the scribes, so nothing is treated as fixed.
    /// </summary>
    internal int?[] PinsByMotion(OwnedExecution execution, IReadOnlyList<ContourChoice> choices = null)
    {
        choices ??= ContourChoices;
        if (Prepared == null || !choices.Any(c => Prepared.IsPinned(c.ContourOrdinal)))
            return null;
        var pins = new int?[execution.Motions.Count];
        var group = -1;
        var opening = true; // Inside the rapids that open a group.
        int? pin = null;
        for (var index = 0; index < execution.Motions.Count; index++)
        {
            var motion = execution.Motions[index];
            if (motion.Rapid)
            {
                if (!opening)
                    opening = true;
                continue;
            }
            if (opening)
            {
                opening = false;
                // Scribes are emitted first, before any contour.
                if (motion.Layer == LayerType.Scribe)
                {
                    pin = null;
                    if (group >= 0)
                        return null;
                    continue;
                }
                if (++group >= choices.Count)
                    return null;
                pin = Prepared.PinNumberOf(choices[group].ContourOrdinal);
            }
            pins[index] = pin;
        }
        return group == choices.Count - 1 ? pins : null;
    }

    internal FixedProgramPlacement Propose(Program proposed, OwnedExecution execution, IReadOnlyList<ContourChoice> choices,
        System.Threading.CancellationToken token = default)
    {
        SelectedContourProgram selected = null;
        try
        {
            // The expected program is constructed from owned choices/settings, NEVER
            // from proposed or its cached execution. Counterfeit payloads remain subject
            // to independent final checks, including callers of this internal test seam.
            selected = Prepared?.CaptureSelectedProgram(choices, Location, token);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException or ArithmeticException)
        {
            // Invalid metadata is a refused proposal, not an exception from final replay.
        }
        return new(SourcePart, SourceOrdinal, Location, Rotation, LeadInsLocked, execution,
            OwnedProgramCopy.Copy(proposed, token), Prepared, Material, choices)
        { SelectedProgram = selected, IsCutOff = IsCutOff, SeparatesSheet = SeparatesSheet };
    }

    /// <summary>A cutoff: always a fixed open-cut program, never material or regenerated.</summary>
    public bool IsCutOff { get; internal init; }

    /// <summary>A cutoff that splits the whole sheet; it is cut after every other placement.</summary>
    public bool SeparatesSheet { get; internal init; }

    public Part SourcePart { get; }
    public int SourceOrdinal { get; }
    public Vector Location { get; }
    public double Rotation { get; }
    public bool LeadInsLocked { get; }
    public OwnedExecution Execution { get; }
}

public enum CuttingPlanStatus
{
    Ready,
    ConstraintConflict,
    UnsupportedGeometry,
    InvalidInput,
    NoSolutionWithinBudget,
    Cancelled,
    /// <summary>Usable owned programs, but geometric safety checks did not pass. Never Ready.</summary>
    BestEffort
}

/// <summary>Ordinals are zero-based source positions, not proposed sequence positions.</summary>
public sealed record CuttingPlanFinding(int? SourceOrdinal, Part SourcePart,
    int? OtherSourceOrdinal, Part OtherSourcePart, PostVerificationKind? Kind, string Message)
{
    /// <summary>
    /// The check's own finding, for its world-coordinate markers (location, rapid, contacts).
    /// Its part numbers are check positions, not source ordinals. Null when the check gave none.
    /// Not part of equality: repeated rejections of the same problem stay one finding.
    /// </summary>
    public PostVerificationFinding Witness { get; init; }

    /// <summary>
    /// For a <see cref="PostVerificationKind.FixedLead"/> warning, what the fixed emission is and
    /// what it hits; null otherwise. Not part of equality (the message already names both).
    /// </summary>
    public FixedLeadWarning FixedLead { get; init; }

    public bool Equals(CuttingPlanFinding other) => other is not null
        && SourceOrdinal == other.SourceOrdinal && Equals(SourcePart, other.SourcePart)
        && OtherSourceOrdinal == other.OtherSourceOrdinal && Equals(OtherSourcePart, other.OtherSourcePart)
        && Kind == other.Kind && Message == other.Message;

    public override int GetHashCode() =>
        HashCode.Combine(SourceOrdinal, SourcePart, OtherSourceOrdinal, OtherSourcePart, Kind, Message);
}

/// <summary>
/// A violation inside a fixed emission, accepted as given rather than refused (D1a); the part is
/// the finding's source part. <see cref="ContourNumber"/> is one-based in the part's program
/// (null when the whole program is concerned); <see cref="PinNumber"/> is the one-based drawing
/// pin, or null for a locked program; <see cref="ObstacleSourceOrdinal"/> is the other part hit,
/// or null for the part's own material, a released contour named in the message, or an
/// uncertifiable gap.
/// </summary>
public sealed record FixedLeadWarning(int? ContourNumber, int? PinNumber, int? ObstacleSourceOrdinal);

/// <summary>
/// A replayed direct-XY proposal, optionally with regenerated programs, that respects the
/// captured cutoff/containment prerequisites. Not physical safety or posting consent. Apply
/// installs it only for a plate-scoped request whose plate is unchanged. BestEffort is explicitly
/// unverified and requires batch warning acceptance; other failures contain no proposals. A
/// BestEffort result that is also <see cref="IndependentlyReplayed"/> passed every strict check
/// except inside fixed emissions, whose violations are its FixedLead warnings.
/// </summary>
public sealed class CuttingPlanResult
{
    internal CuttingPlanResult(CuttingPlanStatus status, IEnumerable<FixedProgramPlacement> order = null,
        IEnumerable<CuttingPlanFinding> findings = null, int expansions = 0,
        double rapidDistance = 0, bool independentlyReplayed = false)
    {
        Status = status;
        ProposedOrder = Array.AsReadOnly((order ?? []).ToArray());
        Findings = Array.AsReadOnly((findings ?? []).ToArray());
        Expansions = expansions;
        RapidDistance = rapidDistance;
        IndependentlyReplayed = independentlyReplayed;
    }

    public CuttingPlanStatus Status { get; }
    public IReadOnlyList<FixedProgramPlacement> ProposedOrder { get; }
    public IReadOnlyList<CuttingPlanFinding> Findings { get; }
    public int Expansions { get; }
    public double RapidDistance { get; }
    public bool IndependentlyReplayed { get; }
    /// <summary>The captured input this result was planned from; binds Apply to its freshness record.</summary>
    internal CuttingPlanSnapshot Snapshot { get; set; }
}
