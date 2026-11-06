using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenNest.IO.Tests;

public class CommonLineUsagePersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AllowCommonLine_RoundTrips_AndIsOmittedWhenFalse(bool allow)
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.AllowCommonLine = allow;
        var nest = new Nest(drawing.Name);
        nest.Drawings.Add(drawing);

        var bytes = Write(nest);
        var reader = new NestReader(new MemoryStream(bytes));
        var copy = Assert.Single(reader.Read().Drawings);

        Assert.Empty(reader.Warnings);
        Assert.Equal(allow, copy.AllowCommonLine);
        var json = NestJson(bytes)["drawings"]!.AsArray().Single()!.AsObject();
        Assert.Equal(allow, json.ContainsKey("allowCommonLine"));
    }

    [Theory]
    [InlineData(CommonLineUsage.WhenItAddsParts, "whenItAddsParts")]
    [InlineData(CommonLineUsage.Whenever, "whenever")]
    public void CommonLineUsage_RoundTrips(CommonLineUsage usage, string saved)
    {
        var nest = new Nest("usage") { CommonLineUsage = usage };

        var bytes = Write(nest);

        Assert.Equal(saved, NestJson(bytes)["commonLineUsage"]!.GetValue<string>());
        Assert.Equal(usage, new NestReader(new MemoryStream(bytes)).Read().CommonLineUsage);
    }

    [Fact]
    public void CommonLineUsage_Off_IsOmitted()
    {
        var bytes = Write(new Nest("off"));

        Assert.False(NestJson(bytes).ContainsKey("commonLineUsage"));
        Assert.Equal(CommonLineUsage.Off, new NestReader(new MemoryStream(bytes)).Read().CommonLineUsage);
    }

    [Theory]
    [InlineData("\"sometimes\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void CommonLineUsage_Unknown_ReadsOff(string value)
    {
        var bytes = Write(new Nest("unknown") { CommonLineUsage = CommonLineUsage.Whenever });
        var edited = ReplaceNestJson(bytes, json => json.Replace("\"commonLineUsage\":\"whenever\"",
            $"\"commonLineUsage\":{value}"));

        Assert.Equal(CommonLineUsage.Off, new NestReader(new MemoryStream(edited)).Read().CommonLineUsage);
    }

    private static byte[] Write(Nest nest)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static JsonObject NestJson(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }

    private static byte[] ReplaceNestJson(byte[] archive, Func<string, string> edit)
    {
        using var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var input = entry.Open();
                using var buffer = new MemoryStream();
                input.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (entry.FullName == "nest.json")
                {
                    var text = new StreamReader(new MemoryStream(bytes), Encoding.UTF8).ReadToEnd();
                    var compact = JsonNode.Parse(text)!.ToJsonString();
                    var changed = edit(compact);
                    Assert.NotEqual(compact, changed);
                    bytes = Encoding.UTF8.GetBytes(changed);
                }
                var copy = target.CreateEntry(entry.FullName);
                using var write = copy.Open();
                write.Write(bytes);
            }
        }
        return output.ToArray();
    }
}
