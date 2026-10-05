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
    /// Returns one row per BOM item, in BOM order, with the drawing file it
    /// matched in <paramref name="dxfFolder"/> and its import status.
    /// </summary>
    public static List<BomPartRow> Build(List<BomItem> items, string dxfFolder)
    {
        var analysis = BomAnalyzer.Analyze(items, dxfFolder);
        var matchedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in analysis.Groups)
            foreach (var part in group.Parts)
                if (part.DxfPath != null)
                    matchedPaths[LookupName(part.Item.FileName)] = part.DxfPath;

        var rows = new List<BomPartRow>();

        foreach (var item in items)
        {
            var row = new BomPartRow
            {
                ItemNum = item.ItemNum,
                FileName = item.FileName,
                Qty = item.Qty,
                Description = item.Description,
                Material = item.Material,
                Thickness = item.Thickness,
            };

            if (string.IsNullOrWhiteSpace(item.FileName))
            {
                row.Status = "Skipped";
                row.IsEditable = false;
            }
            else
            {
                if (matchedPaths.TryGetValue(LookupName(item.FileName), out var dxfPath))
                {
                    row.DxfPath = dxfPath;
                    row.Status = "Matched";
                    row.IsEditable = true;
                }
                else
                {
                    row.Status = "No DXF";
                    row.IsEditable = false;
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// The drawing name a BOM file name refers to: the name without a
    /// .dxf or .dwg extension, as <see cref="BomAnalyzer"/> matches it.
    /// </summary>
    private static string LookupName(string fileName)
    {
        fileName ??= "";
        if (
            fileName.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)
        )
            return Path.GetFileNameWithoutExtension(fileName);
        return fileName;
    }
}
