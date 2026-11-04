using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;

namespace OpenNest.Tests.Reporting;

/// <summary>PDF inspection for report tests: PDFsharp for structure, Poppler for text.</summary>
public static class ReportPdf
{
    public static (int, int) EffectiveSize(PdfPage page)
    {
        var box = page.MediaBox;
        var width = (int)System.Math.Round(box.Width);
        var height = (int)System.Math.Round(box.Height);
        return page.Rotate % 180 == 0 ? (width, height) : (height, width);
    }

    public static IEnumerable<string> ContentStreams(PdfPage page)
    {
        foreach (var item in page.Contents.Elements)
        {
            var dictionary = (item as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfDictionary
                ?? item as PdfDictionary;
            if (dictionary?.Stream != null)
                yield return Encoding.Latin1.GetString(dictionary.Stream.UnfilteredValue);
        }
    }

    /// <summary>Visual-row text of one page; bbox coordinates avoid platform-specific -layout order.</summary>
    public static string Text(string path, int page) =>
        LayoutPage(Run("pdftotext", "-bbox", "-f", Page(page), "-l", Page(page), path, "-"));

    /// <summary>Every page's visual-row text, in order.</summary>
    public static string[] Pages(string path)
    {
        var html = Run("pdftotext", "-bbox", path, "-");
        return Regex.Matches(html, @"<page\b[^>]*>.*?</page>", RegexOptions.Singleline)
            .Select(match => LayoutPage(match.Value)).ToArray();
    }

    private static string LayoutPage(string html)
    {
        var rows = new List<(double Top, List<(double Left, string Text)> Words)>();
        foreach (var word in ParseWords(html).OrderBy(word => word.Top).ThenBy(word => word.Left))
        {
            if (rows.Count == 0 || System.Math.Abs(word.Top - rows[^1].Top) > 1.5)
                rows.Add((word.Top, []));
            rows[^1].Words.Add((word.Left, word.Text));
        }
        return string.Join("\n", rows.Select(row => string.Join(" ", row.Words.OrderBy(word => word.Left).Select(word => word.Text)))) + "\n";
    }

    /// <summary>Summary-page card grid geometry, as laid out by <c>NestPdfWriter</c>.</summary>
    public const double CardLeft = 36, CardWidth = 129, CardGap = 8;
    public const int CardsPerRow = 4;

    /// <summary>
    /// Each summary part card's text, lines joined by '\n', in reading order (page, row, column).
    /// A card ends with its "Plates:" line; header, footer and the weight note are excluded.
    /// </summary>
    public static List<string> PartCards(string path)
    {
        var html = Run("pdftotext", "-bbox", path, "-");
        var cards = new List<(int Page, double Top, int Column, string Text)>();
        var started = false;
        var pages = Regex.Matches(html, @"<page\b[^>]*>.*?</page>", RegexOptions.Singleline).Select(match => match.Value).ToList();
        for (var page = 0; page < pages.Count; page++)
        {
            if (LayoutPage(pages[page]).Contains("Plate 1 of "))
                break;
            var words = ParseWords(pages[page]);
            var rows = Rows(words);
            // Cards start below the "Parts" heading; long notes can push it past the first page.
            var parts = rows.FindIndex(row => Join(row.Words) == "Parts");
            if (!started && parts < 0)
                continue;
            started = true;
            var top = parts >= 0 ? rows[parts].Top : 0;
            var excluded = rows.Where(row => Join(row.Words) is var text
                    && (text.StartsWith("Weights need", StringComparison.Ordinal)
                        || text.StartsWith("Nest report:", StringComparison.Ordinal)
                        || Regex.IsMatch(text, @"^Page \d+ of \d+$")))
                .Select(row => row.Top).ToList();
            for (var column = 0; column < CardsPerRow; column++)
            {
                var left = CardLeft + column * (CardWidth + CardGap);
                var inside = words.Where(word => word.Left >= left - 1 && word.Right <= left + CardWidth + 1
                    && word.Top > top + 1
                    && !excluded.Any(row => System.Math.Abs(row - word.Top) <= 1.5)).ToList();
                var lines = new List<string>();
                var start = 0.0;
                foreach (var row in Rows(inside))
                {
                    if (lines.Count == 0)
                        start = row.Top;
                    var text = Join(row.Words);
                    lines.Add(text);
                    if (text.StartsWith("Plates:", StringComparison.Ordinal))
                    {
                        cards.Add((page, start, column, string.Join("\n", lines)));
                        lines.Clear();
                    }
                }
                Assert.Empty(lines);
            }
        }
        return cards.OrderBy(card => card.Page).ThenBy(card => System.Math.Round(card.Top)).ThenBy(card => card.Column)
            .Select(card => card.Text).ToList();
    }

    private static List<(double Top, List<(double Left, double Top, double Right, double Bottom, string Text)> Words)> Rows(
        IEnumerable<(double Left, double Top, double Right, double Bottom, string Text)> words)
    {
        var rows = new List<(double Top, List<(double Left, double Top, double Right, double Bottom, string Text)> Words)>();
        foreach (var word in words.OrderBy(word => word.Top).ThenBy(word => word.Left))
        {
            if (rows.Count == 0 || System.Math.Abs(word.Top - rows[^1].Top) > 1.5)
                rows.Add((word.Top, []));
            rows[^1].Words.Add(word);
        }
        return rows;
    }

    private static string Join(IEnumerable<(double Left, double Top, double Right, double Bottom, string Text)> words) =>
        string.Join(" ", words.OrderBy(word => word.Left).Select(word => word.Text));

    /// <summary>Words with page-space boxes (points, Y down).</summary>
    public static List<(double Left, double Top, double Right, double Bottom, string Text)> Words(string path, int page) =>
        ParseWords(Run("pdftotext", "-bbox", "-f", Page(page), "-l", Page(page), path, "-"));

    private static List<(double Left, double Top, double Right, double Bottom, string Text)> ParseWords(string xml)
    {
        return Regex.Matches(xml, "<word xMin=\"([\\d.]+)\" yMin=\"([\\d.]+)\" xMax=\"([\\d.]+)\" yMax=\"([\\d.]+)\">([^<]*)</word>")
            .Select(match => (Number(match.Groups[1].Value), Number(match.Groups[2].Value), Number(match.Groups[3].Value),
                Number(match.Groups[4].Value), System.Net.WebUtility.HtmlDecode(match.Groups[5].Value)))
            .ToList();
    }

    private static string Page(int page) => page.ToString(CultureInfo.InvariantCulture);

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    private static string Run(string tool, params string[] arguments)
    {
        var info = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(info)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Skip.If(true, $"{tool} (poppler-utils) is not installed.");
            throw;
        }
        using (process)
        {
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            return text;
        }
    }
}
