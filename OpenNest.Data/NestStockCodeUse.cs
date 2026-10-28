namespace OpenNest.Data;

/// <summary>
/// A ledger item code an open server nest's plates are coded with, returned by
/// <c>GET /api/nests/stock-item-codes</c>. One entry per code and nest; a bundle code used
/// for several sheets appears once.
/// </summary>
public sealed class NestStockCodeUse
{
    public string ItemCode { get; init; } = "";

    public Guid NestId { get; init; }

    public string NestName { get; init; } = "";

    /// <summary>Draft, Quote or To Be Cut: the statuses whose sheets are still to be cut.</summary>
    public NestStatus Status { get; init; }
}
