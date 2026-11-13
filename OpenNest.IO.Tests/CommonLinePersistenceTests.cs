using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.CNC;
using OpenNest.CommonLines;
using OpenNest.CutOffs;
using OpenNest.Geometry;
using static OpenNest.IO.Tests.NestArchiveJson;

namespace OpenNest.IO.Tests;

/// <summary>Common-line groups persist as part positions and a kerf, and only when there are any.</summary>
public class CommonLinePersistenceTests
{
    private const double Kerf = 0.012;

    private static Drawing Rectangle()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(6, 0));
        program.Codes.Add(new LinearMove(6, 12));
        program.Codes.Add(new LinearMove(0, 12));
        program.Codes.Add(new LinearMove(0, 0));
        return new Drawing("rect", program);
    }

    private static (Nest Nest, Part[] Parts) NestWithRow()
    {
        var nest = new Nest("common") { Units = Units.Inches };
        var drawing = Rectangle();
        nest.Drawings.Add(drawing);
        var plate = nest.CreatePlate();
        plate.PartSpacing = 0.25;
        var parts = new[]
        {
            new Part(drawing, new Vector(1, 1)),
            new Part(drawing, new Vector(20, 1)),
            new Part(drawing, new Vector(7 + Kerf, 1)),
        };
        foreach (var part in parts)
            plate.Parts.Add(part);
        // A cut-off part sits between parts in the sequence but never in the parts array.
        plate.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        plate.JoinCommonLine(new[] { parts[0], parts[2] }, Kerf);
        return (nest, parts);
    }

    [Fact]
    public void GroupsRoundTripByPartPosition()
    {
        var (nest, _) = NestWithRow();

        var plate = Assert.Single(new NestReader(Write(nest)).Read().Plates);

        var group = Assert.Single(plate.CommonLines);
        Assert.Equal(Kerf, group.Kerf);
        Assert.Equal(new[] { 1.0, 7 + Kerf }, group.Members.Select(m => m.Location.X).OrderBy(x => x));
        Assert.True(CommonLineGeometry.Check(group).IsIntact);
    }

    [Fact]
    public void NoGroups_WritesNoKey()
    {
        var (nest, _) = NestWithRow();
        nest.Plates[0].CommonLines.Clear();

        Assert.Null(PlateJson(Write(nest))["commonLines"]);
    }

    [Fact]
    public void GroupsWriteKerfAndPositions()
    {
        var (nest, _) = NestWithRow();

        var group = PlateJson(Write(nest))["commonLines"]![0]!;
        Assert.Equal(Kerf, group["kerf"]!.GetValue<double>());
        Assert.Equal(new[] { 0, 2 }, group["parts"]!.AsArray().Select(n => n!.GetValue<int>()));
    }

    private static JsonNode PlateJson(MemoryStream zip)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("nest.json")!.Open(), Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd())!["plates"]![0]!;
    }
}
