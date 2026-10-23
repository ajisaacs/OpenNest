using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

/// <summary>
/// The nest's assigned machine persists as top-level <c>machine</c> (<c>id</c> and <c>name</c>);
/// a nest without one writes no key, and a malformed one loads as none.
/// </summary>
public class NestMachinePersistenceTests
{
    private static readonly NestMachine Machine = new(Guid.Parse("6f1a3c2e-5b7d-4e8f-9a0b-1c2d3e4f5a6b"), "Fiber 1");

    private static Nest NestWith(NestMachine machine)
    {
        var nest = new Nest("machine") { Machine = machine };
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.CreatePlate();
        return nest;
    }

    private static MemoryStream Write(Nest nest)
    {
        var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        return stream;
    }

    private static JsonNode NestJson(MemoryStream zip)
    {
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        var json = JsonNode.Parse(reader.ReadToEnd())!;
        zip.Position = 0;
        return json;
    }

    private static MemoryStream Rewrite(MemoryStream zip, Action<JsonNode> edit)
    {
        var json = NestJson(zip);
        edit(json);
        var copy = new MemoryStream();
        zip.Position = 0;
        zip.CopyTo(copy);
        using (var archive = new ZipArchive(copy, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("nest.json")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("nest.json").Open(), new UTF8Encoding(false));
            writer.Write(json.ToJsonString());
        }
        copy.Position = 0;
        return copy;
    }

    [Fact]
    public void MachineRoundTrips()
    {
        var reopened = new NestReader(Write(NestWith(Machine))).Read();

        Assert.Equal(Machine.Id, reopened.Machine!.Id);
        Assert.Equal("Fiber 1", reopened.Machine.Name);
    }

    [Fact]
    public void NestWithoutMachine_WritesNoKeyAndLoadsWithoutOne()
    {
        var zip = Write(NestWith(null));

        Assert.False(NestJson(zip).AsObject().ContainsKey("machine"));
        Assert.Null(new NestReader(zip).Read().Machine);
    }

    [Theory]
    [InlineData("\"Fiber 1\"")]
    [InlineData("{\"id\":42,\"name\":\"Fiber 1\"}")]
    [InlineData("{\"id\":\"not-a-guid\",\"name\":\"Fiber 1\"}")]
    [InlineData("{\"id\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"name\":\"Fiber 1\"}")]
    public void MalformedMachine_LoadsAsNone(string machine)
    {
        var zip = Rewrite(Write(NestWith(Machine)), json => json["machine"] = JsonNode.Parse(machine));

        var reopened = new NestReader(zip).Read();

        Assert.Null(reopened.Machine);
        Assert.Equal("machine", reopened.Name);
    }

    [Fact]
    public void MachineWithoutName_LoadsWithEmptyName()
    {
        var zip = Rewrite(Write(NestWith(Machine)),
            json => json["machine"] = JsonNode.Parse($"{{\"id\":\"{Machine.Id}\",\"name\":7}}"));

        var reopened = new NestReader(zip).Read();

        Assert.Equal(Machine.Id, reopened.Machine!.Id);
        Assert.Equal("", reopened.Machine.Name);
    }
}
