using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using OpenNest.Engine.Jobs.Adapters;
namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Migrated built-in placement strategy for the whole-job runner. Reuses <see cref="DefaultNestEngine"/>
/// fill/pack geometry but owns its own run-scoped bookkeeping: remaining demand is read from the
/// request and placement counts are derived from returned placements, so the engine's private
/// <see cref="NestItem.Quantity"/> mutations never feed back into job accounting.
/// </summary>
/// <remarks>
/// A private <see cref="Drawing"/> per requirement is created once per solve and reused across every
/// candidate trial (the runner reuses one <see cref="IPlateNester"/> instance per job). This is safe
/// because the engines mutate <see cref="NestItem.Quantity"/> (per-trial) and canonical-frame copies,
/// never the shared <see cref="Drawing"/> or its <c>Quantity</c>. Identity is by Drawing reference,
/// never by name. Each trial still gets a fresh private <see cref="Plate"/>.
/// </remarks>
public sealed class DefaultPlateNester : IPlateNester
{
    private readonly Func<Plate, DefaultNestEngine> engineFactory;
    private readonly OrderedPlateNester restrictedRotationNester = new();
    private readonly Dictionary<string, Drawing> drawingsById = new(StringComparer.Ordinal);
    private readonly Dictionary<Drawing, string> idByDrawing = new(
        ReferenceEqualityComparer.Instance
    );

    public DefaultPlateNester()
        : this(static plate => new DefaultNestEngine(plate)) { }

    /// <param name="engineFactory">Injectable for tests; defaults to <see cref="DefaultNestEngine"/>.</param>
    public DefaultPlateNester(Func<Plate, DefaultNestEngine> engineFactory)
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

        // The legacy engine cannot express a locked or bounded rotation (start == end == 0 reads
        // as "unconstrained") and its Pairs/RectBestFit strategies rotate freely, so it can return
        // poses the requirement's RotationPolicy forbids. Restricted requirements go to the
        // policy-aware ordered nester, which only proposes allowed angles and validates each pose.
        if (request.Parts.Any(part => part.Rotation.Kind != RotationPolicyKind.Automatic))
            return restrictedRotationNester.Place(request, progress, token);

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

            // Quantity is the request's remaining demand; the engine may mutate this per-trial item,
            // and that mutation is deliberately discarded — placement counts come from the result.
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
