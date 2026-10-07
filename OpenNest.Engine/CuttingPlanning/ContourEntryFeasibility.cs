using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>The three verdicts a candidate entry can receive from the shared lead validator.</summary>
public enum ContourFeasibilityStatus
{
    /// <summary>Every emitted lead of this contour is complete AND clear.</summary>
    Clear,

    /// <summary>A complete check found a lead contact/overlap — the reason is preserved.</summary>
    Blocked,

    /// <summary>The check could not complete (incomplete material, malformed emission) — never clear.</summary>
    Incomplete,
}

/// <summary>Verdict for one candidate entry. Complete/blocked/incomplete stay distinct.</summary>
public sealed record ContourFeasibilityVerdict(ContourFeasibilityStatus Status, string? Reason)
{
    public bool IsClear => Status == ContourFeasibilityStatus.Clear;
}

/// <summary>
/// Feasibility adapter over the EXISTING <see cref="LeadPathValidator"/>: emits one owned
/// candidate contour through the S06 diagnostic seam, reads it at the placement position,
/// and certifies the emitted lead-in and lead-out against ALL placed material. It is not a
/// new collision implementation and not a plan approval: a Clear verdict certifies this
/// contour's emitted leads only — the missing-lead (NoLeadIn) check still runs later on the
/// complete plan, and rapids/pierce clearance belong to their existing checkers. A
/// candidate rejected here is not proven infeasible by anything else: this adapter only
/// reports what the validator reported.
/// One instance is one captured planning attempt: verdicts cache per exact choice and node
/// context for the instance's lifetime — settings and placement are fixed per instance —
/// and nothing is cached across instances or statically. Evaluation is lazy: only the
/// choice handed to <see cref="Check"/> is ever emitted or validated, and
/// <see cref="EvaluationCount"/> counts validator executions (not cache hits) for cost tests.
/// </summary>
public sealed class ContourEntryFeasibility
{
    private readonly PreparedContours prepared;
    private readonly Vector location;
    private readonly LeadMaterialSnapshot ownMaterial;
    private readonly LeadMaterialSnapshot[] materials;
    private readonly Dictionary<Key, ContourFeasibilityVerdict> cache = new();

    public ContourEntryFeasibility(PreparedContours prepared, Vector location,
        LeadMaterialSnapshot ownMaterial, IReadOnlyList<LeadMaterialSnapshot> otherMaterials)
    {
        this.prepared = prepared ?? throw new ArgumentException("Prepared contours are required.", nameof(prepared));
        this.ownMaterial = ownMaterial ?? throw new ArgumentException("Own material snapshot is required.", nameof(ownMaterial));
        if (otherMaterials == null)
            throw new ArgumentException("Placed-material snapshots are required.", nameof(otherMaterials));
        this.location = location;
        // Immutable copy; the validator's own-material-first convention is preserved.
        var all = new List<LeadMaterialSnapshot> { this.ownMaterial };
        foreach (var material in otherMaterials)
        {
            if (material == null)
                throw new ArgumentException("Placed-material snapshots must not be null.", nameof(otherMaterials));
            if (!ReferenceEquals(material, this.ownMaterial))
                all.Add(material);
        }
        materials = all.ToArray();
    }

    /// <summary>Validator executions performed (cache misses only) during this attempt.</summary>
    public int EvaluationCount { get; private set; }

    /// <summary>
    /// The verdict for one owned candidate. Same choice + node context within this attempt
    /// is answered from cache. Cancellation propagates; a malformed emission is an
    /// Incomplete verdict with the emission's own reason, never a crash and never Clear.
    /// </summary>
    public ContourFeasibilityVerdict Check(ContourChoice choice, string nodeContext = "",
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (choice == null)
            throw new ArgumentException("A contour choice is required.", nameof(choice));
        var key = new Key(choice.ContourOrdinal, choice.EntityOrdinal,
            choice.Point.X, choice.Point.Y, nodeContext ?? string.Empty);
        if (cache.TryGetValue(key, out var known))
            return known;
        var verdict = Probe(choice, token);
        cache[key] = verdict;
        EvaluationCount++;
        return verdict;
    }

    private ContourFeasibilityVerdict Probe(ContourChoice choice, CancellationToken token)
    {
        try
        {
            var program = prepared.EmitCandidateForValidation(choice);
            var execution = ExecutionMotionReader.Read(program, location, null, token);
            var result = LeadPathValidator.Check(execution, ownMaterial, materials, token);
            if (!result.IsComplete)
                return new(ContourFeasibilityStatus.Incomplete, result.Reason);
            return result.IsClear
                ? new(ContourFeasibilityStatus.Clear, null)
                : new(ContourFeasibilityStatus.Blocked, result.Reason);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Malformed emission or foreign choice: refused, reason preserved, never cached as clear.
            return new(ContourFeasibilityStatus.Incomplete, ex.Message);
        }
    }

    private readonly record struct Key(int ContourOrdinal, int EntityOrdinal, double X, double Y, string NodeContext);
}
