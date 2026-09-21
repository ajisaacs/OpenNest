using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace OpenNest.IO.Bom;

/// <summary>Strict demand reader for Parts sheets with Part Name and Qty Required columns.</summary>
public static class PartQuantityReader
{
    public static IReadOnlyDictionary<string, int> Read(string path)
    {
        using var workbook = new XLWorkbook(path);
        if (!workbook.TryGetWorksheet("Parts", out var sheet))
            throw new InvalidDataException("Workbook must contain a Parts worksheet.");
        var nameColumn = FindColumn(sheet, "Part Name");
        var quantityColumn = FindColumn(sheet, "Qty Required");
        var quantities = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var nameCell = sheet.Cell(row, nameColumn);
            var quantityCell = sheet.Cell(row, quantityColumn);
            if (nameCell.IsEmpty() && quantityCell.IsEmpty())
                continue;
            var name = nameCell.GetString();
            // Do not truncate fractional quantities, infer a default, or silently normalize identifiers.
            if (
                string.IsNullOrWhiteSpace(name)
                || !double.TryParse(
                    quantityCell.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number
                )
                || !double.IsFinite(number)
                || number < 0
                || number > int.MaxValue
                || number != System.Math.Truncate(number)
            )
                throw new InvalidDataException(
                    $"Invalid part name or nonnegative integer quantity at Parts row {row}."
                );
            if (!quantities.TryAdd(name, (int)number))
                throw new InvalidDataException($"Duplicate part name at Parts row {row}: {name}");
        }
        if (!quantities.Values.Any(q => q > 0))
            throw new InvalidDataException("Workbook contains no positive part demand.");
        return quantities;
    }

    private static int FindColumn(IXLWorksheet sheet, string name)
    {
        var matches = sheet.Row(1).CellsUsed().Where(c => c.GetString() == name).ToList();
        if (matches.Count != 1)
            throw new InvalidDataException($"Parts must contain exactly one '{name}' column.");
        return matches[0].Address.ColumnNumber;
    }
}
