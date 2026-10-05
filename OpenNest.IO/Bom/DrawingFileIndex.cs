using System;
using System.Collections.Generic;
using System.IO;

namespace OpenNest.IO.Bom;

/// <summary>
/// The .dxf and .dwg files in one folder, looked up by the name a BOM uses.
/// A .dxf wins over a .dwg with the same name.
/// </summary>
internal sealed class DrawingFileIndex
{
    private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

    private DrawingFileIndex(string folder)
    {
        FolderExists = Directory.Exists(folder);
        if (!FolderExists)
            return;

        foreach (var file in Directory.GetFiles(folder, "*.dxf"))
            files[Path.GetFileNameWithoutExtension(file)] = file;
        foreach (var file in Directory.GetFiles(folder, "*.dwg"))
            files.TryAdd(Path.GetFileNameWithoutExtension(file), file);
    }

    public bool FolderExists { get; }

    public static DrawingFileIndex Load(string folder) => new(folder);

    /// <summary>
    /// The drawing file for a BOM file name, ignoring case and a .dxf or
    /// .dwg extension; null when there is none.
    /// </summary>
    public string Find(string bomFileName) =>
        files.TryGetValue(LookupName(bomFileName), out var path) ? path : null;

    /// <summary>The BOM file name without a .dxf or .dwg extension.</summary>
    public static string LookupName(string fileName)
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
