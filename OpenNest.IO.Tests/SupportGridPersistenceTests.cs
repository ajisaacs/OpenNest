using System.Text.Json.Nodes;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.Support;
using static OpenNest.IO.Tests.NestArchiveJson;

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

    [Fact]
    public void NoBedTabRangeRoundTripsInCuttingParameters()
    {
        var parameters = new CuttingParameters { SupportTabs = true, TabSizeMinWidth = 0.5, TabSizeMaxWidth = 4 };

        var restored = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(parameters));
        var snapshot = CuttingParametersSerializer.FromDto(CuttingParametersSerializer.ToDto(parameters));

        Assert.Equal(0.5, restored.TabSizeMinWidth);
        Assert.Equal(4, restored.TabSizeMaxWidth);
        Assert.Equal(4, snapshot.TabSizeMaxWidth);
        Assert.Equal(0, CuttingParametersSerializer.Deserialize("{}").TabSizeMaxWidth);
    }

    [Fact]
    public void TabHeldLengthRoundTripsInCuttingParameters()
    {
        var parameters = new CuttingParameters { SupportTabs = true, TabHeldMaxLength = 2 };

        var restored = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(parameters));
        var snapshot = CuttingParametersSerializer.FromDto(CuttingParametersSerializer.ToDto(parameters));

        Assert.Equal(2, restored.TabHeldMaxLength);
        Assert.Equal(2, snapshot.TabHeldMaxLength);
        Assert.Equal(0, CuttingParametersSerializer.Deserialize("{}").TabHeldMaxLength);
    }
}
