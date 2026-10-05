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
/// Keep sources/settings stable during Capture. This is not an Apply request.
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
    /// result can later be applied through <see cref="CuttingPlanService.Apply"/>.
    /// </summary>
    public CuttingPlanRequest(Plate plate, Vector startPoint = default, int expansionBudget = 20000,
        CuttingParameters confirmedParameters = null, IEnumerable<Part> eligibleParts = null,
        bool preservePartOrder = false, int maxEntries = 16)
        : this(plate?.Parts, startPoint, expansionBudget, confirmedParameters, eligibleParts,
            preservePartOrder, maxEntries)
    {
        Plate = plate;
    }

    /// <summary>The plate scope, or null for a detached part list that cannot be applied.</summary>
    public Plate Plate { get; }
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
        CuttingParameters ownedParameters = null, CuttingDependencyGraph dependencies = null)
    {
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
    internal bool Regeneration { get; }
    internal bool PreservePartOrder { get; }
    internal int MaxEntries { get; }
    internal Action<int> ExpansionObserver { get; }
    public IReadOnlyList<FixedProgramPlacement> Placements { get; }
    public Vector StartPoint { get; }
    public int ExpansionBudget { get; }
    internal CuttingPlanStatus? Failure { get; }
    internal IReadOnlyList<CuttingPlanFinding> Findings { get; }
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
        { SelectedProgram = selected, IsCutOff = IsCutOff };
    }

    /// <summary>A cutoff: always a fixed open-cut program, never material or regenerated.</summary>
    public bool IsCutOff { get; internal init; }

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
    Cancelled
}

/// <summary>Ordinals are zero-based source positions, not proposed sequence positions.</summary>
public sealed record CuttingPlanFinding(int? SourceOrdinal, Part SourcePart,
    int? OtherSourceOrdinal, Part OtherSourcePart, PostVerificationKind? Kind, string Message);

/// <summary>
/// A replayed direct-XY proposal, optionally with regenerated programs. Not physical
/// safety, posting consent, dependency readiness or an atomic Apply payload. Failures contain no proposals.
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
