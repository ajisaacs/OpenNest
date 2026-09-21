using System;
using System.Threading;

using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement.Fillers;
namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Built-in placement strategy for the whole-job runner. Runs the <see cref="StripPlateFiller"/>
/// iterative shrink-fill/pack geometry with the same run-scoped bookkeeping as
/// <see cref="DefaultPlateNester"/>: remaining demand is read from the request and placement counts
/// are derived from returned placements.
/// </summary>
/// <remarks>
/// The identity/progress boundary mechanics live in <see cref="CandidatePlacementContext"/>: a private
/// <see cref="Drawing"/> per requirement is created once per solve and reused across trials
/// (safe: the filler mutates per-trial <see cref="NestItem.Quantity"/> and canonical copies, never the
/// shared Drawing). Identity is by Drawing reference. Each trial gets a fresh private <see cref="Plate"/>.
/// </remarks>
public sealed class StripPlateNester : IPlateNester
{
    private readonly Func<Plate, StripPlateFiller> fillerFactory;
    private readonly CandidatePlacementContext context = new();

    public StripPlateNester()
        : this(static plate => new StripPlateFiller(plate)) { }

    /// <param name="fillerFactory">Injectable for tests; defaults to <see cref="StripPlateFiller"/>.</param>
    internal StripPlateNester(Func<Plate, StripPlateFiller> fillerFactory)
    {
        this.fillerFactory =
            fillerFactory ?? throw new ArgumentNullException(nameof(fillerFactory));
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

        var filler =
            fillerFactory(plate)
            ?? throw new InvalidOperationException("Filler factory returned null.");
        var legacyProgress = CandidateProgressBridge.Create(progress, request.Stock.Id);
        var parts = filler.Nest(items, legacyProgress, token);
        token.ThrowIfCancellationRequested();
        if (parts == null)
            throw new InvalidOperationException("Filler returned null placements.");

        return new PlateCandidate(context.MapPlacements(parts));
    }
}
