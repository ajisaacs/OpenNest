using System.Collections.Generic;
using System.Linq;

namespace OpenNest.IO.Bom;

/// <summary>
/// The rows of one material and thickness; each group becomes one nest.
/// </summary>
public sealed class BomImportGroup
{
    public BomImportGroup(string material, double thickness, IReadOnlyList<BomPartRow> parts)
    {
        Material = material;
        Thickness = thickness;
        Parts = parts;
    }

    /// <summary>The material as the group's first row spells it.</summary>
    public string Material { get; }

    public double Thickness { get; }

    public IReadOnlyList<BomPartRow> Parts { get; }

    /// <summary>Sum of the rows' quantities.</summary>
    public int TotalQty => Parts.Sum(p => p.Qty ?? 0);

    public string Key => BomImportGroups.Key(Material, Thickness);
}

/// <summary>
/// Groups importable rows by material and thickness. The Groups tab and
/// Create Nests both use this, so they always agree.
/// </summary>
public static class BomImportGroups
{
    /// <summary>
    /// Returns the groups of <see cref="BomRowStatus.Ready"/> rows. Material
    /// is compared case-insensitively. Groups are ordered by material, then
    /// thickness.
    /// </summary>
    public static List<BomImportGroup> Build(IEnumerable<BomPartRow> rows)
    {
        return rows.Where(p => p.Status == BomRowStatus.Ready)
            .GroupBy(p => new
            {
                Material = p.Material.ToUpperInvariant(),
                Thickness = p.Thickness.Value,
            })
            .OrderBy(g => g.First().Material)
            .ThenBy(g => g.Key.Thickness)
            .Select(g => new BomImportGroup(g.First().Material, g.Key.Thickness, g.ToList()))
            .ToList();
    }

    /// <summary>
    /// The key that identifies a group's plate settings across regrouping.
    /// </summary>
    public static string Key(string material, double thickness) =>
        $"{material?.ToUpperInvariant()}|{thickness}";
}
