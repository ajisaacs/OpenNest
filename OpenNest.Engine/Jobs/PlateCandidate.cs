using System.Collections.Generic;

namespace OpenNest.Engine.Jobs;

/// <summary>Owned candidate poses only; not committed fulfillment or inventory accounting.</summary>
public sealed class PlateCandidate
{
    public PlateCandidate(IEnumerable<NestJobPlacement> placements) =>
        Placements = NestJob.Own(placements);

    public IReadOnlyList<NestJobPlacement> Placements { get; }
}
