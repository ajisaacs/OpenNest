using System;
using System.IO;

namespace OpenNest.IO
{
    /// <summary>The drawing file types <see cref="CadImporter"/> reads.</summary>
    public static class CadFormats
    {
        public const string OpenFileFilter =
            "CAD Files (*.dxf;*.dwg;*.svg)|*.dxf;*.dwg;*.svg|"
            + "DXF Files (*.dxf)|*.dxf|DWG Files (*.dwg)|*.dwg|SVG Files (*.svg)|*.svg";

        public static bool IsCadFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
        }
    }
}
