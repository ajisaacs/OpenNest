using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest;

/// <summary>
/// Plans and applies the same automatic-cutoff settings to a stable, ordered set of plates.
/// Callers must prevent concurrent edits for the duration of either operation.
/// </summary>
public static class AutomaticCutOffBatch
{
    /// <summary>Detached plans in plate order. Invalid input identifies the one-based plate number.</summary>
    public static IReadOnlyList<AutomaticCutOffPlan> Create(IReadOnlyList<Plate> plates,
        AutomaticCutOffOptions options, CutOffSettings settings)
    {
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        if (plates.Distinct(ReferenceEqualityComparer.Instance).Count() != plates.Count)
            throw new ArgumentException("Each plate must occur only once.", nameof(plates));

        var plans = new List<AutomaticCutOffPlan>(plates.Count);
        for (var index = 0; index < plates.Count; index++)
        {
            try
            {
                plans.Add(AutomaticCutOffPlanner.Create(plates[index], options, settings));
            }
            catch (ArgumentException error)
            {
                throw new ArgumentException($"Plate {index + 1}: {error.Message}", nameof(plates), error);
            }
        }
        return plans.AsReadOnly();
    }

    /// <summary>
    /// Replans all plates before changing any. A blocking plan returns without applying any
    /// definitions; empty/unchanged plates are untouched. On failure, restores cutoff state
    /// on every touched plate, reporting explicitly if any restoration also fails.
    /// Returned plans describe the fresh proposal, not preview parts to accept into a plate.
    /// </summary>
    public static IReadOnlyList<AutomaticCutOffPlan> Apply(IReadOnlyList<Plate> plates,
        AutomaticCutOffOptions options, CutOffSettings settings) => Stage(plates, options, settings).Plans;

    /// <summary>
    /// Applies like <see cref="Apply"/>, but keeps the recovery state so the caller can take the
    /// new cut-offs back off again (<see cref="AutomaticCutOffStaging.Revert"/>) when the work they
    /// were added for is abandoned. A blocked batch changes nothing and has nothing to revert.
    /// </summary>
    public static AutomaticCutOffStaging Stage(IReadOnlyList<Plate> plates,
        AutomaticCutOffOptions options, CutOffSettings settings)
    {
        var plans = Create(plates, options, settings);
        if (plans.Any(plan => plan.HasBlockingDiagnostics))
            return new AutomaticCutOffStaging(plans, []);

        var restoreActions = new List<(int PlateNumber, Action Restore)>();
        try
        {
            for (var index = 0; index < plates.Count; index++)
            {
                var plan = plans[index];
                if (plan.Definitions.Count == 0)
                    continue;

                var plate = plates[index];
                // Register recovery before the first observable mutation, including AddRange.
                restoreActions.Add((index + 1, CaptureRestore(plate, plan)));
                plate.CutOffs.AddRange(plan.Definitions);
                plate.RegenerateCutOffs(settings);
            }
        }
        catch (Exception applyError)
        {
            var rollbackErrors = RestoreAll(restoreActions);
            if (rollbackErrors.Count > 0)
                throw new InvalidOperationException(
                    $"Apply failed: {applyError.Message}\nRestoring cut-offs also failed: "
                    + string.Join("; ", rollbackErrors.Select(e => e.Message))
                    + "\nThe nest may be incomplete; review it before saving or cutting.",
                    new AggregateException(new[] { applyError }.Concat(rollbackErrors)));

            throw new InvalidOperationException(
                $"No new cut-offs were retained; original cut-offs were restored. {applyError.Message}", applyError);
        }
        return new AutomaticCutOffStaging(plans, restoreActions);
    }

    internal static List<Exception> RestoreAll(IReadOnlyList<(int PlateNumber, Action Restore)> restoreActions)
    {
        var errors = new List<Exception>();
        for (var index = restoreActions.Count - 1; index >= 0; index--)
        {
            var saved = restoreActions[index];
            try
            {
                saved.Restore();
            }
            catch (Exception rollbackError)
            {
                // A broken observer on one plate must not prevent recovery of the others.
                errors.Add(new InvalidOperationException(
                    $"Plate {saved.PlateNumber}: {rollbackError.Message}", rollbackError));
            }
        }
        return errors;
    }

    private static Action CaptureRestore(Plate plate, AutomaticCutOffPlan plan)
    {
        // Regeneration replaces drawing programs and removes/reinserts cutoff parts.
        // Save only that state; real parts, poses, programs and quantities stay untouched.
        var programs = plate.CutOffs.Select(c => (CutOff: c, Program: c.Drawing.Program)).ToArray();
        var parts = plate.Parts.Select((part, index) => (Part: part, Index: index))
            .Where(p => p.Part.BaseDrawing.IsCutOff).ToArray();
        return () =>
        {
            foreach (var definition in plan.Definitions)
                plate.CutOffs.Remove(definition);
            for (var index = plate.Parts.Count - 1; index >= 0; index--)
            {
                if (plate.Parts[index].BaseDrawing.IsCutOff)
                    plate.Parts.RemoveAt(index);
            }
            foreach (var saved in programs)
                saved.CutOff.Drawing.Program = saved.Program;
            foreach (var saved in parts)
                plate.Parts.Insert(saved.Index, saved.Part);
        };
    }
}

/// <summary>
/// Cut-offs added by <see cref="AutomaticCutOffBatch.Stage"/> that are still provisional: either
/// <see cref="Keep"/> them once the work they belong to is committed, or <see cref="Revert"/> them.
/// Owner-thread only, like the plates it changed.
/// </summary>
public sealed class AutomaticCutOffStaging
{
    private IReadOnlyList<(int PlateNumber, Action Restore)> restoreActions;

    internal AutomaticCutOffStaging(IReadOnlyList<AutomaticCutOffPlan> plans,
        IReadOnlyList<(int PlateNumber, Action Restore)> restoreActions)
    {
        Plans = plans;
        this.restoreActions = restoreActions;
        Definitions = plans.SelectMany(plan => plan.HasBlockingDiagnostics ? [] : plan.Definitions).ToArray();
    }

    /// <summary>The fresh plans, one per plate in the order given.</summary>
    public IReadOnlyList<AutomaticCutOffPlan> Plans { get; }

    /// <summary>True when any plate blocked the batch; then nothing was added.</summary>
    public bool IsBlocked => Plans.Any(plan => plan.HasBlockingDiagnostics);

    /// <summary>The definitions this staging added (empty when blocked or nothing was needed).</summary>
    public IReadOnlyList<CutOff> Definitions { get; }

    /// <summary>True while the added cut-offs can still be reverted.</summary>
    public bool CanRevert => restoreActions.Count > 0;

    /// <summary>Makes the added cut-offs permanent; a later <see cref="Revert"/> does nothing.</summary>
    public void Keep() => restoreActions = [];

    /// <summary>
    /// Removes the added cut-offs and restores each touched plate's previous cut-off programs and
    /// sequence. Real parts are not touched. Runs once; throws if any plate could not be restored.
    /// </summary>
    public void Revert()
    {
        var actions = restoreActions;
        restoreActions = [];
        var errors = AutomaticCutOffBatch.RestoreAll(actions);
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Removing the added cut-offs failed: " + string.Join("; ", errors.Select(e => e.Message))
                + "\nThe nest may be incomplete; review it before saving or cutting.",
                new AggregateException(errors));
    }
}
