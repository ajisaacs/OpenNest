using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using OpenNest.Geometry;
using OpenNest.Reporting;
using PdfSharp.Pdf.IO;

namespace OpenNest.Tests.Reporting;

public sealed class NestPdfWriterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-pdf-" + Guid.NewGuid().ToString("N"));

    public NestPdfWriterTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Write_OnePlateJobProducesPortraitSummaryAndLandscapeVectorPlatePage()
    {
        var path = WriteReport(NestReportTestData.CreateNest());

        using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import))
        {
            Assert.Equal(2, pdf.PageCount);
            Assert.Equal((612, 792), EffectiveSize(pdf.Pages[0]));
            Assert.Equal((792, 612), EffectiveSize(pdf.Pages[1]));
        }
        var bytes = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        // Subset-embedded faces; PDF names escape the space as #20.
        Assert.Matches(@"/BaseFont\s*/[A-Z]{6}\+DejaVu#20Sans(?![,\w])", bytes);
        Assert.Matches(@"/BaseFont\s*/[A-Z]{6}\+DejaVu#20Sans,Bold\b", bytes);
        Assert.Contains("/FontFile2", bytes);
        // Thumbnails and diagrams are vectors, never raster screenshots.
        Assert.DoesNotMatch(@"/Subtype\s*/Image", bytes);
        Assert.Matches(@"/Subtype\s*/Form", bytes);
    }

    [SkippableFact]
    public void Write_PdfTextCarriesDocumentLocalIdsQuantitiesAndPageNumbers()
    {
        var path = WriteReport(NestReportTestData.CreateNest());
        var summary = ExtractText(path, 1);
        var plate = ExtractText(path, 2);

        Assert.Contains("Report test job", summary);
        Assert.Contains("Test customer", summary);
        Assert.Contains("Steel A36", summary);
        Assert.Contains("0.125 in", summary);
        Assert.Contains("2026-09-29 12:00 +00:00", summary);
        Assert.Matches(@"Distinct layouts:\s+1", summary);
        Assert.Matches(@"Total physical sheets:\s+2", summary);
        Assert.Matches(@"1\s+48 x 24 in\s+2\s+4\s+\d+\.\d%", summary);
        Assert.Matches(@"R001\s+Bracket\s+5\s+4\s+1\s+0\s+1", summary);
        Assert.Matches(@"R002\s+Rotated\s+1\s+2\s+0\s+1\s+1", summary);
        Assert.Matches(@"R003\s+Bracket\s+7\s+2\s+5\s+0\s+1", summary);
        Assert.Matches(@"R004\s+Unplaced\s+3\s+0\s+3\s+0\s+-", summary);
        Assert.Contains("Page 1 of 2", summary);

        Assert.Contains("Plate 1", plate);
        Assert.Contains("Report test job", plate);
        Assert.Matches(@"Copies:\s+2", plate);
        Assert.Matches(@"R001\s+Bracket\s+2\s+4", plate);
        Assert.Matches(@"R002\s+Rotated\s+1\s+2", plate);
        Assert.Matches(@"R003\s+Bracket\s+1\s+2", plate);
        Assert.DoesNotContain("R004", plate);
        Assert.DoesNotContain("Cutoff test", plate + summary);
        Assert.Contains("not a dimensioned cutting drawing", plate);
        Assert.Contains("not a geometry or CNC approval", plate);
        Assert.Contains("Page 2 of 2", plate);

        // Repeated export of the same snapshot is textually identical.
        Assert.Equal(summary + plate, ExtractText(WriteReport(NestReportTestData.CreateNest(), "again.pdf"), 1)
            + ExtractText(Path.Combine(directory, "again.pdf"), 2));
    }

    [SkippableFact]
    public void Write_EmptyAndDemandOnlyJobsProduceExplicitSummaryOnlyReports()
    {
        var nest = new Nest("Empty test") { Units = Units.Millimeters };
        var empty = ExtractText(WriteReport(nest, "empty.pdf"), 1);
        Assert.Contains("No plates in this job.", empty);
        Assert.Contains("No parts in this job.", empty);
        Assert.Contains("Page 1 of 1", empty);

        nest.Drawings.Add(NestReportTestData.Rectangle("Alpha", 20, 10, 2));
        var demand = ExtractText(WriteReport(nest, "demand.pdf"), 1);
        Assert.Contains("No plates in this job.", demand);
        Assert.Matches(@"R001\s+Alpha\s+2\s+0\s+2\s+0\s+-", demand);
        Assert.Contains("Page 1 of 1", demand);
    }

    [Fact]
    public void Write_TabbedLeadInPartStrokesEachOpenContourSeparately()
    {
        var nest = NestReportTestData.CreateTabbedNest();
        var geometry = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt).Plates[0].Parts[0].Geometry;
        Assert.True(geometry.StrokeOnly);
        var path = WriteReport(nest);
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Assert.Equal(2, pdf.PageCount);
        // The vector diagram is the last content stream on the plate page. Each stroke-only
        // contour must begin its own subpath; a missing move-to draws a false connecting line.
        var diagram = ContentStreams(pdf.Pages[1]).Last();
        Assert.Equal(geometry.Contours.Length, Regex.Matches(diagram, @"(?m)^[-\d. ]+ m$").Count);
    }

    [Fact]
    public void Write_LateFailureLeavesExistingReportUnchangedAndNoTemporaryFile()
    {
        var path = Path.Combine(directory, "existing.report.pdf");
        var original = "%PDF previous report"u8.ToArray();
        File.WriteAllBytes(path, original);
        var snapshot = NestReportBuilder.Capture(NestReportTestData.CreateNest(), NestReportTestData.GeneratedAt);

        Assert.Throws<IOException>(() => NestPdfWriter.Write(snapshot, path, stream => new FailingStream(stream, 512)));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }

    [Fact]
    public void Write_SuccessfulExportReplacesExistingReport()
    {
        var path = Path.Combine(directory, "existing.report.pdf");
        File.WriteAllText(path, "old");
        WriteReport(NestReportTestData.CreateNest(), "existing.report.pdf");
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 5));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("second-plate")]
    [InlineData("summary-overflow")]
    [InlineData("plate-table-overflow")]
    [InlineData("illegible-label")]
    public void Write_UnsupportedLayoutFailsBeforeReplacingDestination(string scenario)
    {
        var nest = NestReportTestData.CreateNest();
        var plate = nest.Plates[0];
        switch (scenario)
        {
            case "second-plate":
                nest.Plates.Add(new Plate(24, 48) { Quantity = 1 });
                nest.Plates[1].Parts.Add(new Part(plate.Parts[0].BaseDrawing));
                break;
            case "summary-overflow":
                for (var i = 0; i < 40; i++)
                    nest.Drawings.Add(NestReportTestData.Rectangle($"Demand {i:D2}", 1, 1));
                break;
            case "plate-table-overflow":
                for (var i = 0; i < 12; i++)
                    // Large enough for readable labels, so only the table can overflow.
                    plate.Parts.Add(new Part(NestReportTestData.Rectangle($"Small {i:D2}", 2, 2), new Vector(2 + i * 3, 16)));
                break;
            case "illegible-label":
                plate.Parts.Add(new Part(NestReportTestData.Rectangle("Tiny", 0.05, 0.05), new Vector(44, 20)));
                break;
        }
        var path = Path.Combine(directory, "keep.report.pdf");
        File.WriteAllText(path, "keep");
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

        var error = Assert.Throws<NotSupportedException>(() => NestPdfWriter.Write(snapshot, path));

        Assert.Contains("not supported", error.Message);
        Assert.Equal("keep", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }

    [Fact]
    public void Write_UnsupportedTextIsIdentifiedBeforeReplacingDestination()
    {
        var nest = NestReportTestData.CreateNest();
        nest.Plates[0].Parts[2].BaseDrawing.Name = "Rotated 中";
        var path = Path.Combine(directory, "keep.report.pdf");
        File.WriteAllText(path, "keep");
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

        var error = Assert.Throws<InvalidOperationException>(() => NestPdfWriter.Write(snapshot, path));

        Assert.Contains("R002", error.Message);
        Assert.Contains("U+4E2D", error.Message);
        Assert.Equal("keep", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private string WriteReport(Nest nest, string name = "test.report.pdf")
    {
        var path = Path.Combine(directory, name);
        NestPdfWriter.Write(NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt), path);
        return path;
    }

    private static (int, int) EffectiveSize(PdfSharp.Pdf.PdfPage page)
    {
        var box = page.MediaBox;
        var width = (int)System.Math.Round(box.Width);
        var height = (int)System.Math.Round(box.Height);
        return page.Rotate % 180 == 0 ? (width, height) : (height, width);
    }

    private static IEnumerable<string> ContentStreams(PdfSharp.Pdf.PdfPage page)
    {
        foreach (var item in page.Contents.Elements)
        {
            var dictionary = (item as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfSharp.Pdf.PdfDictionary
                ?? item as PdfSharp.Pdf.PdfDictionary;
            if (dictionary?.Stream != null)
                yield return Encoding.Latin1.GetString(dictionary.Stream.UnfilteredValue);
        }
    }

    /// <summary>Poppler text extraction; skipped where poppler-utils is not installed.</summary>
    private static string ExtractText(string path, int page)
    {
        var info = new ProcessStartInfo("pdftotext")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-layout", "-f", page.ToString(), "-l", page.ToString(), path, "-" })
            info.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(info)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Skip.If(true, "pdftotext (poppler-utils) is not installed.");
            throw;
        }
        using (process)
        {
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            return Regex.Replace(text, "[ \t]+\n", "\n");
        }
    }

    private sealed class FailingStream(Stream inner, long limit) : Stream
    {
        private long written;
        public override bool CanRead => false;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (written + count > limit)
                throw new IOException("injected late report write failure");
            written += count;
            inner.Write(buffer, offset, count);
        }
    }
}
