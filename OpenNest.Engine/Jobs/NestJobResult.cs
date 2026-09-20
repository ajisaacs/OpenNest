using System;
using System.Collections.Generic;

namespace OpenNest;

public enum NestJobStatus
{
    Complete,
    Incomplete,
}

public enum NestJobStopReason
{
    Completed,
    StockExhausted,
    NoPlacementFound,
    PlateLimitReached,
}

/// <summary>
/// Rotate about the snapshot origin, then translate by X/Y into the selected plate quadrant frame.
/// Rotation is in radians. InstanceIndex is zero-based and unique within a part requirement across the job.
/// The runner assigns final instance indices when committing a candidate.
/// </summary>
public sealed record NestJobPlacement(
    string PartId,
    int InstanceIndex,
    double X,
    double Y,
    double Rotation
);

/// <summary>Requested = Placed + Unplaced for a requirement ID.</summary>
public sealed record PartFulfillment(string PartId, int Requested, int Placed, int Unplaced);

/// <summary>Used counts physical sheets; Remaining is null only for unlimited stock.</summary>
public sealed record StockUsage(string StockId, int Used, int? Remaining);

/// <summary>One physical sheet, with owned ordered placements and immutable stock/settings snapshot.</summary>
public sealed class NestJobPlateResult
{
    public NestJobPlateResult(
        int plateIndex,
        NestPlateStock stock,
        IEnumerable<NestJobPlacement> placements
    )
    {
        ArgumentNullException.ThrowIfNull(stock);
        PlateIndex = plateIndex;
        Stock = stock;
        Placements = NestJob.Own(placements);
    }

    public int PlateIndex { get; }
    public string StockId => Stock.Id;
    public NestPlateStock Stock { get; }
    public IReadOnlyList<NestJobPlacement> Placements { get; }
}

/// <summary>Detached result values in commit/input order; no mutable Drawing, Plate, or NestItem escapes.</summary>
public sealed class NestJobResult
{
    public NestJobResult(
        NestJobStatus status,
        NestJobStopReason stopReason,
        IEnumerable<NestJobPlateResult> plates,
        IEnumerable<PartFulfillment> fulfillment,
        IEnumerable<StockUsage> stockUsage
    )
    {
        Status = status;
        StopReason = stopReason;
        Plates = NestJob.Own(plates);
        Fulfillment = NestJob.Own(fulfillment);
        StockUsage = NestJob.Own(stockUsage);
    }

    public NestJobStatus Status { get; }
    public NestJobStopReason StopReason { get; }
    public IReadOnlyList<NestJobPlateResult> Plates { get; }
    public IReadOnlyList<PartFulfillment> Fulfillment { get; }
    public IReadOnlyList<StockUsage> StockUsage { get; }
}
