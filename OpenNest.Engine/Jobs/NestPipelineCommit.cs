using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.Engine.Jobs;

/// <summary>Applies an explicitly accepted whole-job proposal to empty physical sheets only.
/// Caller must keep the nest and its drawings stable from request construction through commit.</summary>
public static class NestPipelineCommit
{
    public static IReadOnlyList<Plate> ApplyToEmptyPlates(
        NestPipelineResult result,
        PlateManager manager,
        bool allowInvalid = false,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(manager);
        token.ThrowIfCancellationRequested();
        if (!result.CanKeep || (!result.IsValid && !allowInvalid))
            throw new InvalidOperationException("The nesting result cannot be committed without a keepable layout and explicit consent to its violations.");
        if (NestPipelineDrawingFreshness.Changes(result.Job, result.DrawingsByPartId).Count > 0
            || NestPipelineDrawingFreshness.BoundChanges(result.Job, result.Raw,
                result.Plates, result.DrawingsByPartId).Count > 0)
            throw new InvalidOperationException("A drawing or proposed part changed after the nesting proposal was validated; run Auto Nest again.");
        token.ThrowIfCancellationRequested();

        var applied = new List<Plate>();
        // Commit is synchronous on the caller's owning thread. Cancellation is checked
        // before mutation, not partway through attachment (which would leave half a job).
        manager.BeginBatch();
        try
        {
            foreach (var proposed in result.Plates)
            {
                if (proposed.Parts.Count == 0)
                    continue;
                var plate = manager.GetOrCreateEmpty();
                plate.Size = proposed.Stock.Size;
                plate.PartSpacing = proposed.Stock.PartSpacing;
                plate.EdgeSpacing = proposed.Stock.EdgeSpacing;
                plate.Quadrant = proposed.Stock.Quadrant;
                plate.Quantity = 1;
                plate.Parts.AddRange(proposed.Parts);
                applied.Add(plate);
            }
        }
        finally
        {
            manager.EndBatch();
        }
        return applied.AsReadOnly();
    }
}
