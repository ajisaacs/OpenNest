using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.IO;

public class NestStatusSerializationTests
{
    [Fact]
    public void RoundTrip_StatusAndMadeBy_Preserved()
    {
        var nest = CreateMinimalNest();
        nest.Status = NestStatus.ToBeCut;
        nest.MadeBy = "AJ";

        var loaded = RoundTrip(nest);

        Assert.Equal(NestStatus.ToBeCut, loaded.Status);
        Assert.Equal("AJ", loaded.MadeBy);
    }

    [Fact]
    public void RoundTrip_DefaultStatus_IsQuote()
    {
        var loaded = RoundTrip(CreateMinimalNest());

        Assert.Equal(NestStatus.Quote, loaded.Status);
        Assert.Equal("", loaded.MadeBy);
    }

    [Fact]
    public void RoundTrip_HasBeenCut_Preserved()
    {
        var nest = CreateMinimalNest();
        nest.Status = NestStatus.HasBeenCut;

        var loaded = RoundTrip(nest);

        Assert.Equal(NestStatus.HasBeenCut, loaded.Status);
    }

    [Fact]
    public void Status_SerializedAsEnumString()
    {
        var nest = CreateMinimalNest();
        nest.Status = NestStatus.ToBeCut;
        nest.MadeBy = "Rex";

        using var ms = new MemoryStream();
        new NestWriter(nest).Write(ms);
        var dtoJson = ReadNestJson(ms);
        using var doc = JsonDocument.Parse(dtoJson);

        // Enum strings follow the existing units convention (PascalCase).
        Assert.Equal("ToBeCut", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Rex", doc.RootElement.GetProperty("madeBy").GetString());
    }

    [Fact]
    public void LegacyFile_WithoutStatusFields_LoadsDefaults()
    {
        var nest = CreateMinimalNest();
        using var ms = new MemoryStream();
        new NestWriter(nest).Write(ms);

        // Rebuild the archive with the additive fields stripped from nest.json.
        var rebuilt = StripFields(ms, "status", "madeBy");

        var loaded = new NestReader(rebuilt).Read();

        Assert.Equal(NestStatus.Quote, loaded.Status);
        Assert.Equal("", loaded.MadeBy);
    }

    [Fact]
    public void File_WithUnknownStatusValue_LoadsQuote()
    {
        var nest = CreateMinimalNest();
        using var ms = new MemoryStream();
        new NestWriter(nest).Write(ms);
        var rebuilt = ReplaceField(ms, "status", "Banana");

        var loaded = new NestReader(rebuilt).Read();

        Assert.Equal(NestStatus.Quote, loaded.Status);
    }

    private static Nest CreateMinimalNest()
    {
        var program = new OpenNest.CNC.Program();
        program.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(10, 0)));
        var drawing = new Drawing("Part", program);
        var nest = new Nest { Name = "Test" };
        nest.Drawings.Add(drawing);
        var plate = new Plate(new Size(100, 100));
        plate.Parts.Add(new Part(drawing, new Vector(0, 0)));
        nest.Plates.Add(plate);
        return nest;
    }

    private static Nest RoundTrip(Nest nest)
    {
        var ms = new MemoryStream();
        new NestWriter(nest).Write(ms);
        ms.Position = 0;
        return new NestReader(ms).Read();
    }

    private static string ReadNestJson(MemoryStream archive)
    {
        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(
            zip.GetEntry("nest.json").Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static MemoryStream RewriteNestJson(MemoryStream archive, string json)
    {
        var output = new MemoryStream();
        archive.Position = 0;
        using (var source = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var newEntry = target.CreateEntry(entry.FullName);
                using var sourceStream = entry.Open();
                using var targetStream = newEntry.Open();
                if (entry.FullName == "nest.json")
                {
                    using var writer = new StreamWriter(targetStream, Encoding.UTF8);
                    writer.Write(json);
                }
                else
                {
                    sourceStream.CopyTo(targetStream);
                }
            }
        }
        output.Position = 0;
        return output;
    }

    private static MemoryStream StripFields(MemoryStream archive, params string[] fields)
    {
        using var doc = JsonDocument.Parse(ReadNestJson(archive));
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var root = doc.RootElement.Deserialize<Dictionary<string, JsonElement>>(options);
        foreach (var field in fields)
            root.Remove(field);
        return RewriteNestJson(archive, JsonSerializer.Serialize(root, options));
    }

    private static MemoryStream ReplaceField(MemoryStream archive, string field, string value)
    {
        using var doc = JsonDocument.Parse(ReadNestJson(archive));
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var root = doc.RootElement.Deserialize<Dictionary<string, JsonElement>>(options);
        root[field] = JsonSerializer.Deserialize<JsonElement>($"\"{value}\"");
        return RewriteNestJson(archive, JsonSerializer.Serialize(root, options));
    }
}
