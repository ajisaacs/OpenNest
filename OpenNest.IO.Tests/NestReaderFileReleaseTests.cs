using System.IO.Compression;
using OpenNest.IO;

namespace OpenNest.IO.Tests;

/// <summary>
/// A file that fails to read must not stay open: on Windows an open handle keeps the user from
/// renaming, replacing or deleting it until the garbage collector runs.
/// </summary>
public sealed class NestReaderFileReleaseTests : IDisposable
{
    private readonly string directory =
        Directory.CreateTempSubdirectory("opennest-reader-release-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static void AssertNotOpen(string path)
    {
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void AFileThatIsNotANest_IsClosedWhenTheReaderRefusesIt()
    {
        var path = Path.Combine(directory, "broken.nest");
        File.WriteAllText(path, "not a nest");

        Assert.ThrowsAny<Exception>(() => new NestReader(path).Read());

        AssertNotOpen(path);
    }

    [Fact]
    public void AnArchiveWithoutANest_IsClosedWhenReadFails()
    {
        var path = Path.Combine(directory, "empty.nest");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");

        Assert.ThrowsAny<Exception>(() => new NestReader(path).Read());

        AssertNotOpen(path);
    }
}
