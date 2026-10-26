using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Converters;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

public class PartCuttingParametersPersistenceTests
{
    [Fact]
    public void ManualLeadIns_KeepTheirSettings_AndCleanPartsWriteNone()
    {
        var nest = new Nest("manual");
        var drawing = Square();
        nest.Drawings.Add(drawing);
        var plate = nest.CreatePlate();
        var leaded = new Part(drawing, new Vector(1, 1));
        var parameters = Parameters(0.3);
        leaded.ApplyLeadIns(parameters, Vector.Zero);
        plate.Parts.Add(leaded);
        plate.Parts.Add(new Part(drawing, new Vector(6, 1)));

        var bytes = Write(nest);
        var reader = new NestReader(new MemoryStream(bytes));
        var parts = Assert.Single(reader.Read().Plates).Parts;

        Assert.Empty(reader.Warnings);
        Assert.True(parts[0].HasManualLeadIns);
        Assert.Equal(CuttingParametersSerializer.Serialize(parameters),
            CuttingParametersSerializer.Serialize(parts[0].CuttingParameters));
        Assert.Null(parts[1].CuttingParameters);
        var json = PartsJson(bytes);
        Assert.True(json[0]!.AsObject().ContainsKey("cuttingParameters"));
        Assert.False(json[1]!.AsObject().ContainsKey("cuttingParameters"));
    }

    [Fact]
    public void CommittedCuttingPlan_KeepsEachPartsSettings()
    {
        var nest = new Nest("planned");
        var drawing = Square();
        nest.Drawings.Add(drawing);
        var plate = nest.CreatePlate();
        plate.Parts.Add(new Part(drawing, new Vector(1, 1)));
        plate.Parts.Add(new Part(drawing, new Vector(6, 1)));
        var parameters = Parameters(0.2);
        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, confirmedParameters: parameters));
        Assert.Equal(CuttingCommitStatus.Applied, CuttingPlanService.Apply([result]).Status);
        Assert.All(plate.Parts, part => Assert.NotNull(part.CuttingParameters));
        var expected = plate.Parts.Select(p => CuttingParametersSerializer.Serialize(p.CuttingParameters)).ToArray();

        var reader = new NestReader(new MemoryStream(Write(nest)));
        var parts = Assert.Single(reader.Read().Plates).Parts;

        Assert.Empty(reader.Warnings);
        Assert.All(parts, part => Assert.True(part.HasManualLeadIns));
        Assert.Equal(expected, parts.Select(p => CuttingParametersSerializer.Serialize(p.CuttingParameters)));
    }

    [Fact]
    public void OlderFiles_WithoutTheField_ReadNullSettings()
    {
        var nest = new Nest("legacy");
        var drawing = Square();
        nest.Drawings.Add(drawing);
        var part = new Part(drawing, new Vector(1, 1));
        part.ApplyLeadIns(Parameters(0.3), Vector.Zero);
        nest.CreatePlate().Parts.Add(part);
        var legacy = RemovePartSettings(Write(nest));

        var reader = new NestReader(new MemoryStream(legacy));
        var copy = Assert.Single(Assert.Single(reader.Read().Plates).Parts);

        Assert.Empty(reader.Warnings);
        Assert.True(copy.HasManualLeadIns);
        Assert.Null(copy.CuttingParameters);
    }

    private static Drawing Square() =>
        new("Square", ConvertGeometry.ToProgram(new List<Entity>
        {
            new Line(0, 0, 4, 0),
            new Line(4, 0, 4, 4),
            new Line(4, 4, 0, 4),
            new Line(0, 4, 0, 0),
        }));

    private static CuttingParameters Parameters(double leadLength) => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = leadLength, ApproachAngle = 45 },
        InternalLeadIn = new LineLeadIn { Length = leadLength, ApproachAngle = 45 },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
        PierceClearance = 0.05,
    };

    private static byte[] Write(Nest nest)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static JsonArray PartsJson(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        var nest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        return nest["plates"]!.AsArray().Single()!["parts"]!.AsArray();
    }

    private static byte[] RemovePartSettings(byte[] archive)
    {
        using var stream = new MemoryStream();
        stream.Write(archive);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("nest.json")!;
            JsonObject nest;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                nest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
            foreach (var part in nest["plates"]!.AsArray().SelectMany(p => p!["parts"]!.AsArray()))
                Assert.True(part!.AsObject().Remove("cuttingParameters"));
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("nest.json").Open(), new UTF8Encoding(false));
            writer.Write(nest.ToJsonString());
        }
        return stream.ToArray();
    }
}
