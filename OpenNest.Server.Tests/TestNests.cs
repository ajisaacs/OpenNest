using System.IO.Compression;
using System.Text;

namespace OpenNest.Server.Tests;

/// <summary>
/// Smallest archives the server accepts as nests. The tag names the nest, so different tags
/// give different bytes, and fixed entry times make the same tag give identical bytes; tests
/// compare stored archives exactly, as they did with arbitrary byte payloads.
/// </summary>
internal static class TestNests
{
    private static readonly DateTimeOffset EntryTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Archive(params byte[] tag) =>
        Zip("nest.json", $$"""{"version":2,"name":"tag {{Convert.ToHexString(tag)}}","drawings":[],"plates":[]}""");

    /// <summary>A valid ZIP holding <paramref name="name"/>; not necessarily a nest.</summary>
    public static byte[] Zip(string name, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(name);
            entry.LastWriteTime = EntryTime;
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return stream.ToArray();
    }
}
