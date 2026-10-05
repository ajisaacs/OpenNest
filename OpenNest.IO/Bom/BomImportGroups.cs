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

    /// <summary>Sum of the rows' quantities; a blank quantity counts as 0.</summary>
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
    /// Returns the groups of editable rows that have a drawing, a material
    /// and a thickness. Material is compared case-insensitively. Groups are
    /// ordered by material, then thickness.
    /// </summary>
    public static List<BomImportGroup> Build(IEnumerable<BomPartRow> rows)
    {
        return rows.Where(p =>
                p.IsEditable
                && !string.IsNullOrWhiteSpace(p.Material)
                && p.Thickness.HasValue
                && !string.IsNullOrWhiteSpace(p.DxfPath)
            )
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
