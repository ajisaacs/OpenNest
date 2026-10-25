using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// One Auto Nest stock row built from ledger items. <see cref="Size"/> is in nest units with
/// Width the short side. <see cref="ItemCodes"/> lists one code per unit of
/// <see cref="Quantity"/> (a bundle repeats its code), in the order later steps assign them.
/// </summary>
public sealed record LedgerStockRow(
    string Id,
    Size Size,
    int Quantity,
    bool IsDrop,
    bool IsIrregular,
    IReadOnlyList<string> ItemCodes,
    IReadOnlyList<string> Locations);

/// <summary>The rows offered to Auto Nest and the counts of ledger items left out.</summary>
public sealed record LedgerStockMapping(
    IReadOnlyList<LedgerStockRow> Rows,
    int SkippedUnknownShape,
    int SkippedTooSmall)
{
    public int SheetRows => Rows.Count(r => !r.IsDrop);
    public int Drops => Rows.Where(r => r.IsDrop).Sum(r => r.Quantity);

    /// <summary>E.g. "Ledger: 3 sheet sizes, 7 drops (2 skipped: unknown shape)".</summary>
    public string Summary
    {
        get
        {
            var skipped = new List<string>();
            if (SkippedUnknownShape > 0)
                skipped.Add($"{SkippedUnknownShape} unknown shape");
            if (SkippedTooSmall > 0)
                skipped.Add($"{SkippedTooSmall} too small for any part");
            var text = $"Ledger: {Plural(SheetRows, "sheet size")}, {Plural(Drops, "drop")}";
            return skipped.Count == 0
                ? text
                : $"{text} ({SkippedUnknownShape + SkippedTooSmall} skipped: {string.Join(", ", skipped)})";
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}

/// <summary>
/// Turns ledger items into Auto Nest stock rows: each bundle or full sheet becomes a row, and
/// drops of the same usable size share a row. The engine sees only the <c>ledger:{n}</c> ids.
/// </summary>
public static class LedgerStockMapper
{
    public const string IdPrefix = "ledger:";

    private const double MillimetersPerInch = 25.4;

    public static bool IsLedgerId(string? id) =>
        id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>The nest thickness in inches for the ledger query, rounded to 4 places.</summary>
    public static double ThicknessInches(double thickness, Units units) =>
        System.Math.Round(units == Units.Millimeters ? thickness / MillimetersPerInch : thickness, 4);

    /// <param name="items">Ledger items in ledger order.</param>
    /// <param name="units">The nest's units; ledger sizes are always inches.</param>
    /// <param name="partEnvelopes">
    /// Bounding sizes of the parts to nest, in nest units. An item no part fits in either
    /// orientation is skipped; null or empty keeps every size.
    /// </param>
    public static LedgerStockMapping Map(IEnumerable<LedgerStockItem> items, Units units,
        IReadOnlyCollection<Size>? partEnvelopes = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var scale = units == Units.Millimeters ? MillimetersPerInch : 1.0;
        var unknown = 0;
        var tooSmall = 0;
        var entries = new List<Entry>();
        var drops = new Dictionary<(decimal Width, decimal Length, bool Irregular), Entry>();

        foreach (var item in items)
        {
            if (item == null || item.Quantity <= 0)
                continue;
            if (!IsShape(item, LedgerItemShapes.Rectangular) && !IsShape(item, LedgerItemShapes.IrregularConservativeRectangle))
            {
                // Unknown, and any shape this build does not know, has no trustworthy usable size.
                unknown++;
                continue;
            }
            if (!IsKind(item, LedgerItemKinds.Drop) && !IsKind(item, LedgerItemKinds.FullSheet)
                && !IsKind(item, LedgerItemKinds.FullSheetBundle))
                continue;

            var shortIn = System.Math.Min(item.WidthIn, item.LengthIn);
            var longIn = System.Math.Max(item.WidthIn, item.LengthIn);
            if (shortIn <= 0)
                continue;
            var size = new Size((double)shortIn * scale, (double)longIn * scale);
            if (!AnyPartFits(size, partEnvelopes))
            {
                tooSmall += item.Quantity;
                continue;
            }

            var irregular = IsShape(item, LedgerItemShapes.IrregularConservativeRectangle);
            if (IsKind(item, LedgerItemKinds.Drop))
            {
                // ML stores sizes as decimal(10,3), so identical drops match after rounding.
                var key = (System.Math.Round(shortIn, 3), System.Math.Round(longIn, 3), irregular);
                if (!drops.TryGetValue(key, out var group))
                {
                    group = new Entry(size, IsDrop: true, irregular);
                    drops.Add(key, group);
                    entries.Add(group);
                }
                group.Items.Add(item);
            }
            else
            {
                var sheet = new Entry(size, IsDrop: false, irregular);
                sheet.Items.Add(item);
                entries.Add(sheet);
            }
        }

        var rows = new List<LedgerStockRow>(entries.Count);
        foreach (var entry in entries)
        {
            var ordered = entry.IsDrop
                ? entry.Items.OrderBy(i => i.LocationCode, StringComparer.Ordinal)
                    .ThenBy(i => i.ItemCode, StringComparer.Ordinal).ToList()
                : entry.Items;
            var codes = ordered.SelectMany(i => Enumerable.Repeat(i.ItemCode, i.Quantity)).ToList();
            var locations = ordered.Select(i => i.LocationCode).Where(code => !string.IsNullOrEmpty(code))
                .Distinct(StringComparer.Ordinal).ToList();
            rows.Add(new LedgerStockRow($"{IdPrefix}{rows.Count + 1}", entry.Size, codes.Count,
                entry.IsDrop, entry.IsIrregular, codes, locations));
        }
        return new LedgerStockMapping(rows, unknown, tooSmall);
    }

    private static bool AnyPartFits(Size size, IReadOnlyCollection<Size>? partEnvelopes)
    {
        if (partEnvelopes == null || partEnvelopes.Count == 0)
            return true;
        foreach (var part in partEnvelopes)
        {
            var partShort = System.Math.Min(part.Width, part.Length);
            var partLong = System.Math.Max(part.Width, part.Length);
            if (partShort <= size.Width + Tolerance.Epsilon && partLong <= size.Length + Tolerance.Epsilon)
                return true;
        }
        return false;
    }

    private static bool IsKind(LedgerStockItem item, string kind) =>
        string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase);

    private static bool IsShape(LedgerStockItem item, string shape) =>
        string.Equals(item.Shape, shape, StringComparison.OrdinalIgnoreCase);

    private sealed record Entry(Size Size, bool IsDrop, bool IsIrregular)
    {
        public List<LedgerStockItem> Items { get; } = new();
    }
}
