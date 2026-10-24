using System;
using System.Collections.Generic;
using System.IO;

namespace OpenNest.IO.Bom;

/// <summary>
/// Builds the import dialog's part rows from the BOM items and the drawing
/// folder.
/// </summary>
public static class BomImportRows
{
    /// <summary>
    /// Returns one row per BOM item, in BOM order. Every row with a file
    /// name gets the drawing it matches in <paramref name="dxfFolder"/>,
    /// whether or not its material and thickness are filled in. A blank
    /// BOM quantity becomes 1, marked as assumed.
    /// </summary>
    public static List<BomPartRow> Build(List<BomItem> items, string dxfFolder)
    {
        var index = DrawingFileIndex.Load(dxfFolder);
        var rows = new List<BomPartRow>();

        foreach (var item in items)
        {
            rows.Add(
                new BomPartRow
                {
                    ItemNum = item.ItemNum,
                    FileName = item.FileName,
                    BomQty = item.Qty,
                    Qty = item.Qty ?? 1,
                    QtyAssumed = item.Qty == null,
                    Description = item.Description,
                    Material = item.Material,
                    Thickness = item.Thickness,
                    DxfPath = string.IsNullOrWhiteSpace(item.FileName)
                        ? null
                        : index.Find(item.FileName),
                }
            );
        }

        return rows;
    }

    /// <summary>
    /// The job a BOM file is for: its file name without the extension or a trailing
    /// " BOM" ("Frame A BOM.xlsx" is "Frame A").
    /// </summary>
    public static string JobName(string bomPath)
    {
        var name = Path.GetFileNameWithoutExtension(bomPath ?? "");
        if (name.EndsWith(" BOM", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4).TrimEnd();
        return name;
    }
}
