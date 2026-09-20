using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest;

/// <summary>
/// Migrated built-in placement strategy for the whole-job runner. Reuses <see cref="StripNestEngine"/>
/// iterative shrink-fill/pack geometry with the same run-scoped bookkeeping as <see cref="DefaultPlateNester"/>:
/// remaining demand is read from the request and placement counts are derived from returned placements.
/// </summary>
/// <remarks>
/// A private <see cref="Drawing"/> per requirement is created once per solve and reused across trials
/// (safe: the engine mutates per-trial <see cref="NestItem.Quantity"/> and canonical copies, never the
/// shared Drawing). Identity is by Drawing reference. Each trial gets a fresh private <see cref="Plate"/>.
/// </remarks>
public sealed class StripPlateNester : IPlateNester
{
    private readonly Func<Plate, StripNestEngine> engineFactory;
    private readonly Dictionary<string, Drawing> drawingsById = new(StringComparer.Ordinal);
    private readonly Dictionary<Drawing, string> idByDrawing = new(
        ReferenceEqualityComparer.Instance
    );

    public StripPlateNester()
        : this(static plate => new StripNestEngine(plate)) { }

    /// <param name="engineFactory">Injectable for tests; defaults to <see cref="StripNestEngine"/>.</param>
    public StripPlateNester(Func<Plate, StripNestEngine> engineFactory)
    {
        this.engineFactory =
            engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
    }

    public PlateCandidate Place(
        PlatePlacementRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();

        var plate = DrawingJobMapper.CreatePlate(request.Stock);
        var items = new List<NestItem>(request.Parts.Count);
        foreach (var requirement in request.Parts)
        {
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

        var engine =
            engineFactory(plate)
            ?? throw new InvalidOperationException("Engine factory returned null.");
        var legacyProgress = CandidateProgressBridge.Create(progress, request.Stock.Id);
        var parts = engine.Nest(items, legacyProgress, token);
        token.ThrowIfCancellationRequested();
        if (parts == null)
            throw new InvalidOperationException("Engine returned null placements.");

        var placements = new List<NestJobPlacement>(parts.Count);
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

        return new PlateCandidate(placements);
    }
}
