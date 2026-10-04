using OpenNest.Reporting;

namespace OpenNest.Tests.Reporting;

public sealed class AtomicReportFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-report-" + Guid.NewGuid().ToString("N"));

    public AtomicReportFileTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void SuccessfulRenderReplacesExistingFileOnlyAfterRendering()
    {
        var path = Path.Combine(directory, "existing.pdf");
        File.WriteAllText(path, "original");
        AtomicReportFile.Write(path, stream =>
        {
            Assert.Equal("original", File.ReadAllText(path));
            stream.Write("replacement"u8);
        });
        Assert.Equal("replacement", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }

    [Fact]
    public void LateRenderFailurePreservesExistingFileAndRemovesTemporarySibling()
    {
        var path = Path.Combine(directory, "existing.pdf");
        File.WriteAllText(path, "original");
        Assert.Throws<IOException>(() => AtomicReportFile.Write(path, stream =>
        {
            stream.Write("partial PDF"u8);
            Assert.Equal(2, Directory.GetFiles(directory).Length);
            throw new IOException("injected late render failure");
        }));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }

    [Fact]
    public void FailedNewExportLeavesNoOutput()
    {
        var path = Path.Combine(directory, "new.pdf");
        Assert.Throws<IOException>(() => AtomicReportFile.Write(path, stream =>
        {
            stream.Write("partial"u8);
            throw new IOException("injected write failure");
        }));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void FailedReplacementPreservesDirectoryTargetAndCleansTemporaryFile()
    {
        var path = Path.Combine(directory, "not-a-file.pdf");
        Directory.CreateDirectory(path);
        // Moving onto a directory target raises IOException on some platforms and
        // UnauthorizedAccessException on others (observed on Windows); callers treat both alike.
        var error = Record.Exception(() => AtomicReportFile.Write(path, stream => stream.Write("PDF"u8)));
        Assert.True(error is IOException or UnauthorizedAccessException,
            $"Expected IOException or UnauthorizedAccessException, got {error?.GetType()}");
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.GetFiles(directory));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
