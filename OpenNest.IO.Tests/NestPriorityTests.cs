using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenNest.IO.Tests;

/// <summary>
/// Priority nests higher numbers first. Files keep writing the lower-first legacy value under
/// <c>priority</c> so older readers keep the same order, and files without <c>nestPriority</c>
/// are converted when read.
/// </summary>
public class NestPriorityTests
{
    [Fact]
    public void Write_KeepsTheLegacyValueBesideTheNewOne()
    {
        var file = Write(5);

        var drawing = DrawingJson(file);
        Assert.Equal(-5, (int)drawing["priority"]!);
        Assert.Equal(5, (int)drawing["nestPriority"]!);
        Assert.Equal(5, Read(file).Priority);
    }

    [Fact]
    public void Write_OmitsAZeroPriority()
    {
        var file = Write(0);

        Assert.False(DrawingJson(file).ContainsKey("nestPriority"));
        Assert.Equal(0, Read(file).Priority);
    }

    [Fact]
    public void Read_ConvertsAFileWithOnlyTheLegacyValue()
    {
        var file = Rewrite(Write(5), drawing =>
        {
            drawing.Remove("nestPriority");
            drawing["priority"] = 3;
        });

        Assert.Equal(-3, Read(file).Priority);
    }

    [Fact]
    public void Read_PrefersTheNewValue()
    {
        var file = Rewrite(Write(5), drawing => drawing["priority"] = 9);

        Assert.Equal(5, Read(file).Priority);
    }

    private static byte[] Write(int priority)
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.Priority = priority;
        var nest = new Nest(drawing.Name) { Units = Units.Millimeters };
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static Drawing Read(byte[] file) =>
        Assert.Single(new NestReader(new MemoryStream(file)).Read().Drawings);

    private static JsonObject DrawingJson(byte[] file)
    {
        using var archive = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd())!["drawings"]![0]!.AsObject();
    }

    private static byte[] Rewrite(byte[] file, Action<JsonObject> change)
    {
        using var stream = new MemoryStream();
        stream.Write(file);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("nest.json")!;
            JsonNode root;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                root = JsonNode.Parse(reader.ReadToEnd())!;
            change(root["drawings"]![0]!.AsObject());
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("nest.json").Open(), new UTF8Encoding(false));
            writer.Write(root.ToJsonString());
        }
        return stream.ToArray();
    }
}
