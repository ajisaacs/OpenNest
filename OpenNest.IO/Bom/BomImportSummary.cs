using System.Collections.Generic;
using System.Linq;

namespace OpenNest.IO.Bom;

/// <summary>The import dialog's one-line summary of the part rows.</summary>
public static class BomImportSummary
{
    /// <summary>
    /// For example "12 ready, 2 need a thickness, 1 no drawing found, 3 had
    /// no BOM quantity (1 used)".
    /// </summary>
    public static string Describe(IEnumerable<BomPartRow> rows)
    {
        var list = rows.ToList();
        var counts = list.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        int Count(BomRowStatus status) => counts.TryGetValue(status, out var n) ? n : 0;

        var parts = new List<string> { $"{Count(BomRowStatus.Ready)} ready" };
        Add(parts, Count(BomRowStatus.NeedsMaterial), "needs a material", "need a material");
        Add(parts, Count(BomRowStatus.NeedsThickness), "needs a thickness", "need a thickness");
        Add(parts, Count(BomRowStatus.NeedsQuantity), "needs a quantity", "need a quantity");
        Add(parts, Count(BomRowStatus.NoDrawing), "no drawing found", "no drawing found");
        Add(parts, Count(BomRowStatus.NoFileName), "no file name", "no file name");
        Add(
            parts,
            list.Count(r => r.IsEditable && r.QtyAssumed),
            "had no BOM quantity (1 used)",
            "had no BOM quantity (1 used)"
        );
        return string.Join(", ", parts);
    }

    private static void Add(List<string> parts, int count, string one, string many)
    {
        if (count > 0)
            parts.Add($"{count} {(count == 1 ? one : many)}");
    }
}
