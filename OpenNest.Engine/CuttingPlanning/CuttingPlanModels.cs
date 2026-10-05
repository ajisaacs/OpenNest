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
        Action<int> expansionObserver = null)
    {
        Placements = Array.AsReadOnly(placements.ToArray());
        StartPoint = startPoint;
        ExpansionBudget = expansionBudget;
        Failure = failure;
        Findings = Array.AsReadOnly((findings ?? []).ToArray());
        Regeneration = regeneration;
        PreservePartOrder = preservePartOrder;
        MaxEntries = maxEntries;
        ExpansionObserver = expansionObserver;
    }

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
    internal FixedProgramPlacement Propose(Program proposed, OwnedExecution execution, IReadOnlyList<ContourChoice> choices) =>
        new(SourcePart, SourceOrdinal, Location, Rotation, LeadInsLocked, execution, OwnedProgramCopy.Copy(proposed), Prepared, Material, choices);

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
}
