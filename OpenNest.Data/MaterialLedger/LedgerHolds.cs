namespace OpenNest.Data.MaterialLedger;

/// <summary>The body of MaterialLedger's <c>GET /api/stock/holds?openNestNestId=</c>.</summary>
public sealed record LedgerHoldsResponse(Guid OpenNestNestId, IReadOnlyList<LedgerHold>? Holds);

/// <summary>
/// One ML <c>MaterialReservation</c> for a sheet of the nest. Sizes are inches with
/// <see cref="WidthIn"/> the short side. <see cref="Status"/> stays a string so a new ML
/// <c>ReservationStatus</c> value still deserializes.
/// </summary>
public sealed record LedgerHold(
    long MaterialReservationId,
    string ItemCode,
    decimal WidthIn,
    decimal LengthIn,
    string Status,
    string? LocationCode,
    DateTime CreatedAtUtc);

/// <summary>ML <c>ReservationStatus</c> names.</summary>
public static class LedgerHoldStatuses
{
    public const string Proposed = "Proposed";
    public const string Approved = "Approved";
    public const string Released = "Released";
    public const string AwaitingCloseout = "AwaitingCloseout";
    public const string ClosedConsumed = "ClosedConsumed";
    public const string ClosedReturned = "ClosedReturned";
    public const string Canceled = "Cancelled";
    public const string Exception = "Exception";

    /// <summary>
    /// True for holds that still keep a sheet for the nest and need closing out: the statuses ML
    /// counts against free stock. Released means the hold was let go, not sent to cutting, so it
    /// is not open; nor are closed, canceled, exception and unknown statuses.
    /// </summary>
    public static bool IsOpen(string? status) =>
        string.Equals(status, Proposed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Approved, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, AwaitingCloseout, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a ledger hold request came to. Expected failures are results, never exceptions.</summary>
public abstract record LedgerHoldsResult
{
    private LedgerHoldsResult() { }

    /// <summary>No ledger URL is set on this PC.</summary>
    public sealed record NotConfigured : LedgerHoldsResult;

    /// <summary>The ledger did not answer usefully: network error, timeout, bad status or body.</summary>
    public sealed record Unreachable(string Reason) : LedgerHoldsResult;

    /// <summary>The ledger rejected the API key (401/403).</summary>
    public sealed record Unauthorized : LedgerHoldsResult;

    /// <summary>The nest's holds in every status; empty when ML has none for it.</summary>
    public sealed record Ok(IReadOnlyList<LedgerHold> Holds) : LedgerHoldsResult;
}
