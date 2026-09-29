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
            Assert.Equal((612, 792), ReportPdf.EffectiveSize(pdf.Pages[0]));
            Assert.Equal((792, 612), ReportPdf.EffectiveSize(pdf.Pages[1]));
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
        var summary = ReportPdf.Text(path, 1);
        var plate = ReportPdf.Text(path, 2);

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
        Assert.Equal(summary + plate, ReportPdf.Text(WriteReport(NestReportTestData.CreateNest(), "again.pdf"), 1)
            + ReportPdf.Text(Path.Combine(directory, "again.pdf"), 2));
    }

    [SkippableFact]
    public void Write_EmptyAndDemandOnlyJobsProduceExplicitSummaryOnlyReports()
    {
        var nest = new Nest("Empty test") { Units = Units.Millimeters };
        var empty = ReportPdf.Text(WriteReport(nest, "empty.pdf"), 1);
        Assert.Contains("No plates in this job.", empty);
        Assert.Contains("No parts in this job.", empty);
        Assert.Contains("Page 1 of 1", empty);

        nest.Drawings.Add(NestReportTestData.Rectangle("Alpha", 20, 10, 2));
        var demand = ReportPdf.Text(WriteReport(nest, "demand.pdf"), 1);
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
        // The drawing area's clip path ("W* n") precedes the geometry.
        var diagram = ReportPdf.ContentStreams(pdf.Pages[1]).Last();
        diagram = diagram[(diagram.IndexOf("W* n", StringComparison.Ordinal) + 4)..];
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
    [InlineData("illegible-label", "Plate 4, part 2 (R004)")]
    [InlineData("cell-overflow", "Drawing R002")]
    [InlineData("header-overflow", "Page header")]
    public void Write_UnsupportedLayoutFailsBeforeReplacingDestination(string scenario, string identified)
    {
        var nest = NestReportTestData.CreateMultiPlateNest();
        switch (scenario)
        {
            case "illegible-label":
                // Too small for a legible label even in a detail view, on the LAST plate.
                nest.Plates[3].Parts.Add(new Part(NestReportTestData.Rectangle("Tiny", 0.01, 0.01), new Vector(44, 20)));
                break;
            case "cell-overflow":
                // A row taller than a page would be silently clipped by MigraDoc.
                nest.Plates[0].Parts[2].BaseDrawing.Name = string.Concat(Enumerable.Repeat("0123456789", 60));
                break;
            case "header-overflow":
                nest.Name = string.Join(" ", Enumerable.Repeat("Very long nest name", 40));
                break;
        }
        var path = Path.Combine(directory, "keep.report.pdf");
        File.WriteAllText(path, "keep");
        var before = NestReportBuilderTests.Fingerprint(nest);
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

        var error = Assert.Throws<NotSupportedException>(() => NestPdfWriter.Write(snapshot, path));

        Assert.Contains(identified, error.Message);
        Assert.Contains("support", error.Message);
        Assert.Equal("keep", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
        Assert.Equal(before, NestReportBuilderTests.Fingerprint(nest));
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

    internal sealed class FailingStream(Stream inner, long limit) : Stream
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
