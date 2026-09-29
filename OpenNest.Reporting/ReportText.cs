using System.Globalization;
using System.Text;
using PdfSharp.Drawing;

namespace OpenNest.Reporting;

/// <summary>
/// Lossless pre-wrapping. MigraDoc lets an unbroken token overflow a table cell and silently
/// clips a row taller than the page, so cell text is wrapped here and bounded before layout.
/// </summary>
internal static class ReportText
{
    /// <summary>Line cap for one table cell; taller rows would be clipped by MigraDoc.</summary>
    internal const int MaxCellLines = 20;

    // Headless measurement only; PDF output uses the same bundled font metrics.
    private static readonly XGraphics Measure = XGraphics.CreateMeasureContext(
        new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
    private static readonly object Sync = new();

    internal static XSize Size(string text, XFont font)
    {
        lock (Sync)
            return Measure.MeasureString(text, font);
    }

    /// <summary>
    /// Splits explicit line breaks, then wraps at spaces, and breaks tokens wider than
    /// <paramref name="width"/> between characters. Only the space at a wrap point is consumed.
    /// </summary>
    internal static List<string> Wrap(string text, XFont font, double width)
    {
        // Leave slack so MigraDoc does not re-break an already fitted line.
        var limit = width - 1;
        var lines = new List<string>();
        foreach (var hard in text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ').Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in hard.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Size(candidate, font).Width <= limit)
                {
                    line.Clear().Append(candidate);
                    continue;
                }
                if (line.Length > 0)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                var rest = word;
                while (Size(rest, font).Width > limit)
                {
                    var take = FittingPrefix(rest, font, limit);
                    lines.Add(rest[..take]);
                    rest = rest[take..];
                }
                line.Append(rest);
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    /// <summary>Wraps table-cell text and rejects a row MigraDoc could not show completely.</summary>
    internal static List<string> Cell(string text, XFont font, double width, string field)
    {
        var lines = Wrap(text, font, width);
        if (lines.Count > MaxCellLines)
            throw new NotSupportedException($"{field}: text needs {lines.Count} lines in its table cell; this report supports at most {MaxCellLines}.");
        return lines;
    }

    /// <summary>Compresses ascending plate numbers losslessly, e.g. "1-3, 5".</summary>
    internal static string Ranges(IReadOnlyList<int> numbers)
    {
        if (numbers.Count == 0)
            return "-";
        var parts = new List<string>();
        var start = numbers[0];
        var previous = start;
        for (var index = 1; index <= numbers.Count; index++)
        {
            if (index < numbers.Count && numbers[index] == previous + 1)
            {
                previous = numbers[index];
                continue;
            }
            parts.Add(start == previous
                ? start.ToString(CultureInfo.InvariantCulture)
                : $"{start.ToString(CultureInfo.InvariantCulture)}-{previous.ToString(CultureInfo.InvariantCulture)}");
            if (index < numbers.Count)
                start = previous = numbers[index];
        }
        return string.Join(", ", parts);
    }

    /// <summary>Map-style row names: A..Z, AA, AB, ...</summary>
    internal static string RowName(int index)
    {
        var name = "";
        for (var value = index + 1; value > 0; value = (value - 1) / 26)
            name = (char)('A' + (value - 1) % 26) + name;
        return name;
    }

    private static int FittingPrefix(string text, XFont font, double limit)
    {
        // At least one character always advances, even if a single glyph is wider than the cell.
        int low = 1, high = text.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Size(text[..middle], font).Width <= limit)
                low = middle;
            else
                high = middle - 1;
        }
        return low;
    }
}
