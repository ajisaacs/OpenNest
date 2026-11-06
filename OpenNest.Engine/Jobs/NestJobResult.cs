using System;
using System.Collections.Generic;

namespace OpenNest.Engine.Jobs;

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

/// <summary>
/// One common-line group on a result sheet: the zero-based positions of its members in the
/// sheet's <see cref="NestJobPlateResult.Placements"/> and the kerf they were placed at. The
/// validation exempts members from the part spacing only through this explicit list, never by
/// guessing from proximity (see docs/common-line-cutting.md).
/// </summary>
public sealed record NestJobCommonLine
{
    public NestJobCommonLine(double kerf, IEnumerable<int> placements)
    {
        Kerf = kerf;
        Placements = NestJob.Own(placements ?? throw new ArgumentNullException(nameof(placements)));
    }

    public double Kerf { get; }
    public IReadOnlyList<int> Placements { get; }

    public bool Equals(NestJobCommonLine other) =>
        other is not null
        && BitConverter.DoubleToInt64Bits(Kerf) == BitConverter.DoubleToInt64Bits(other.Kerf)
        && System.Linq.Enumerable.SequenceEqual(Placements, other.Placements);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BitConverter.DoubleToInt64Bits(Kerf));
        foreach (var index in Placements)
            hash.Add(index);
        return hash.ToHashCode();
    }
}

/// <summary>One physical sheet, with owned ordered placements and immutable stock/settings snapshot.</summary>
public sealed class NestJobPlateResult
{
    public NestJobPlateResult(
        int plateIndex,
        NestPlateStock stock,
        IEnumerable<NestJobPlacement> placements,
        IEnumerable<NestJobCommonLine> commonLines = null
    )
    {
        ArgumentNullException.ThrowIfNull(stock);
        PlateIndex = plateIndex;
        Stock = stock;
        Placements = NestJob.Own(placements);
        CommonLines = commonLines == null ? Array.Empty<NestJobCommonLine>() : NestJob.Own(commonLines);
    }

    public int PlateIndex { get; }
    public string StockId => Stock.Id;
    public NestPlateStock Stock { get; }
    public IReadOnlyList<NestJobPlacement> Placements { get; }

    /// <summary>Common-line groups among <see cref="Placements"/>; empty when there are none.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<NestJobCommonLine> CommonLines { get; }

    /// <summary><see cref="CommonLines"/> when serialized: omitted when empty, so a sheet without
    /// groups serializes exactly as before they existed.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("CommonLines")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<NestJobCommonLine> SerializedCommonLines => CommonLines.Count == 0 ? null : CommonLines;
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
