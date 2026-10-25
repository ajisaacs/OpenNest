namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// The body of MaterialLedger's <c>GET /api/stock/opennest</c>: the <c>/api/stock</c> shape plus
/// <see cref="Mapped"/>, false when ML has no approved mapping for the OpenNest material.
/// </summary>
public sealed record LedgerStockResponse(
    bool Mapped,
    IReadOnlyList<LedgerStockSpec>? Specs,
    IReadOnlyList<LedgerStockItem>? Items);

/// <summary>
/// Mirrors ML <c>StockApi.OpenNestStockSpecDto</c>. <see cref="Rank"/> is the spec's place in ML's
/// acceptance rule (0 an exact mapping, 1 preferred, higher a substitute); an older ledger sends
/// neither it nor the catalog names, so they stay null and the spec counts as rank 1.
/// </summary>
public sealed record LedgerStockSpec(long MaterialSpecId, string DisplayName, decimal ThicknessIn, string? GaugeLabel)
{
    public int? Rank { get; init; }
    public string? MaterialFamily { get; init; }
    public string? Grade { get; init; }
    public string? Finish { get; init; }
}

/// <summary>
/// Mirrors ML <c>StockApi.StockItemDto</c>. Sizes are inches with <see cref="WidthIn"/> the short
/// side; <see cref="Quantity"/> is free after active holds. <see cref="Kind"/> and
/// <see cref="Shape"/> stay strings so a new ML enum value still deserializes. <see cref="Rank"/>
/// is its spec's rank, null from an older ledger.
/// </summary>
public sealed record LedgerStockItem(
    string ItemCode,
    long MaterialSpecId,
    string Kind,
    decimal WidthIn,
    decimal LengthIn,
    int Quantity,
    string Shape,
    string LocationCode,
    string LocationName,
    string? ParentLocationCode,
    decimal? CostPerUnit,
    DateTime LastObservedAtUtc)
{
    public int? Rank { get; init; }
}

/// <summary>ML <c>InventoryItemKind</c> names.</summary>
public static class LedgerItemKinds
{
    public const string FullSheetBundle = "FullSheetBundle";
    public const string FullSheet = "FullSheet";
    public const string Drop = "Drop";
}

/// <summary>ML <c>ShapeClassification</c> names.</summary>
public static class LedgerItemShapes
{
    public const string Rectangular = "Rectangular";
    public const string IrregularConservativeRectangle = "IrregularConservativeRectangle";
    public const string Unknown = "Unknown";
}

/// <summary>What a ledger stock request came to. Expected failures are results, never exceptions.</summary>
public abstract record LedgerStockResult
{
    private LedgerStockResult() { }

    /// <summary>No ledger URL is set on this PC.</summary>
    public sealed record NotConfigured : LedgerStockResult;

    /// <summary>The ledger did not answer usefully: network error, timeout, bad status or body.</summary>
    public sealed record Unreachable(string Reason) : LedgerStockResult;

    /// <summary>The ledger rejected the API key (401/403).</summary>
    public sealed record Unauthorized : LedgerStockResult;

    /// <summary>The ledger has no approved mapping for this material, grade and thickness.</summary>
    public sealed record Unmapped : LedgerStockResult;

    public sealed record Ok(IReadOnlyList<LedgerStockSpec> Specs, IReadOnlyList<LedgerStockItem> Items) : LedgerStockResult;
}
