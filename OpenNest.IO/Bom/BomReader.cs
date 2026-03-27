using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenNest.IO.Bom
{
    public class BomReader : IDisposable
    {
        private readonly XLWorkbook workbook;
        private Dictionary<PropertyInfo, int> columnNameIndexDict;

        public BomReader(string file)
        {
            workbook = new XLWorkbook(file);
            columnNameIndexDict = new Dictionary<PropertyInfo, int>();
        }

        private IXLWorksheet GetPartsWorksheet()
        {
            if (!workbook.TryGetWorksheet("Parts", out var worksheet))
                throw new InvalidOperationException("BOM file does not contain a 'Parts' worksheet.");
            return worksheet;
        }

        private void FindColumnIndexes(IXLWorksheet worksheet)
        {
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            var properties = typeof(BomItem).GetProperties();

            foreach (var property in properties)
            {
                var column = property.GetCustomAttribute<ColumnAttribute>();

                if (column == null)
                    continue;

                var classColumnNames = column.Names.Select(n => n.ToUpper());

                for (var columnIndex = 1; columnIndex <= lastColumn; columnIndex++)
                {
                    var cell = worksheet.Cell(1, columnIndex);
                    if (cell.IsEmpty()) continue;

                    var excelColumnName = cell.GetString().ToUpper();
                    var isMatch = classColumnNames.Any(n => n == excelColumnName);

                    if (!isMatch)
                        continue;

                    columnNameIndexDict.Add(property, columnIndex);
                    break;
                }
            }
        }

        public List<BomItem> GetItems()
        {
            var worksheet = GetPartsWorksheet();

            FindColumnIndexes(worksheet);

            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
            var items = new List<BomItem>();

            for (var rowIndex = 2; rowIndex <= lastRow; rowIndex++)
            {
                var item = new BomItem();

                foreach (var dictItem in columnNameIndexDict)
                {
                    var property = dictItem.Key;
                    var excelColumnIndex = dictItem.Value;
                    var cell = worksheet.Cell(rowIndex, excelColumnIndex);
                    var type = property.PropertyType;

                    if (type == typeof(int?))
                        property.SetValue(item, cell.ToIntOrNull());
                    else if (type == typeof(string))
                        property.SetValue(item, cell.IsEmpty() ? null : cell.GetString());
                    else if (type == typeof(double?))
                        property.SetValue(item, cell.ToDoubleOrNull());
                    else
                        throw new NotImplementedException($"Unsupported property type: {type}");
                }

                items.Add(item);
            }

            return items;
        }

        public void Dispose()
        {
            workbook?.Dispose();
        }
    }
}
