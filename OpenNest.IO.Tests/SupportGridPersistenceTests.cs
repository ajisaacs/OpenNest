using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.IO.Tests;

/// <summary>
/// The nest's support grid persists in <c>plateDefaults.supportBed</c> and every plate follows
/// it on load; a nest without one writes no key, and a malformed one loads as none.
/// </summary>
public class SupportGridPersistenceTests
{
    private static readonly SupportGrid Grid = new(2, 1.5, 0.5, -0.25, 0.125, 0.25);

    private static Nest NestWith(SupportBed bed)
    {
        var nest = new Nest("grid");
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.SetSupportBed(bed);
        var plate = nest.CreatePlate();
        plate.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical));
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
    public void GridRoundTripsAndEveryPlateFollowsIt()
    {
        var reopened = new NestReader(Write(NestWith(Grid))).Read();

        Assert.Equal(Grid, reopened.SupportBed);
        Assert.Same(reopened.SupportBed, Assert.Single(reopened.Plates).SupportBed);
        Assert.Same(reopened.SupportBed, reopened.CreatePlate().SupportBed);
    }

    [Fact]
    public void GridIsWrittenWithItsType()
    {
        var bed = NestJson(Write(NestWith(Grid)))["plateDefaults"]!["supportBed"]!;

        Assert.Equal("grid", bed["type"]!.GetValue<string>());
        Assert.Equal(1.5, bed["pitchY"]!.GetValue<double>());
        Assert.Equal(-0.25, bed["offsetY"]!.GetValue<double>());
    }

    [Fact]
    public void SlatsRoundTripWithTheirTypeAndAxis()
    {
        var slats = new SupportSlats(SlatAxis.Y, 4, 0.125, 1.5, 0.125, 0.25);
        var file = Write(NestWith(slats));

        var bed = NestJson(file)["plateDefaults"]!["supportBed"]!;
        var reopened = new NestReader(file).Read();

        Assert.Equal("slats", bed["type"]!.GetValue<string>());
        Assert.Equal("y", bed["axis"]!.GetValue<string>());
        Assert.Equal(0.125, bed["thickness"]!.GetValue<double>());
        Assert.False(bed.AsObject().ContainsKey("pitchX"));
        Assert.Equal(slats, reopened.SupportBed);
        Assert.Same(reopened.SupportBed, Assert.Single(reopened.Plates).SupportBed);
    }

    [Theory]
    [InlineData("""{"type":"slats","axis":"z","pitch":4,"thickness":0.125}""")]
    [InlineData("""{"type":"slats","axis":"x","pitch":0,"thickness":0.125}""")]
    [InlineData("""{"type":"slats","axis":"x","pitch":4}""")]
    [InlineData("""{"type":"slats","axis":"x","pitch":4,"thickness":0.125,"offset":"one"}""")]
    public void MalformedSlats_LoadAsNone(string bed)
    {
        var file = Rewrite(Write(NestWith(Grid)), json => json["plateDefaults"]!["supportBed"] = JsonNode.Parse(bed));

        Assert.Null(new NestReader(file).Read().SupportBed);
    }

    [Fact]
    public void NestWithoutGrid_WritesNoKey()
    {
        var defaults = NestJson(Write(NestWith(null)))["plateDefaults"]!.AsObject();

        Assert.False(defaults.ContainsKey("supportBed"));
    }

    [Theory]
    [InlineData("""{"type":"slats","pitchX":2,"pitchY":2}""")]
    [InlineData("""{"type":"grid","pitchX":0,"pitchY":2}""")]
    [InlineData("""{"type":"grid","pitchX":"two","pitchY":2}""")]
    [InlineData("""[1,2]""")]
    public void MalformedOrUnknownGrid_LoadsAsNone(string bed)
    {
        var file = Rewrite(Write(NestWith(Grid)), json => json["plateDefaults"]!["supportBed"] = JsonNode.Parse(bed));

        var reopened = new NestReader(file).Read();

        Assert.Null(reopened.SupportBed);
        Assert.Null(Assert.Single(reopened.Plates).SupportBed);
    }

    [Fact]
    public void SupportTabsRoundTripInCuttingParameters()
    {
        var parameters = new CuttingParameters { SupportTabs = true, TabConfig = new NormalTab { Size = 0.1 } };

        var restored = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(parameters));

        Assert.True(restored.SupportTabs);
    }
}
