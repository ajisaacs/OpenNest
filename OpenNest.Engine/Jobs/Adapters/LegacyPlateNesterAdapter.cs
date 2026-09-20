using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest;

/// <summary>
/// A fresh private legacy plate/drawing/item graph for each call. Only returned poses cross the boundary;
/// legacy quantity mutations are deliberately ignored. Does not certify geometric safety or rotation compliance.
/// </summary>
public sealed class LegacyPlateNesterAdapter : IPlateNester
{
    private readonly Func<Plate, NestEngineBase> engineFactory;

    public LegacyPlateNesterAdapter(Func<Plate, NestEngineBase> engineFactory)
    {
        ArgumentNullException.ThrowIfNull(engineFactory);
        this.engineFactory = engineFactory;
    }

    /// <summary>Convenience overload delegating to <see cref="PlateNesterFactory"/> so strategy
    /// resolution has a single source of truth; rejects unknown keys. Never reads or changes the
    /// process-global NestEngineRegistry.</summary>
    public static IPlateNester Create(string strategy) => PlateNesterFactory.Create(strategy);

    public PlateCandidate Place(
        PlatePlacementRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        var plate = DrawingJobMapper.CreatePlate(request.Stock);
        var items = new List<NestItem>();
        var identities = new Dictionary<Drawing, string>(ReferenceEqualityComparer.Instance);
        foreach (var requirement in request.Parts)
        {
            var drawing = DrawingJobMapper.CreateDrawing(requirement);
            identities.Add(drawing, requirement.Id);
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
        var engine =
            engineFactory(plate)
            ?? throw new InvalidOperationException("Legacy engine factory returned null.");
        var legacyProgress =
            progress == null ? null : new LegacyProgress(progress, request.Stock.Id);
        var parts = engine.Nest(items, legacyProgress, token);
        token.ThrowIfCancellationRequested();
        if (parts == null)
            throw new InvalidOperationException("Legacy engine returned null placements.");
        var placements = new List<NestJobPlacement>();
        foreach (var part in parts)
        {
            if (part?.BaseDrawing == null || !identities.TryGetValue(part.BaseDrawing, out var id))
                throw new InvalidOperationException(
                    "Legacy placement does not reference a private requirement drawing."
                );
            placements.Add(
                new NestJobPlacement(id, 0, part.Location.X, part.Location.Y, part.Rotation)
            );
        }
        return new PlateCandidate(placements);
    }

    private sealed class LegacyProgress(IProgress<NestJobProgress> progress, string stockId)
        : IProgress<NestProgress>
    {
        public void Report(NestProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            progress.Report(
                new NestJobProgress(NestJobStage.EvaluatingCandidate, stockId, -1, 0, 0, value)
            );
        }
    }
}
