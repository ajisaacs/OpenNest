using System;
using System.Threading;

using OpenNest.Engine.Jobs.Placement.Fillers;
namespace OpenNest.Engine.Jobs.Placement;

/// <summary>
/// Built-in remnant placement strategy for the whole-job runner. Fills one candidate per trial with
/// a <see cref="DefaultPlateFiller"/> with the vertical or horizontal remnant <see cref="PlateFillPolicy"/>. Placement is
/// <see cref="DefaultPlateNester"/>'s with that filler: the same fresh private plate per trial and
/// the same run-scoped identity, progress, and accounting boundaries.
/// </summary>
/// <remarks>
/// The remnant fillers share the Fill pipeline's automatic-rotation limitation, so non-automatic
/// requirements route through <see cref="OrderedPlateNester"/> exactly as they do for Fill.
/// </remarks>
public sealed class RemnantPlateNester : IPlateNester
{
    private readonly DefaultPlateNester nester;

    internal RemnantPlateNester(Func<Plate, DefaultPlateFiller> fillerFactory)
    {
        nester = new DefaultPlateNester(
            fillerFactory ?? throw new ArgumentNullException(nameof(fillerFactory)));
    }

    /// <summary>Vertical-remnant policy: minimize X-extent, prefer horizontal placement.</summary>
    internal static RemnantPlateNester Vertical() =>
        new(plate => new DefaultPlateFiller(plate, PlateFillPolicy.VerticalRemnant));

    /// <summary>Horizontal-remnant policy: minimize Y-extent, prefer vertical placement.</summary>
    internal static RemnantPlateNester Horizontal() =>
        new(plate => new DefaultPlateFiller(plate, PlateFillPolicy.HorizontalRemnant));

    public PlateCandidate Place(
        PlatePlacementRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    ) => nester.Place(request, progress, token);
}
