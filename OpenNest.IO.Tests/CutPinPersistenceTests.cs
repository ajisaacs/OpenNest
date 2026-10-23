using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using static OpenNest.IO.Tests.NestArchiveJson;

namespace OpenNest.IO.Tests;

public class CutPinPersistenceTests
{
    private const string ValidPin =
        "{\"x\":13.5,\"y\":1.75,\"leadIn\":{\"type\":\"Line\",\"length\":0.25,\"approachAngle\":90},\"leadOut\":{\"type\":\"None\"}}";

    private static Drawing PinnedDrawing()
    {
        var drawing = DrawingPackageFixture.Build();
        drawing.CutPins.Add(new ContourCutPin(new Vector(13.5, -2.25),
            new LineLeadIn { Length = 0.25, ApproachAngle = 80 }, new NoLeadOut(),
            new NormalTab { Size = 0.03, CutoutMaxWidth = 4, CutoutMaxHeight = 5 }));
        drawing.CutPins.Add(new ContourCutPin(new Vector(7.5, 0.75),
            new ArcLeadIn { Radius = 0.1 }, new LineLeadOut { Length = 0.05, ApproachAngle = 45 }));
        drawing.CutPins.Add(new ContourCutPin(new Vector(3.5, 3.75),
            new LineLineLeadIn { Length1 = 0.2, ApproachAngle1 = 30, Length2 = 0.1, ApproachAngle2 = 60 },
            new NoLeadOut(),
            new BreakerTab { Size = 0.04, BreakerDepth = 0.01, TabLeadIn = new ArcLeadIn { Radius = 0.02 } }));
        return drawing;
    }

    [Fact]
    public void Pins_RoundTripEveryFieldInOrder()
    {
        var (restored, warnings) = RoundTrip(Write(PinnedDrawing()));

        Assert.Empty(warnings);
        var pins = Assert.Single(restored.Drawings).CutPins;
        Assert.Equal(3, pins.Count);

        Assert.Equal(new Vector(13.5, -2.25), pins[0].Point);
        var line = Assert.IsType<LineLeadIn>(pins[0].LeadIn);
        Assert.Equal((0.25, 80.0), (line.Length, line.ApproachAngle));
        Assert.IsType<NoLeadOut>(pins[0].LeadOut);
        var normal = Assert.IsType<NormalTab>(pins[0].Tab);
        Assert.Equal((0.03, 4.0, 5.0), (normal.Size, normal.CutoutMaxWidth, normal.CutoutMaxHeight));

        Assert.Equal(0.1, Assert.IsType<ArcLeadIn>(pins[1].LeadIn).Radius);
        var lineOut = Assert.IsType<LineLeadOut>(pins[1].LeadOut);
        Assert.Equal((0.05, 45.0), (lineOut.Length, lineOut.ApproachAngle));
        Assert.False(pins[1].HasTab);

        var lineLine = Assert.IsType<LineLineLeadIn>(pins[2].LeadIn);
        Assert.Equal((0.2, 30.0, 0.1, 60.0),
            (lineLine.Length1, lineLine.ApproachAngle1, lineLine.Length2, lineLine.ApproachAngle2));
        var breaker = Assert.IsType<BreakerTab>(pins[2].Tab);
        Assert.Equal((0.04, 0.01), (breaker.Size, breaker.BreakerDepth));
        Assert.Equal(0.02, Assert.IsType<ArcLeadIn>(breaker.TabLeadIn).Radius);
    }

    [Fact]
    public void NoPins_AreOmittedFromTheFile()
    {
        var archive = Write(DrawingPackageFixture.Build());

        var (restored, warnings) = RoundTrip(archive);

        Assert.False(NestJson(archive)["drawings"]![0]!.AsObject().ContainsKey("cutPins"));
        Assert.Empty(warnings);
        Assert.Empty(Assert.Single(restored.Drawings).CutPins);
    }

    public static TheoryData<string> MalformedPins => new()
    {
        "\"not a pin\"",
        "[]",
        "{\"x\":\"left\",\"y\":0,\"leadIn\":{\"type\":\"Line\",\"length\":0.25},\"leadOut\":{\"type\":\"None\"}}",
        "{\"x\":13.5,\"y\":1.75,\"leadIn\":{\"type\":\"Spiral\",\"length\":0.25},\"leadOut\":{\"type\":\"None\"}}",
        "{\"x\":13.5,\"y\":1.75,\"leadIn\":{\"type\":\"Line\",\"length\":0.25}}",
        "{\"x\":13.5,\"y\":1.75,\"leadIn\":{\"type\":\"Line\",\"length\":-0.25},\"leadOut\":{\"type\":\"None\"}}",
        "{\"x\":13.5,\"y\":1.75,\"leadIn\":{\"type\":\"Line\",\"length\":0.25},\"leadOut\":{\"type\":\"Line\",\"length\":0.1},\"tab\":{\"type\":\"Normal\",\"size\":0.03}}",
        "{\"x\":13.5,\"y\":1.75,\"leadIn\":{\"type\":\"Line\",\"length\":0.25},\"leadOut\":{\"type\":\"None\"},\"tab\":{\"type\":\"None\"}}",
        "{\"x\":13.5,\"y\":1.75,\"leadIn\":{\"type\":\"Line\",\"length\":0.25},\"leadOut\":{\"type\":\"None\"},\"tab\":{\"type\":\"Normal\",\"tabLeadIn\":{\"type\":\"Spiral\"}}}",
    };

    [Theory]
    [MemberData(nameof(MalformedPins))]
    public void MalformedPin_IsSkippedWithAWarningAndTheRestLoad(string pinJson)
    {
        var archive = ReplaceCutPins(Write(DrawingPackageFixture.Build()),
            JsonNode.Parse($"[{ValidPin},{pinJson}]"));

        var (restored, warnings) = RoundTrip(archive);

        var pin = Assert.Single(Assert.Single(restored.Drawings).CutPins);
        Assert.Equal(new Vector(13.5, 1.75), pin.Point);
        var warning = Assert.Single(warnings);
        Assert.Contains("'Synthetic bracket'", warning);
        Assert.Contains("cut pin 2", warning);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("\"pins\"")]
    public void PinsThatAreNotAList_LoadNoPinsWithAWarning(string value)
    {
        var archive = ReplaceCutPins(Write(DrawingPackageFixture.Build()), JsonNode.Parse(value));

        var (restored, warnings) = RoundTrip(archive);

        var drawing = Assert.Single(restored.Drawings);
        Assert.Empty(drawing.CutPins);
        Assert.Equal(NestWriter.GetProgramText(DrawingPackageFixture.Build().Program),
            NestWriter.GetProgramText(drawing.Program));
        Assert.Contains("cut pin", Assert.Single(warnings));
    }

    [Fact]
    public void Package_CarriesPinsAndTheyArePartOfItsContent()
    {
        var drawing = PinnedDrawing();
        var pinned = DrawingPackage.Write(drawing, Units.Inches);
        var pins = drawing.CutPins.ToList();
        drawing.CutPins.Clear();
        var plain = DrawingPackage.Write(drawing, Units.Inches);
        drawing.CutPins.AddRange(pins);

        var contents = DrawingPackage.Read(pinned);

        Assert.Empty(contents.Warnings);
        Assert.Equal(3, contents.Drawing.CutPins.Count);
        Assert.Equal(new Vector(7.5, 0.75), contents.Drawing.CutPins[1].Point);
        Assert.NotEqual(SHA256.HashData(plain), SHA256.HashData(pinned));
        Assert.Equal(SHA256.HashData(pinned), SHA256.HashData(DrawingPackage.Write(drawing, Units.Inches)));
    }

    [Fact]
    public void PinsOff_RoundTripsPerPartAndIsOmittedWhenFalse()
    {
        var archive = WritePlate(pinsOff: new[] { true, false });

        var (restored, warnings) = RoundTrip(archive);

        Assert.Empty(warnings);
        var parts = Assert.Single(restored.Plates).Parts;
        Assert.Equal(new[] { true, false }, parts.Select(p => p.DrawingPinsOff));
        var partJson = NestJson(archive)["plates"]![0]!["parts"]!.AsArray();
        Assert.True(partJson[0]!["drawingPinsOff"]!.GetValue<bool>());
        Assert.False(partJson[1]!.AsObject().ContainsKey("drawingPinsOff"));
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("{}")]
    public void MalformedPinsOff_ReadsAsPinsOnWithAWarning(string value)
    {
        var archive = WritePlate(pinsOff: new[] { true });
        var json = NestJson(archive);
        json["plates"]![0]!["parts"]![0]!["drawingPinsOff"] = JsonNode.Parse(value);

        var (restored, warnings) = RoundTrip(ReplaceNestJson(archive, json));

        Assert.False(Assert.Single(Assert.Single(restored.Plates).Parts).DrawingPinsOff);
        var warning = Assert.Single(warnings);
        Assert.Contains("part 0 ('Synthetic bracket')", warning);
        Assert.Contains("drawing pins", warning);
    }

    private static byte[] WritePlate(bool[] pinsOff)
    {
        var drawing = PinnedDrawing();
        var nest = new Nest(drawing.Name) { Units = Units.Inches };
        nest.Drawings.Add(drawing);
        var plate = new Plate(48, 96);
        nest.Plates.Add(plate);
        for (var i = 0; i < pinsOff.Length; i++)
        {
            var part = new Part(drawing, new Vector(1 + 15 * i, 1)) { DrawingPinsOff = pinsOff[i] };
            plate.Parts.Add(part);
        }

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static byte[] Write(Drawing drawing)
    {
        var nest = new Nest(drawing.Name) { Units = Units.Inches };
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static byte[] ReplaceCutPins(byte[] archive, JsonNode? cutPins)
    {
        var json = NestJson(archive);
        json["drawings"]![0]!["cutPins"] = cutPins;
        return ReplaceNestJson(archive, json);
    }
}
