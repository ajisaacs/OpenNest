using System.Collections.Generic;

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
    /// whether or not its material and thickness are filled in.
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
                    Qty = item.Qty,
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
}
