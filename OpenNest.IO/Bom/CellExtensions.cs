using ClosedXML.Excel;

namespace OpenNest.IO.Bom
{
    public static class CellExtensions
    {
        public static int? ToIntOrNull(this IXLCell cell)
        {
            if (cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.Number) return (int)cell.GetDouble();
            if (int.TryParse(cell.GetString(), out var i)) return i;
            return null;
        }

        public static double? ToDoubleOrNull(this IXLCell cell)
        {
            if (cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.Number) return cell.GetDouble();
            if (double.TryParse(cell.GetString(), out var result)) return result;
            return null;
        }
    }
}
