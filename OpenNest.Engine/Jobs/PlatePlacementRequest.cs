using System;
using System.Collections.Generic;

namespace OpenNest;

/// <summary>Read-only stock settings and remaining requirements for a single candidate trial.</summary>
public sealed class PlatePlacementRequest
{
    public PlatePlacementRequest(NestPlateStock stock, IEnumerable<NestJobPart> parts)
    {
        ArgumentNullException.ThrowIfNull(stock);
        Stock = stock;
        Parts = NestJob.Own(parts);
    }

    public NestPlateStock Stock { get; }
    public IReadOnlyList<NestJobPart> Parts { get; }
}
