using System.Collections.Concurrent;
using System.Text;
using OpenNest.Reporting;
using PdfSharp.Pdf.IO;

namespace OpenNest.Tests.Reporting;

public sealed class NestPdfWriterConcurrencyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-pdf-concurrency-" + Guid.NewGuid().ToString("N"));

    public NestPdfWriterConcurrencyTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void Write_ConcurrentMultiPageAndDetailViewExportsMatchSequentialExports()
    {
        var snapshots = new[] { NestReportTestData.CreateMultiPlateNest(), NestReportTestData.CreateDenseNest() }
            .Select(nest => NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt)).ToArray();
        var expected = snapshots.Select((snapshot, index) =>
        {
            var path = Path.Combine(directory, $"reference-{index}.pdf");
            NestPdfWriter.Write(snapshot, path);
            return PageContent(path);
        }).ToArray();
        var mismatches = new ConcurrentBag<int>();

        Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = System.Math.Max(8, Environment.ProcessorCount) }, index =>
        {
            var path = Path.Combine(directory, $"multi-{index}.pdf");
            NestPdfWriter.Write(snapshots[index % 2], path);
            if (PageContent(path) != expected[index % 2])
                mismatches.Add(index);
        });

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Write_ConcurrentExportsProduceTheSameLayoutAsASequentialExport()
    {
        // PDFsharp/MigraDoc layout shares process-wide font state; unsynchronized concurrent
        // exports produced shifted/merged words (for example "Total physicalsheets").
        var snapshot = NestReportBuilder.Capture(NestReportTestData.CreateNest(), NestReportTestData.GeneratedAt);
        var reference = Path.Combine(directory, "reference.pdf");
        NestPdfWriter.Write(snapshot, reference);
        var expected = PageContent(reference);
        var mismatches = new ConcurrentBag<int>();

        Parallel.For(0, 128, new ParallelOptions { MaxDegreeOfParallelism = System.Math.Max(8, Environment.ProcessorCount) }, index =>
        {
            var path = Path.Combine(directory, $"export-{index}.pdf");
            NestPdfWriter.Write(snapshot, path);
            if (PageContent(path) != expected)
                mismatches.Add(index);
        });

        Assert.Empty(mismatches);
    }

    /// <summary>Page content streams only: document IDs and font-subset tags vary per export.</summary>
    private static string PageContent(string path)
    {
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var text = new StringBuilder();
        foreach (var page in pdf.Pages)
        {
            foreach (var item in page.Contents.Elements)
            {
                var dictionary = (item as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfSharp.Pdf.PdfDictionary
                    ?? item as PdfSharp.Pdf.PdfDictionary;
                if (dictionary?.Stream != null)
                    text.Append(Encoding.Latin1.GetString(dictionary.Stream.UnfilteredValue));
            }
            text.Append('\f');
        }
        return text.ToString();
    }
}
