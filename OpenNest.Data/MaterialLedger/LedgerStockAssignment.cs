namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// Gives the plates Auto Nest committed on ledger stock their item codes. Runs only after the
/// proposal was validated, kept and committed, so a rejected or canceled one never changes
/// codes. Codes are hand-off data for MaterialLedger holds, not inventory accounting.
/// </summary>
public static class LedgerStockAssignment
{
    // ClampRemovalRecovery solves a sheet with clamps removed as "{stock id}::removed:{ids}".
    private const string VariantSeparator = "::";

    /// <param name="nestPlates">The nest's plates; codes are dealt out in this order.</param>
    /// <param name="committed">Each plate the commit attached, with the engine stock id it was solved on.</param>
    /// <param name="rows">The ledger rows offered to the run.</param>
    /// <remarks>
    /// Each row's codes are a queue: a plate takes the next <see cref="Plate.Quantity"/> codes of
    /// its row (a bundle repeats its one code). A plate whose row runs out keeps the rest of its
    /// sheets uncoded. A committed plate on hand-typed stock has no codes.
    /// </remarks>
    public static void Assign(IList<Plate> nestPlates, IEnumerable<(Plate Plate, string StockId)> committed,
        IEnumerable<LedgerStockRow> rows)
    {
        ArgumentNullException.ThrowIfNull(nestPlates);
        ArgumentNullException.ThrowIfNull(committed);
        ArgumentNullException.ThrowIfNull(rows);
        var queues = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
        foreach (var row in rows)
            queues.TryAdd(row.Id, new Queue<string>(row.ItemCodes));

        foreach (var (plate, stockId) in committed.OrderBy(c => IndexOf(nestPlates, c.Plate)))
        {
            var codes = new List<string>();
            if (stockId != null && queues.TryGetValue(BaseStockId(stockId), out var queue))
            {
                while (codes.Count < plate.Quantity && queue.Count > 0)
                    codes.Add(queue.Dequeue());
            }
            plate.StockItemCodes = codes;
        }
    }

    /// <summary>The offered stock id a solved sheet came from, without a clamp-removal suffix.</summary>
    public static string BaseStockId(string stockId)
    {
        ArgumentNullException.ThrowIfNull(stockId);
        var separator = stockId.IndexOf(VariantSeparator, StringComparison.Ordinal);
        return separator < 0 ? stockId : stockId[..separator];
    }

    /// <summary>
    /// "Stock: DROP-1182 (B3)" for display, with runs of one code counted ("SHEET-456 (A1) x3")
    /// and uncoded sheets noted; null when no sheet is coded. Locations are display-only.
    /// </summary>
    public static string? Describe(Plate plate, IReadOnlyDictionary<string, string>? locations)
    {
        ArgumentNullException.ThrowIfNull(plate);
        var codes = plate.StockItemCodes;
        if (codes.Count == 0)
            return null;
        var parts = new List<string>();
        for (var i = 0; i < codes.Count;)
        {
            var run = 1;
            while (i + run < codes.Count && codes[i + run] == codes[i])
                run++;
            var text = locations != null && locations.TryGetValue(codes[i], out var location)
                ? $"{codes[i]} ({location})"
                : codes[i];
            parts.Add(run > 1 ? $"{text} x{run}" : text);
            i += run;
        }
        var uncoded = plate.Quantity - codes.Count;
        if (uncoded > 0)
            parts.Add($"{uncoded} uncoded");
        return "Stock: " + string.Join(", ", parts);
    }

    private static int IndexOf(IList<Plate> plates, Plate plate)
    {
        var index = plates.IndexOf(plate);
        return index < 0 ? int.MaxValue : index;
    }
}
