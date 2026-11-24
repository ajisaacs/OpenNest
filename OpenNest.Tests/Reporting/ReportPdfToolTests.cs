namespace OpenNest.Tests.Reporting;

/// <summary>
/// The report tests need a pdftotext with -bbox. PATH can list one without it first (Git for
/// Windows ships Xpdf's), so the search must pass over it.
/// </summary>
public class ReportPdfToolTests
{
    private const string XpdfHelp = """
        pdftotext version 4.00
        Usage: pdftotext [options] <PDF-file> [<text-file>]
          -f <int>             : first page to convert
          -layout              : maintain original physical layout
        """;

    private const string PopplerHelp = """
        pdftotext version 25.07.0
        Usage: pdftotext [options] <PDF-file> [<text-file>]
          -f <int>             : first page to convert
          -bbox                : output bounding box for each word and page size to html. Sets -htmlmeta
          -bbox-layout         : like -bbox but with extra layout bounding box data.  Sets -htmlmeta
        """;

    [Fact]
    public void FirstWithBbox_PassesOverAToolWithoutTheOption()
    {
        var help = new Dictionary<string, string>
        {
            ["git/pdftotext"] = XpdfHelp,
            ["broken/pdftotext"] = string.Empty,
            ["poppler/pdftotext"] = PopplerHelp,
            ["later/pdftotext"] = PopplerHelp,
        };

        Assert.Equal("poppler/pdftotext", ReportPdf.FirstWithBbox(help.Keys, tool => help[tool]));
    }

    [Fact]
    public void FirstWithBbox_IsNullWhenNoToolHasTheOption()
    {
        // -bbox-layout alone is not the option the tests pass.
        var onlyLayout = "  -bbox-layout         : like -bbox but with extra layout bounding box data.";

        Assert.Null(ReportPdf.FirstWithBbox(new[] { "a", "b" }, tool => tool == "a" ? XpdfHelp : onlyLayout));
        Assert.Null(ReportPdf.FirstWithBbox(Array.Empty<string>(), _ => PopplerHelp));
    }
}
