namespace OpenNest.Data.MaterialLedger;

/// <summary>A plate's item code that is no longer the nest's to use, and why.</summary>
/// <param name="PlateNumber">The plate's 1-based position in the nest.</param>
public sealed record LedgerCodeConflict(int PlateNumber, string ItemCode, string Reason);

/// <summary>
/// The save and post re-check of a nest's <see cref="Plate.StockItemCodes"/>: finds codes the
/// ledger no longer has free for this nest or that other open nests are coded with, and takes
/// them off the plates when the programmer chooses to. Pure; the caller fetches the inputs.
/// </summary>
public static class LedgerCodeCheck
{
    /// <summary>True when any plate has a coded sheet.</summary>
    public static bool HasCodes(IEnumerable<Plate> plates) =>
        (plates ?? throw new ArgumentNullException(nameof(plates))).Any(plate => plate.StockItemCodes.Count > 0);

    /// <param name="plates">The nest's plates in order.</param>
    /// <param name="freeItems">The ledger's free stock for the nest's material, after every hold.</param>
    /// <param name="ownHolds">
    /// The ledger's holds for this nest: an open one keeps its sheet for this nest, so the
    /// sheet counts as available here although the ledger no longer lists it as free.
    /// </param>
    /// <param name="inUse">Other open nests' codes; an unknown answer checks the ledger alone.</param>
    /// <returns>One conflict per plate and code, in plate order then the plate's code order.</returns>
    public static IReadOnlyList<LedgerCodeConflict> Find(IList<Plate> plates, IEnumerable<LedgerStockItem> freeItems,
        IEnumerable<LedgerHold>? ownHolds, LedgerCodesInUse inUse)
    {
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(freeItems);
        ArgumentNullException.ThrowIfNull(inUse);

        var available = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in freeItems.Where(item => item != null && item.Quantity > 0))
            available[item.ItemCode] = available.GetValueOrDefault(item.ItemCode) + item.Quantity;
        foreach (var hold in (ownHolds ?? Array.Empty<LedgerHold>()).Where(hold => hold != null && LedgerHoldStatuses.IsOpen(hold.Status)))
            available[hold.ItemCode] = available.GetValueOrDefault(hold.ItemCode) + 1;

        // A bundle's code repeats once per sheet across every plate it is used on.
        var needed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var code in plates.SelectMany(plate => plate.StockItemCodes))
            needed[code] = needed.GetValueOrDefault(code) + 1;

        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, count) in needed)
        {
            var parts = new List<string>();
            var free = available.GetValueOrDefault(code);
            if (free == 0)
                parts.Add("not free in MaterialLedger");
            else if (free < count)
                parts.Add($"only {free} of the {count} sheets needed free in MaterialLedger");
            var nests = inUse.NestsUsing(code);
            if (nests.Count > 0)
                parts.Add((nests.Count == 1 ? "on nest " : "on nests ") + string.Join(", ", nests));
            if (parts.Count > 0)
                reasons[code] = Capitalize(string.Join("; ", parts));
        }

        var conflicts = new List<LedgerCodeConflict>();
        for (var i = 0; i < plates.Count; i++)
        {
            foreach (var code in plates[i].StockItemCodes.Distinct(StringComparer.Ordinal))
            {
                if (reasons.TryGetValue(code, out var reason))
                    conflicts.Add(new LedgerCodeConflict(i + 1, code, reason));
            }
        }
        return conflicts;
    }

    /// <summary>
    /// Takes every copy of <paramref name="codes"/> off the plates, keeping the layout and the
    /// other codes in order; the sheets left uncoded are matched by size in ML. Returns how
    /// many sheets lost their code.
    /// </summary>
    public static int Remove(IEnumerable<Plate> plates, IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(codes);
        var remove = codes.ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var plate in plates)
        {
            var kept = plate.StockItemCodes.Where(code => !remove.Contains(code)).ToList();
            if (kept.Count == plate.StockItemCodes.Count)
                continue;
            removed += plate.StockItemCodes.Count - kept.Count;
            plate.StockItemCodes = kept;
        }
        return removed;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
