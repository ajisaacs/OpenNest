using System;
using System.Collections.Generic;

using OpenNest.Engine.Jobs.Adapters;
namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Owns the private Drawing-to-requirement identity map for one plate-nester run.
/// It creates fresh mutable legacy items for each candidate while only returned poses cross back
/// into the immutable jobs boundary.
/// </summary>
internal sealed class CandidatePlacementContext
{
    private readonly Dictionary<string, Drawing> drawingsById = new(StringComparer.Ordinal);
    private readonly Dictionary<Drawing, string> idByDrawing = new(
        ReferenceEqualityComparer.Instance
    );

    internal List<NestItem> CreateItems(IEnumerable<NestJobPart> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var items = new List<NestItem>();
        foreach (var requirement in requirements)
        {
            ArgumentNullException.ThrowIfNull(requirement);
            if (!drawingsById.TryGetValue(requirement.Id, out var drawing))
            {
                drawing = DrawingJobMapper.CreateDrawing(requirement);
                drawingsById.Add(requirement.Id, drawing);
                idByDrawing.Add(drawing, requirement.Id);
            }

            items.Add(
                new NestItem
                {
                    Drawing = drawing,
                    Quantity = requirement.Quantity,
                    Priority = requirement.Priority,
                    StepAngle = DrawingJobMapper.LegacyStep(requirement.Rotation),
                    RotationStart = requirement.Rotation.Start,
                    RotationEnd = requirement.Rotation.End,
                }
            );
        }

        return items;
    }

    internal List<NestJobPlacement> MapPlacements(IEnumerable<Part> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var placements = new List<NestJobPlacement>();
        foreach (var part in parts)
        {
            if (part?.BaseDrawing == null || !idByDrawing.TryGetValue(part.BaseDrawing, out var id))
                throw new InvalidOperationException(
                    "Placement does not reference a known requirement drawing."
                );
            placements.Add(
                new NestJobPlacement(id, 0, part.Location.X, part.Location.Y, part.Rotation)
            );
        }

        return placements;
    }
}
