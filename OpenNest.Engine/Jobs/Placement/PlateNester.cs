using System;
using System.Linq;
using System.Threading;

using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement.Fillers;
namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Built-in placement strategy for the whole-job runner, for every entry in
/// <see cref="PlacementStrategies"/>. Fills each candidate trial with a <see cref="PlateFiller"/>
/// on a fresh private plate and owns its own run-scoped bookkeeping: remaining demand is read
/// from the request and placement counts are derived from returned placements, so the filler's
/// private <see cref="NestItem.Quantity"/> mutations never feed back into job accounting.
/// </summary>
/// <remarks>
/// The identity/progress boundary mechanics live in <see cref="CandidatePlacementContext"/>: one
/// private <see cref="Drawing"/> per requirement is created once per solve and reused across every
/// candidate trial (the runner reuses one <see cref="IPlateNester"/> instance per job). This is safe
/// because the fillers mutate <see cref="NestItem.Quantity"/> (per-trial) and canonical-frame copies,
/// never the shared <see cref="Drawing"/> or its <c>Quantity</c>. Identity is by Drawing reference,
/// never by name. Each trial gets a fresh private <see cref="Plate"/> and filler, so learned angles
/// never cross trials.
/// </remarks>
internal sealed class PlateNester : IPlateNester
{
    private readonly Func<Plate, PlateFiller> fillerFactory;
    private readonly OrderedPlateNester restrictedRotationNester = new();
    private readonly CandidatePlacementContext context = new();

    /// <param name="fillerFactory">Builds the trial's filler; injectable for tests.</param>
    internal PlateNester(Func<Plate, PlateFiller> fillerFactory)
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

        // No filler can express a locked or bounded rotation (start == end == 0 reads as
        // "unconstrained"): the Pairs/RectBestFit strategies rotate freely and the strip fills
        // try two classification angles, so they can return poses the requirement's
        // RotationPolicy forbids. Restricted requirements go to the policy-aware ordered nester,
        // which only proposes allowed angles and validates each pose.
        if (request.Parts.Any(part => part.Rotation.Kind != RotationPolicyKind.Automatic))
            return restrictedRotationNester.Place(request, progress, token);

        var plate = DrawingJobMapper.CreatePlate(request.Stock);
        // Quantity is the request's remaining demand; the filler may mutate these per-trial items,
        // and that mutation is deliberately discarded — placement counts come from the result.
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
