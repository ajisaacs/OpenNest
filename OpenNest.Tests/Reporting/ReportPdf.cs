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
