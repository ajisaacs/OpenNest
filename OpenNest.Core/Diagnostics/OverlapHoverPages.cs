using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenNest.Diagnostics;

/// <summary>
/// Viewport-sized pages of cached overlap details, including continuation pages for a
/// single long pair. The caller supplies its actual single-line font measurement.
/// No names or numeric details are elided, and no geometry is queried here.
/// </summary>
public sealed class OverlapHoverPages
{
    private readonly IReadOnlyList<string>[] pages;
    private readonly IReadOnlyList<string>[] navigation;

    private OverlapHoverPages(IReadOnlyList<string>[] pages, IReadOnlyList<string>[] navigation,
        bool needsLargerViewport = false)
    {
        this.pages = pages;
        this.navigation = navigation;
        NeedsLargerViewport = needsLargerViewport;
    }

    public IReadOnlyList<string> Lines => PageCount == 0 ? Array.Empty<string>() : pages[PageIndex];
    public IReadOnlyList<string> NavigationLines => PageCount == 0 ? Array.Empty<string>() : navigation[PageIndex];
    public int PageIndex { get; private set; }
    public int PageCount => pages.Length;
    public bool NeedsLargerViewport { get; }

    public void MovePage(int delta) => PageIndex = (int)System.Math.Clamp((long)PageIndex + delta, 0,
        System.Math.Max(0, PageCount - 1));

    public static OverlapHoverPages Create(string text, int pairCount, int maxRows,
        double maxWidth, Func<string, double> measure)
    {
        var tooSmall = new OverlapHoverPages([], [], true);
        if (maxRows < 1 || !double.IsFinite(maxWidth) || maxWidth <= 0)
            return tooSmall;
        var lines = Wrap(text, maxWidth, measure);
        if (lines == null)
            return tooSmall;
        if (lines.Count <= maxRows)
            return new OverlapHoverPages([lines.AsReadOnly()], [Array.Empty<string>()]);

        // Reserve the real, wrapped hint as well as content. Increasing the reserved
        // rows can increase the page count/digit count, so converge before slicing.
        for (var hintRows = 2; hintRows < maxRows;)
        {
            var contentRows = maxRows - hintRows;
            var count = (lines.Count - 1) / contentRows + 1;
            var hints = new IReadOnlyList<string>[count];
            var requiredHintRows = hintRows;
            for (var page = 0; page < count; page++)
            {
                var hint = Wrap($"Page {page + 1}/{count} · {pairCount} pairs\nPgUp/PgDn", maxWidth, measure);
                if (hint == null)
                    return tooSmall;
                hints[page] = hint.AsReadOnly();
                requiredHintRows = System.Math.Max(requiredHintRows, hint.Count);
            }
            if (requiredHintRows > hintRows)
            {
                hintRows = requiredHintRows;
                continue;
            }
            var pages = Enumerable.Range(0, count)
                .Select(page => (IReadOnlyList<string>)Array.AsReadOnly(lines.Skip(page * contentRows).Take(contentRows).ToArray()))
                .ToArray();
            return new OverlapHoverPages(pages, hints);
        }
        // There must be room for at least one complete content line AND navigation.
        return tooSmall;
    }

    private static List<string> Wrap(string text, double width, Func<string, double> measure)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add("");
                continue;
            }
            var starts = StringInfo.ParseCombiningCharacters(paragraph).Append(paragraph.Length).ToArray();
            for (var first = 0; first < starts.Length - 1;)
            {
                var low = first;
                var high = starts.Length - 1;
                while (low < high)
                {
                    var end = low + (high - low + 1) / 2;
                    if (measure(paragraph[starts[first]..starts[end]]) <= width)
                        low = end;
                    else
                        high = end - 1;
                }
                if (low == first)
                    return null; // Even one grapheme cannot fit; do not silently clip it.
                var last = low;
                if (last < starts.Length - 1)
                {
                    for (var end = last; end > first; end--)
                    {
                        if (char.IsWhiteSpace(paragraph[starts[end - 1]]))
                        {
                            last = end;
                            break;
                        }
                    }
                }
                lines.Add(paragraph[starts[first]..starts[last]]);
                first = last;
            }
        }
        return lines;
    }
}
