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
        AutomaticCutOffOptions options, CutOffSettings settings)
    {
        var plans = Create(plates, options, settings);
        if (plans.Any(plan => plan.HasBlockingDiagnostics))
            return plans;

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
            var rollbackErrors = new List<Exception>();
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
                    rollbackErrors.Add(new InvalidOperationException(
                        $"Plate {saved.PlateNumber}: {rollbackError.Message}", rollbackError));
                }
            }
            if (rollbackErrors.Count > 0)
                throw new InvalidOperationException(
                    $"Apply failed: {applyError.Message}\nRestoring cut-offs also failed: "
                    + string.Join("; ", rollbackErrors.Select(e => e.Message))
                    + "\nThe nest may be incomplete; review it before saving or cutting.",
                    new AggregateException(new[] { applyError }.Concat(rollbackErrors)));

            throw new InvalidOperationException(
                $"No new cut-offs were retained; original cut-offs were restored. {applyError.Message}", applyError);
        }
        return plans;
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
