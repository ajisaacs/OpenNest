using System;
using System.Threading;

using OpenNest.Engine.Jobs.Adapters;
namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Migrated built-in placement strategy for the whole-job runner. Reuses <see cref="StripNestEngine"/>
/// iterative shrink-fill/pack geometry with the same run-scoped bookkeeping as <see cref="DefaultPlateNester"/>:
/// remaining demand is read from the request and placement counts are derived from returned placements.
/// </summary>
/// <remarks>
/// The identity/progress boundary mechanics live in <see cref="CandidatePlacementContext"/>: a private
/// <see cref="Drawing"/> per requirement is created once per solve and reused across trials
/// (safe: the engine mutates per-trial <see cref="NestItem.Quantity"/> and canonical copies, never the
/// shared Drawing). Identity is by Drawing reference. Each trial gets a fresh private <see cref="Plate"/>.
/// </remarks>
public sealed class StripPlateNester : IPlateNester
{
    private readonly Func<Plate, StripNestEngine> engineFactory;
    private readonly CandidatePlacementContext context = new();

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
        var items = context.CreateItems(request.Parts);

        var engine =
            engineFactory(plate)
            ?? throw new InvalidOperationException("Engine factory returned null.");
        var legacyProgress = CandidateProgressBridge.Create(progress, request.Stock.Id);
        var parts = engine.Nest(items, legacyProgress, token);
        token.ThrowIfCancellationRequested();
        if (parts == null)
            throw new InvalidOperationException("Engine returned null placements.");

        return new PlateCandidate(context.MapPlacements(parts));
    }
}
