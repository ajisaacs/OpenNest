using System;
using System.Collections.Generic;
using OpenNest.IO;

namespace OpenNest.Desktop;

/// <summary>
/// Which files dropped on the main window open as nests, as in the WinForms app: only paths
/// with the .nest extension (in any case); everything else is ignored.
/// </summary>
public static class DroppedNestFiles
{
    /// <summary>Whether the drop holds a nest file, so the window accepts it.</summary>
    public static bool Any(IEnumerable<string> paths) => Select(paths).Count > 0;

    /// <summary>
    /// The dropped nest files to open, in the order dropped, each once (a file dropped twice,
    /// however its path is written, opens one tab).
    /// </summary>
    public static IReadOnlyList<string> Select(IEnumerable<string> paths)
    {
        var nests = new List<string>();
        if (paths == null)
            return nests;
        var seen = new HashSet<string>(NestTabs.PathComparer);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsNestFile(path))
                continue;
            var fullPath = System.IO.Path.GetFullPath(path);
            if (seen.Add(fullPath))
                nests.Add(fullPath);
        }
        return nests;
    }

    private static bool IsNestFile(string path) => string.Equals(
        System.IO.Path.GetExtension(path), NestFormat.FileExtension,
        StringComparison.OrdinalIgnoreCase);
}
