using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenNest.IO.Tests;

/// <summary>
/// Reads and rewrites the <c>nest.json</c> entry of a .nest archive, for tests that check or
/// corrupt the stored shape. Import with <c>using static</c>.
/// </summary>
internal static class NestArchiveJson
{
    public static (Nest Nest, IReadOnlyList<string> Warnings) RoundTrip(byte[] archive)
    {
        var reader = new NestReader(new MemoryStream(archive));
        return (reader.Read(), reader.Warnings);
    }

    public static JsonObject NestJson(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }

    public static byte[] ReplaceNestJson(byte[] archive, JsonObject json)
    {
        using var output = new MemoryStream();
        output.Write(archive);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry("nest.json")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("nest.json").Open(), new UTF8Encoding(false));
            writer.Write(json.ToJsonString());
        }

        return output.ToArray();
    }

    public static string ReadNestJson(MemoryStream zipBytes)
    {
        zipBytes.Position = 0;
        using var archive = new ZipArchive(zipBytes, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("nest.json");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>A copy of the archive whose nest.json text passed through <paramref name="rewrite"/>.</summary>
    public static MemoryStream RewriteNestJson(MemoryStream zipBytes, Func<string, string> rewrite)
    {
        zipBytes.Position = 0;
        var output = new MemoryStream();
        using (var source = new ZipArchive(zipBytes, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var newEntry = target.CreateEntry(entry.FullName);
                using var input = entry.Open();
                using var outStream = newEntry.Open();
                if (entry.FullName == "nest.json")
                {
                    using var text = new StreamReader(input, Encoding.UTF8);
                    using var writer = new StreamWriter(outStream, new UTF8Encoding(false));
                    writer.Write(rewrite(text.ReadToEnd()));
                }
                else
                {
                    input.CopyTo(outStream);
                }
            }
        }
        output.Position = 0;
        return output;
    }
}
