using OpenNest.CNC;
using OpenNest.Reporting;
using PdfSharp.Pdf.IO;

namespace OpenNest.Tests.Reporting;

public sealed class PartPrintoutTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-printout-" + Guid.NewGuid().ToString("N"));

    public PartPrintoutTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, true); } catch (IOException) { }
    }

    [Fact]
    public void Capture_DefaultsToEveryPartDrawingInNestOrder()
    {
        var snapshot = PartPrintoutBuilder.Capture(NestReportTestData.CreateNest(), null, NestReportTestData.GeneratedAt);

        // The cutoff drawing is never a part page.
        Assert.Equal(["Unplaced", "Rotated", "Bracket"], snapshot.Parts.Select(part => part.Name).ToArray());
        Assert.Equal("Steel A36", snapshot.Material);
        Assert.Equal(0.125, snapshot.Thickness);
        Assert.Equal("in", snapshot.Units);
    }

    [Fact]
    public void Capture_MeasuresTheDrawing()
    {
        var nest = NestReportTestData.CreateNest();
        var bracket = nest.Drawings.Single(drawing => drawing.Name == "Bracket");

        var part = Assert.Single(PartPrintoutBuilder.Capture(nest, [bracket], NestReportTestData.GeneratedAt).Parts);

        Assert.Equal(10, part.Width, 6);
        Assert.Equal(10, part.Length, 6);
        Assert.Equal(100 - System.Math.PI, part.Area, 6);
        Assert.Equal(40 + 2 * System.Math.PI, part.CutLength, 6);
        Assert.Equal(2, part.PierceCount);
        Assert.Equal(5, part.Quantity);
        Assert.Equal((100 - System.Math.PI) * 0.125 * 0.2836, part.Weight!.Value, 6);
        Assert.Equal("Test customer", part.Customer);
    }

    [Fact]
    public void Capture_ReportsYExtentAsWidthAndXExtentAsLength()
    {
        var nest = NestReportTestData.CreateNest();
        var rotated = nest.Drawings.Single(drawing => drawing.Name == "Rotated"); // 4 along X, 2 along Y

        var part = Assert.Single(PartPrintoutBuilder.Capture(nest, [rotated], NestReportTestData.GeneratedAt).Parts);

        Assert.Equal(2, part.Width, 6);
        Assert.Equal(4, part.Length, 6);
    }

    [Fact]
    public void CutMetrics_CountOnlyWhatTheHeadCuts()
    {
        var program = new Program();
        // 10 x 10 outside: one pierce, 40 cut.
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(0, 10));
        program.Codes.Add(new LinearMove(10, 10));
        program.Codes.Add(new LinearMove(10, 0));
        program.Codes.Add(new LinearMove(0, 0));
        // An etch mark is neither cut length nor a pierce.
        program.Codes.Add(new RapidMove(1, 1));
        program.Codes.Add(new LinearMove(3, 1) { Layer = LayerType.Scribe });
        // A tabbed 4 x 2 cutout: two cut pieces either side of 1-long tabs, so two pierces, 10 cut.
        program.Codes.Add(new RapidMove(3, 4));
        program.Codes.Add(new LinearMove(7, 4));
        program.Codes.Add(new LinearMove(7, 5));
        program.Codes.Add(new LinearMove(7, 6) { Layer = LayerType.Tab });
        program.Codes.Add(new LinearMove(3, 6));
        program.Codes.Add(new LinearMove(3, 5));
        program.Codes.Add(new LinearMove(3, 4) { Layer = LayerType.Tab });
        // A breakoff's break line is cut: one pierce, 2 cut.
        program.Codes.Add(new RapidMove(8, 2));
        program.Codes.Add(new LinearMove(8, 4) { Layer = LayerType.Break });

        var (length, pierces) = PartPrintoutBuilder.CutMetrics(program);

        Assert.Equal(40 + 10 + 2, length, 6);
        Assert.Equal(4, pierces);
    }

    [Fact]
    public void Write_MakesOneLetterPortraitPageAndBookmarkPerPart()
    {
        var nest = NestReportTestData.CreateNest();
        var path = Path.Combine(directory, "parts.pdf");

        PartPrintoutWriter.Write(PartPrintoutBuilder.Capture(nest, null, NestReportTestData.GeneratedAt), path);

        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        Assert.Equal(3, pdf.PageCount);
        foreach (var page in pdf.Pages)
            Assert.Equal((612, 792), ReportPdf.EffectiveSize(page));
        Assert.Equal(["Unplaced", "Rotated", "Bracket"], pdf.Outlines.Select(outline => outline.Title).ToArray());
    }

    [SkippableFact]
    public void Write_PageCarriesTheDetailsTable()
    {
        var nest = NestReportTestData.CreateNest();
        var bracket = nest.Drawings.Single(drawing => drawing.Name == "Bracket");
        var path = Path.Combine(directory, "bracket.pdf");

        PartPrintoutWriter.Write(PartPrintoutBuilder.Capture(nest, [bracket], NestReportTestData.GeneratedAt), path);

        var text = ReportPdf.Text(path, 1);
        Assert.Contains("Bracket", text);
        Assert.Contains("Test customer", text);
        Assert.Matches(@"DIMENSIONS\s+10\.00"" x 10\.00""\s+MATERIAL\s+Steel A36", text);
        Assert.Matches(@"THICKNESS\s+0\.125""\s+CUT LENGTH\s+46\.28""", text);
        Assert.Matches(@"PIERCE COUNT\s+2\s+WEIGHT\s+3\.43 lb", text);
        Assert.Matches(@"AREA\s+96\.86 sq in\s+QUANTITY\s+5", text);
        Assert.Contains("Generated 9/29/2026 12:00 PM", text);
    }

    [SkippableFact]
    public void Write_NamedThicknessLeadsTheDecimal()
    {
        var nest = NestReportTestData.CreateNest();
        var bracket = nest.Drawings.Single(drawing => drawing.Name == "Bracket");
        var path = Path.Combine(directory, "named.pdf");

        PartPrintoutWriter.Write(PartPrintoutBuilder.Capture(nest, [bracket], NestReportTestData.GeneratedAt)
            with
        { ThicknessName = "11 ga" }, path);

        Assert.Matches(@"THICKNESS\s+11 ga - 0\.125""\s+CUT LENGTH", ReportPdf.Text(path, 1));
    }

    [Fact]
    public void Write_RejectsAThicknessNameTheBundledFontCannotShow()
    {
        var nest = NestReportTestData.CreateNest();
        var printout = Path.Combine(directory, "bad-printout.pdf");
        var report = Path.Combine(directory, "bad-report.pdf");

        var printoutError = Assert.Throws<InvalidOperationException>(() => PartPrintoutWriter.Write(
            PartPrintoutBuilder.Capture(nest, null, NestReportTestData.GeneratedAt) with { ThicknessName = "1/4—plate" },
            printout));
        var reportError = Assert.Throws<InvalidOperationException>(() => NestPdfWriter.Write(
            NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt) with { ThicknessName = "1/4—plate" },
            report));

        Assert.Contains("Thickness name", printoutError.Message);
        Assert.Contains("Thickness name", reportError.Message);
        Assert.False(File.Exists(printout));
        Assert.False(File.Exists(report));
    }

    [Fact]
    public void Write_RejectsTextTheBundledFontCannotShow()
    {
        var nest = NestReportTestData.CreateNest();
        var drawing = nest.Drawings.Single(item => item.Name == "Rotated");
        drawing.Customer = "em—dash";
        var path = Path.Combine(directory, "bad.pdf");

        var error = Assert.Throws<InvalidOperationException>(() =>
            PartPrintoutWriter.Write(PartPrintoutBuilder.Capture(nest, [drawing], NestReportTestData.GeneratedAt), path));

        Assert.Contains("U+2014", error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Capture_RejectsAnEmptySelection()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            PartPrintoutBuilder.Capture(new Nest("empty"), null, NestReportTestData.GeneratedAt));
        Assert.Contains("no drawings", error.Message);
    }
}
