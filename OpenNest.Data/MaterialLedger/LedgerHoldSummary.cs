using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNest.Data.MaterialLedger;

/// <summary>How a nest's open ledger holds compare with the sheets it cuts.</summary>
public enum LedgerHoldState
{
    /// <summary>Every sheet has an open hold (possibly more holds than sheets).</summary>
    Held,

    /// <summary>Some sheets have an open hold and some do not.</summary>
    Short,

    /// <summary>No open holds, whether or not closed ones exist.</summary>
    None,

    /// <summary>The ledger did not answer, or rejected the API key.</summary>
    Unavailable,
}

/// <summary>
/// A read-only summary of a server nest's MaterialLedger holds against its physical sheet
/// count, for the desktop's status line and tooltip. Only open statuses
/// (<see cref="LedgerHoldStatuses.IsOpen"/>) count as held.
/// </summary>
public sealed class LedgerHoldSummary
{
    private const int MaxTooltipHolds = 20;
    private const double MillimetersPerInch = 25.4;

    private LedgerHoldSummary(LedgerHoldState state, int sheets, IReadOnlyList<LedgerHold> holds, string? reason)
    {
        State = state;
        Sheets = sheets;
        Holds = holds;
        Reason = reason;
        OpenHolds = holds.Count(hold => LedgerHoldStatuses.IsOpen(hold.Status));
        ClosedHolds = holds.Count - OpenHolds;
    }

    public LedgerHoldState State { get; }

    /// <summary>The nest's physical sheets (<see cref="PhysicalSheetCount"/>) when it was summarized.</summary>
    public int Sheets { get; }

    /// <summary>Every hold the ledger returned, open or not, in its order.</summary>
    public IReadOnlyList<LedgerHold> Holds { get; }

    public int OpenHolds { get; }

    /// <summary>Holds in any other status: released, closed, canceled, exception or unknown.</summary>
    public int ClosedHolds { get; }

    /// <summary>Sheets without an open hold.</summary>
    public int ShortSheets => System.Math.Max(0, Sheets - OpenHolds);

    /// <summary>Why the ledger is unavailable; null otherwise.</summary>
    public string? Reason { get; }

    /// <summary>The status line, e.g. "Ledger: 2 of 4 held, 2 short".</summary>
    public string Text => State switch
    {
        LedgerHoldState.Unavailable => "Ledger: unavailable",
        LedgerHoldState.None => ClosedHolds == 0
            ? "Ledger: no holds"
            : $"Ledger: no open holds ({ClosedHolds} closed)",
        LedgerHoldState.Short => $"Ledger: {OpenHolds} of {Sheets} held, {ShortSheets} short",
        _ => OpenHolds > Sheets
            ? $"Ledger: {Sheets} of {Sheets} sheets held, {OpenHolds - Sheets} extra"
            : $"Ledger: {Sheets} of {Sheets} sheets held",
    };

    /// <summary>
    /// Sheets the nest cuts: the sum of plate quantities, skipping a trailing plate with no
    /// material parts and no cut-offs as the server's <c>/sheets</c> does (it is the editor's
    /// new-plate slot). This is what MaterialLedger makes one hold per sheet from.
    /// </summary>
    public static int PhysicalSheetCount(Nest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var plates = nest.Plates;
        var count = plates.Count;
        if (count > 0 && plates[count - 1] is { } last
            && last.Parts.All(part => part.BaseDrawing.IsCutOff) && last.CutOffs.Count == 0)
            count--;

        var sheets = 0;
        for (var i = 0; i < count; i++)
            sheets += System.Math.Max(0, plates[i].Quantity);
        return sheets;
    }

    /// <summary>
    /// Summarizes <paramref name="result"/> against <paramref name="sheets"/>; null when the
    /// ledger is not configured, so callers show nothing.
    /// </summary>
    public static LedgerHoldSummary? From(LedgerHoldsResult result, int sheets)
    {
        ArgumentNullException.ThrowIfNull(result);
        sheets = System.Math.Max(0, sheets);
        switch (result)
        {
            case LedgerHoldsResult.NotConfigured:
                return null;
            case LedgerHoldsResult.Unauthorized:
                return new(LedgerHoldState.Unavailable, sheets, Array.Empty<LedgerHold>(),
                    "MaterialLedger rejected the API key. Check it in Options.");
            case LedgerHoldsResult.Unreachable unreachable:
                return new(LedgerHoldState.Unavailable, sheets, Array.Empty<LedgerHold>(), unreachable.Reason);
            case LedgerHoldsResult.Ok ok:
                var open = ok.Holds.Count(hold => LedgerHoldStatuses.IsOpen(hold.Status));
                var state = open == 0 ? LedgerHoldState.None
                    : open < sheets ? LedgerHoldState.Short
                    : LedgerHoldState.Held;
                return new(state, sheets, ok.Holds, null);
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, null);
        }
    }

    /// <summary>The status line, then one line per hold with its size in <paramref name="units"/>.</summary>
    public string ToolTip(Units units)
    {
        var text = new StringBuilder(Text);
        if (Reason != null)
            text.AppendLine().Append(Reason);
        foreach (var hold in Holds.Take(MaxTooltipHolds))
            text.AppendLine().Append(FormatHold(hold, units));
        if (Holds.Count > MaxTooltipHolds)
            text.AppendLine().Append($"+{Holds.Count - MaxTooltipHolds} more");
        return text.ToString();
    }

    /// <summary>One hold, e.g. "DROP-1182  30 x 52.5 in  B3  Approved".</summary>
    public static string FormatHold(LedgerHold hold, Units units)
    {
        ArgumentNullException.ThrowIfNull(hold);
        var parts = new List<string> { hold.ItemCode, FormatSize(hold.WidthIn, hold.LengthIn, units) };
        if (!string.IsNullOrWhiteSpace(hold.LocationCode))
            parts.Add(hold.LocationCode.Trim());
        parts.Add(DisplayStatus(hold.Status));
        return string.Join("  ", parts);
    }

    /// <summary>A ledger size (inches) in nest units, e.g. "30 x 52.5 in" or "762 x 1333.5 mm".</summary>
    public static string FormatSize(decimal widthIn, decimal lengthIn, Units units)
    {
        var scale = units == Units.Millimeters ? MillimetersPerInch : 1.0;
        var format = units == Units.Millimeters ? "0.#" : "0.###";
        return ((double)widthIn * scale).ToString(format, CultureInfo.CurrentCulture) + " x "
            + ((double)lengthIn * scale).ToString(format, CultureInfo.CurrentCulture) + " "
            + UnitsHelper.GetShortString(units);
    }

    /// <summary>An ML status name spaced for display ("AwaitingCloseout" → "Awaiting Closeout").</summary>
    public static string DisplayStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "(no status)" : Regex.Replace(status.Trim(), "(?<=[a-z])(?=[A-Z])", " ");
}
