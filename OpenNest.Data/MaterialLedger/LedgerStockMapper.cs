using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// One Auto Nest stock row built from ledger items. <see cref="Size"/> is in nest units with
/// Width the short side. <see cref="ItemCodes"/> lists one code per unit of
/// <see cref="Quantity"/> (a bundle repeats its code), in ledger order, which later steps assign.
/// </summary>
public sealed record LedgerStockRow(
    string Id,
    Size Size,
    int Quantity,
    bool IsDrop,
    bool IsIrregular,
    IReadOnlyList<string> ItemCodes,
    IReadOnlyList<string> Locations)
{
    /// <summary>Each item code's ledger location code, for display only.</summary>
    public IReadOnlyDictionary<string, string> ItemLocations { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The row's spec rank in ML's acceptance rule: 0 an exact mapping, 1 the preferred entry,
    /// higher a substitute. An older ledger sends none, which counts as 1.
    /// </summary>
    public int Rank { get; init; } = 1;

    /// <summary>The catalog grade and finish the row is cut from, e.g. "A1011 P&amp;O"; null when the ledger sends neither.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// The best rule rank among the specs ML matched at this thickness: the preferred material
    /// for this request. A rule may list sheet grades first, so at plate thickness its best match
    /// can be rank 3 and is still not a substitute.
    /// </summary>
    public int PreferredRank { get; init; } = 1;

    /// <summary>True for stock ML accepts in place of the preferred material (a rule rank above <see cref="PreferredRank"/>).</summary>
    public bool IsSubstitute => Rank > PreferredRank;
}

/// <summary>The rows offered to Auto Nest and the counts of ledger items left out.</summary>
public sealed record LedgerStockMapping(
    IReadOnlyList<LedgerStockRow> Rows,
    int SkippedUnknownShape,
    int SkippedTooSmall)
{
    public int SheetRows => Rows.Count(r => !r.IsDrop);
    public int Drops => Rows.Where(r => r.IsDrop).Sum(r => r.Quantity);
    public int SubstituteRows => Rows.Count(r => r.IsSubstitute);

    /// <summary>E.g. "Ledger: 3 sheet sizes, 7 drops, 2 substitute rows (2 skipped: 2 unknown shape)".</summary>
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
            if (SubstituteRows > 0)
                text += $", {Plural(SubstituteRows, "substitute row")}";
            return skipped.Count == 0
                ? text
                : $"{text} ({SkippedUnknownShape + SkippedTooSmall} skipped: {string.Join(", ", skipped)})";
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}

/// <summary>
/// Turns ledger items into Auto Nest stock rows: each bundle or full sheet becomes a row, and
/// drops of the same usable size and spec share a row. Rows run in rank order and keep the
/// ledger's order within a rank. The engine sees only the <c>ledger:{n}</c> ids, never ranks.
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

    /// <param name="items">Ledger items in ledger order (rank, then location code, then item code).</param>
    /// <param name="units">The nest's units; ledger sizes are always inches.</param>
    /// <param name="partEnvelopes">
    /// Bounding sizes of the parts to nest, in nest units. An item no part fits in either
    /// orientation is skipped; null or empty keeps every size.
    /// </param>
    /// <param name="specs">The response's specs, which name each row's grade and finish; null leaves rows unlabeled.</param>
    public static LedgerStockMapping Map(IEnumerable<LedgerStockItem> items, Units units,
        IReadOnlyCollection<Size>? partEnvelopes = null, IEnumerable<LedgerStockSpec>? specs = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var scale = units == Units.Millimeters ? MillimetersPerInch : 1.0;
        var unknown = 0;
        var tooSmall = 0;
        var specById = new Dictionary<long, LedgerStockSpec>();
        foreach (var spec in specs ?? Array.Empty<LedgerStockSpec>())
        {
            if (spec != null)
                specById.TryAdd(spec.MaterialSpecId, spec);
        }
        var entries = new List<Entry>();
        var drops = new Dictionary<(long Spec, decimal Width, decimal Length, bool Irregular), Entry>();

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
            specById.TryGetValue(item.MaterialSpecId, out var itemSpec);
            var rank = item.Rank ?? itemSpec?.Rank ?? 1;
            if (IsKind(item, LedgerItemKinds.Drop))
            {
                // ML stores sizes as decimal(10,3), so identical drops match after rounding. Drops
                // of different specs stay apart: they differ in grade, finish or rank.
                var key = (item.MaterialSpecId, System.Math.Round(shortIn, 3), System.Math.Round(longIn, 3), irregular);
                if (!drops.TryGetValue(key, out var group))
                {
                    group = new Entry(size, IsDrop: true, irregular, rank, Label(itemSpec));
                    drops.Add(key, group);
                    entries.Add(group);
                }
                group.Items.Add(item);
            }
            else
            {
                var sheet = new Entry(size, IsDrop: false, irregular, rank, Label(itemSpec));
                sheet.Items.Add(item);
                entries.Add(sheet);
            }
        }

        // ML labels substitutes against the best-ranked spec it matched, not against rank 1;
        // an exact mapping (rank 0) overrides the rule and never makes rule stock a substitute.
        var ruleRanks = specById.Values.Where(s => s.Rank >= 1).Select(s => s.Rank!.Value)
            .Concat(entries.Where(e => e.Rank >= 1).Select(e => e.Rank)).ToList();
        var preferredRank = ruleRanks.Count == 0 ? 1 : ruleRanks.Min();
        var rows = new List<LedgerStockRow>(entries.Count);
        // ML already sends rank order; the stable sort keeps ledger order within a rank.
        foreach (var entry in entries.OrderBy(e => e.Rank))
        {
            var ordered = entry.Items;
            var codes = ordered.SelectMany(i => Enumerable.Repeat(i.ItemCode, i.Quantity)).ToList();
            var locations = ordered.Select(i => i.LocationCode).Where(code => !string.IsNullOrEmpty(code))
                .Distinct(StringComparer.Ordinal).ToList();
            var itemLocations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in ordered)
            {
                if (!string.IsNullOrEmpty(item.LocationCode))
                    itemLocations.TryAdd(item.ItemCode, item.LocationCode);
            }
            rows.Add(new LedgerStockRow($"{IdPrefix}{rows.Count + 1}", entry.Size, codes.Count,
                entry.IsDrop, entry.IsIrregular, codes, locations)
            {
                ItemLocations = itemLocations,
                Rank = entry.Rank,
                PreferredRank = preferredRank,
                Label = entry.Label,
            });
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

    /// <summary>"A1011 P&amp;O" from the spec's grade and finish; null when it has neither.</summary>
    private static string? Label(LedgerStockSpec? spec)
    {
        var parts = new[] { spec?.Grade?.Trim(), spec?.Finish?.Trim() }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static bool IsKind(LedgerStockItem item, string kind) =>
        string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase);

    private static bool IsShape(LedgerStockItem item, string shape) =>
        string.Equals(item.Shape, shape, StringComparison.OrdinalIgnoreCase);

    private sealed record Entry(Size Size, bool IsDrop, bool IsIrregular, int Rank, string? Label)
    {
        public List<LedgerStockItem> Items { get; } = new();
    }
}
