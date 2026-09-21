using System;
using System.Threading;

namespace OpenNest.Engine.Jobs;

/// <summary>Places on one sheet only. Must not change stock, demand, or caller-owned domain objects.</summary>
public interface IPlateNester
{
    PlateCandidate Place(
        PlatePlacementRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    );
}
