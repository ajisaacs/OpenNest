using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenNest.IO.Bom;

/// <summary>
/// One drawing a BOM puts in a drawing set: a drawing file with one material and
/// thickness, its BOM rows' total quantity and its canonical library package.
/// </summary>
public sealed class BomSetDrawing
{
    internal BomSetDrawing(
        string name,
        string material,
        double thickness,
        long quantity,
        string dxfPath,
        IReadOnlyList<BomPartRow> rows,
        Drawing drawing,
        byte[] package
    )
    {
        Name = name;
        Material = material;
        Thickness = thickness;
        Quantity = quantity;
        DxfPath = dxfPath;
        Rows = rows;
        Drawing = drawing;
        Package = package;
    }

    /// <summary>The drawing file's name without its extension.</summary>
    public string Name { get; }

    /// <summary>The BOM material, trimmed; blank when the BOM has none.</summary>
    public string Material { get; }

    /// <summary>The BOM thickness, or 0 when the BOM has none.</summary>
    public double Thickness { get; }

    /// <summary>The total quantity of the rows that name this drawing.</summary>
    public long Quantity { get; }

    public string DxfPath { get; }

    /// <summary>The BOM rows this drawing came from, in BOM order.</summary>
    public IReadOnlyList<BomPartRow> Rows { get; }

    public Drawing Drawing { get; }

    /// <summary>The drawing as a canonical <see cref="DrawingPackage"/>.</summary>
    public byte[] Package { get; }

    /// <summary>True when the drawing still needs a material or thickness before it can be released.</summary>
    public bool NeedsDetails => Material.Length == 0 || Thickness <= 0;

    /// <summary>"PT01 (items 3, 7)", or just the name when the rows have no item numbers.</summary>
    public string Label
    {
        get
        {
            var items = Rows.Where(r => r.ItemNum != null).Select(r => r.ItemNum.Value.ToString()).ToList();
            return items.Count == 0
                ? Name
                : $"{Name} (item{(items.Count == 1 ? "" : "s")} {string.Join(", ", items)})";
        }
    }
}

/// <summary>A BOM row that does not reach the set, and why.</summary>
public sealed record BomSetSkip(BomPartRow Row, string Name, string Reason);

/// <summary>
/// The drawings a BOM puts in a drawing set and the rows it leaves out. Unlike
/// Create Nests, rows with no material or thickness are kept: the set editor and
/// Edit Drawing fix them before release.
/// </summary>
public sealed class BomSetDrawings
{
    private BomSetDrawings(IReadOnlyList<BomSetDrawing> drawings, IReadOnlyList<BomSetSkip> skipped)
    {
        Drawings = drawings;
        Skipped = skipped;
    }

    /// <summary>The drawings in the order their first row appears in the BOM.</summary>
    public IReadOnlyList<BomSetDrawing> Drawings { get; }

    public IReadOnlyList<BomSetSkip> Skipped { get; }

    /// <summary>
    /// Imports each drawing file named by <paramref name="rows"/> once and packages it in
    /// <paramref name="units"/>. Rows naming the same file with the same material
    /// (ignoring case) and thickness become one drawing needing their total quantity.
    /// Rows with no file name, no drawing file or a quantity below 1, and files that
    /// fail to import, are skipped with a reason.
    /// </summary>
    public static BomSetDrawings Build(IEnumerable<BomPartRow> rows, Units units)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var skipped = new List<BomSetSkip>();
        var kept = new List<BomPartRow>();
        foreach (var row in rows)
        {
            switch (row.Status)
            {
                case BomRowStatus.NoFileName:
                    skipped.Add(new BomSetSkip(row, RowName(row), "no file name"));
                    break;
                case BomRowStatus.NoDrawing:
                    skipped.Add(new BomSetSkip(row, RowName(row), "no drawing file found"));
                    break;
                case BomRowStatus.NeedsQuantity:
                    skipped.Add(new BomSetSkip(row, RowName(row), "quantity is below 1"));
                    break;
                default:
                    kept.Add(row);
                    break;
            }
        }

        var drawings = new List<BomSetDrawing>();
        var imported = new Dictionary<string, (Drawing Drawing, byte[] Package, string Error)>(
            StringComparer.OrdinalIgnoreCase
        );
        var groups = kept.GroupBy(r => (
            Path: r.DxfPath,
            Material: (r.Material ?? "").Trim().ToUpperInvariant(),
            Thickness: ThicknessOf(r)
        ));
        foreach (var group in groups)
        {
            var first = group.First();
            if (!imported.TryGetValue(first.DxfPath, out var file))
            {
                file = Import(first.DxfPath, units);
                imported[first.DxfPath] = file;
            }

            if (file.Error != null)
            {
                skipped.AddRange(group.Select(r => new BomSetSkip(r, RowName(r), file.Error)));
                continue;
            }

            drawings.Add(
                new BomSetDrawing(
                    Path.GetFileNameWithoutExtension(first.DxfPath),
                    (first.Material ?? "").Trim(),
                    group.Key.Thickness,
                    group.Sum(r => (long)(r.Qty ?? 1)),
                    first.DxfPath,
                    group.ToList(),
                    file.Drawing,
                    file.Package
                )
            );
        }

        return new BomSetDrawings(drawings, skipped);
    }

    private static (Drawing, byte[], string) Import(string path, Units units)
    {
        if (!File.Exists(path))
            return (null, null, "drawing file not found");
        try
        {
            var drawing = CadImporter.ImportDrawing(path);
            return (drawing, DrawingPackage.Write(drawing, units), null);
        }
        catch (Exception ex)
        {
            return (null, null, $"could not import the drawing: {ex.Message}");
        }
    }

    private static double ThicknessOf(BomPartRow row) =>
        row.Thickness is double t && double.IsFinite(t) && t > 0 ? t : 0;

    private static string RowName(BomPartRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.FileName))
            return row.FileName.Trim();
        if (row.ItemNum != null)
            return $"Item {row.ItemNum}";
        return string.IsNullOrWhiteSpace(row.Description) ? "(unnamed row)" : row.Description.Trim();
    }
}
