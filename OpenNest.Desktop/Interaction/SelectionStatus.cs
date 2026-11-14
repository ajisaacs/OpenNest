using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Interaction;

/// <summary>The status bar's selection text, worded as the WinForms main window words it.</summary>
public static class SelectionStatus
{
    /// <summary>
    /// "Selected: [x, y] w x l" for one part, "Selected (n): [x, y] w x l" around several, or an
    /// empty string when no part is selected. Numbers are in the current culture, to four places.
    /// </summary>
    public static string Text(IReadOnlyCollection<Part> parts)
    {
        if (parts == null || parts.Count == 0)
            return string.Empty;
        var box = parts.Count == 1 ? parts.First().BoundingBox : parts.ToList().GetBoundingBox();
        var where = $"[{box.X:n4}, {box.Y:n4}] {box.Width:n4} x {box.Length:n4}";
        return parts.Count == 1 ? $"Selected: {where}" : $"Selected ({parts.Count}): {where}";
    }
}
