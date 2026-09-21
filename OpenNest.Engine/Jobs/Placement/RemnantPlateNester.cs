using System;
using System.Linq;
using System.Threading;

using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement.Fillers;
namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Built-in remnant placement strategy for the whole-job runner. Fills one candidate per trial with
/// a <see cref="RemnantPlateFiller"/> for the vertical or horizontal remnant policy, on a fresh
/// private plate, using the same run-scoped identity, progress, and accounting boundaries as
/// <see cref="DefaultPlateNester"/>.
/// </summary>
/// <remarks>
/// The remnant fillers share the Default pipeline's automatic-rotation limitation, so non-automatic
/// requirements route through <see cref="OrderedPlateNester"/> exactly as they do for Default.
/// </remarks>
public sealed class RemnantPlateNester : IPlateNester
{
    private readonly Func<Plate, RemnantPlateFiller> fillerFactory;
    private readonly OrderedPlateNester restrictedRotationNester = new();
    private readonly CandidatePlacementContext context = new();

    internal RemnantPlateNester(Func<Plate, RemnantPlateFiller> fillerFactory)
    {
        this.fillerFactory =
            fillerFactory ?? throw new ArgumentNullException(nameof(fillerFactory));
    }

    /// <summary>Vertical-remnant policy: minimize X-extent, prefer horizontal placement.</summary>
    internal static RemnantPlateNester Vertical() =>
        new(plate => new RemnantPlateFiller(plate, RemnantFillPolicy.Vertical));

    /// <summary>Horizontal-remnant policy: minimize Y-extent, prefer vertical placement.</summary>
    internal static RemnantPlateNester Horizontal() =>
        new(plate => new RemnantPlateFiller(plate, RemnantFillPolicy.Horizontal));

    public PlateCandidate Place(
        PlatePlacementRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();

        // Same safety rule as DefaultPlateNester: the remnant fillers inherit the Default pipeline's
        // automatic-rotation limitation, so any non-automatic requirement goes to the policy-aware
        // ordered nester.
        if (request.Parts.Any(part => part.Rotation.Kind != RotationPolicyKind.Automatic))
            return restrictedRotationNester.Place(request, progress, token);

        var plate = DrawingJobMapper.CreatePlate(request.Stock);
        var items = context.CreateItems(request.Parts);

        var filler =
            fillerFactory(plate)
            ?? throw new InvalidOperationException("Filler factory returned null.");
        var candidateProgress = CandidateProgressBridge.Create(progress, request.Stock.Id);
        var parts = filler.Nest(items, candidateProgress, token);
        token.ThrowIfCancellationRequested();
        if (parts == null)
            throw new InvalidOperationException("Filler returned null placements.");

        return new PlateCandidate(context.MapPlacements(parts));
    }
}
