using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

/// <summary>
/// Rails with clamps on other edges than the origin side persist those clamps in optional
/// per-edge arrays; origin-side-only rails write exactly the keys they always did.
/// </summary>
public class ClampEdgePersistenceTests
{
    private static ClampRail Mixed() => new(1.5, 0.625, new[]
    {
        new RailClamp(ClampEdge.OriginSide, 6),
        new RailClamp(ClampEdge.FarSide, 20),
        new RailClamp(ClampEdge.OriginEnd, 8),
        new RailClamp(ClampEdge.FarEnd, 30),
    });

    private static Nest NestWith(ClampRail rail)
    {
        var nest = new Nest("edges");
        nest.PlateDefaults.Size = new Size(48, 96);
        nest.SetClampRail(rail);
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

    private static JsonNode RailJson(MemoryStream zip)
    {
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        var json = JsonNode.Parse(reader.ReadToEnd())!["plateDefaults"]!["clampRail"]!;
        zip.Position = 0;
        return json;
    }

    [Fact]
    public void ClampsOnEveryEdgeRoundTripAndPlatesFollowTheRail()
    {
        var reopened = new NestReader(Write(NestWith(Mixed()))).Read();

        Assert.Equal(Mixed(), reopened.PlateDefaults.ClampRail);
        var plate = Assert.Single(reopened.Plates);
        Assert.Same(reopened.PlateDefaults.ClampRail, plate.ClampRail);
        Assert.Equal(4, plate.ClampZones.Count);
    }

    [Fact]
    public void OriginSideOnlyRailsOmitTheOtherEdgeKeys()
    {
        var rail = RailJson(Write(NestWith(new ClampRail(1.5, 0.625, new double[] { 6, 21 })))).AsObject();

        Assert.Equal(new[] { "width", "depth", "centers", "removalMinutes" }, rail.Select(p => p.Key));
    }

    [Fact]
    public void OtherEdgesAreWrittenInTheirOwnArrays()
    {
        var rail = RailJson(Write(NestWith(Mixed())));

        Assert.Equal(6, rail["centers"]![0]!.GetValue<double>());
        Assert.Equal(20, rail["farSideCenters"]![0]!.GetValue<double>());
        Assert.Equal(8, rail["originEndCenters"]![0]!.GetValue<double>());
        Assert.Equal(30, rail["farEndCenters"]![0]!.GetValue<double>());
    }

    [Fact]
    public void ARailWithNoOriginSideClampsStillLoads()
    {
        var farOnly = new ClampRail(1.5, 0.625, new[] { new RailClamp(ClampEdge.FarSide, 12) });
        var reopened = new NestReader(Write(NestWith(farOnly))).Read();

        Assert.Equal(farOnly, reopened.PlateDefaults.ClampRail);
    }
}
