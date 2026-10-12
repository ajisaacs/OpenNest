using System;
using System.Collections.Generic;

namespace OpenNest.Engine.Jobs;

/// <summary>Pure limits for total physical copies, including the first, using pre-commit demand.
/// Callers must validate the selected layout before allowing more than one copy.</summary>
internal static class CompletedLayoutRepeat
{
    public static int Limit(
        IReadOnlyDictionary<string, int> remaining,
        IReadOnlyDictionary<string, int> layoutCounts,
        bool searchCompleted,
        int availableStockEntries,
        int? stockRemaining,
        int? physicalRemaining)
    {
        ArgumentNullException.ThrowIfNull(remaining);
        ArgumentNullException.ThrowIfNull(layoutCounts);
        if (availableStockEntries < 0 || stockRemaining < 0 || physicalRemaining < 0)
            throw new ArgumentOutOfRangeException(nameof(availableStockEntries), "Repeat bounds cannot be negative.");
        if (layoutCounts.Count == 0 || availableStockEntries == 0)
            return 0;

        var copies = int.MaxValue;
        foreach (var (id, count) in layoutCounts)
        {
            if (count <= 0 || !remaining.TryGetValue(id, out var demand) || demand < 0)
                throw new ArgumentException("Layout counts must be positive and reference exact remaining requirement IDs.", nameof(layoutCounts));
            copies = System.Math.Min(copies, demand / count);
        }
        if (stockRemaining is int stock)
            copies = System.Math.Min(copies, stock);
        if (physicalRemaining is int cap)
            copies = System.Math.Min(copies, cap);
        if (!searchCompleted || availableStockEntries != 1)
            copies = System.Math.Min(copies, 1);
        return copies;
    }
}

/// <summary>Immutable observation of a decode decision, not production accounting.</summary>
internal sealed record RepeatBatch(string StockId, int Copies, int AvailableStockEntries, int SheetsBefore, bool ForcedFirst);
