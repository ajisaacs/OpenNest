namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// Takes item codes already on other open nests out of ledger stock rows, so two nests are
/// not planned on the same sheet before ML holds exist for either. Pure: rows are rebuilt,
/// never changed.
/// </summary>
public static class LedgerCodeFilter
{
    /// <summary>
    /// <paramref name="rows"/> without <paramref name="codesInUse"/>: every copy of a code
    /// leaves the row's <see cref="LedgerStockRow.ItemCodes"/> and
    /// <see cref="LedgerStockRow.ItemLocations"/>, its quantity drops by the copies removed, and
    /// a row left with none is dropped. Row ids and order are kept. <paramref name="removed"/>
    /// counts the sheets and drops taken out.
    /// </summary>
    public static IReadOnlyList<LedgerStockRow> Exclude(IEnumerable<LedgerStockRow> rows,
        IEnumerable<string> codesInUse, out int removed)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(codesInUse);
        var exclude = codesInUse as IReadOnlySet<string> ?? codesInUse.ToHashSet(StringComparer.Ordinal);
        removed = 0;
        var kept = new List<LedgerStockRow>();
        foreach (var row in rows)
        {
            if (row == null)
                continue;
            var codes = row.ItemCodes.Where(code => !exclude.Contains(code)).ToList();
            var dropped = row.ItemCodes.Count - codes.Count;
            if (dropped == 0)
            {
                kept.Add(row);
                continue;
            }
            removed += dropped;
            if (codes.Count == 0)
                continue;

            var itemLocations = row.ItemLocations
                .Where(pair => !exclude.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            // A location stays listed while an item left in the row is there, or when the row
            // never said which item it held.
            var locations = row.Locations
                .Where(location => itemLocations.ContainsValue(location)
                    || !row.ItemLocations.Values.Contains(location, StringComparer.Ordinal))
                .ToList();
            kept.Add(row with
            {
                Quantity = codes.Count,
                ItemCodes = codes,
                Locations = locations,
                ItemLocations = itemLocations,
            });
        }
        return kept;
    }

    /// <summary>
    /// <paramref name="mapping"/> with <paramref name="codesInUse"/> taken out of its rows and
    /// the sheets removed counted in <see cref="LedgerStockMapping.OnOtherNests"/>.
    /// </summary>
    public static LedgerStockMapping Exclude(LedgerStockMapping mapping, IEnumerable<string> codesInUse)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var rows = Exclude(mapping.Rows, codesInUse, out var removed);
        return mapping with { Rows = rows, OnOtherNests = mapping.OnOtherNests + removed };
    }
}
