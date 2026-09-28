using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Posts.CincinnatiCIFiber;

namespace OpenNest.Tests.IO;

public class PartLeadInSerializationTests
{
    [Fact]
    public void RoundTrip_RotatedLeadInsKeepPoseFlagsAndBounds()
    {
        var nest = CreateNest();
        var expected = nest.Plates[0].Parts[0];
        var loaded = Read(Save(nest));
        var actual = loaded.Plates[0].Parts[0];

        Assert.True(actual.HasManualLeadIns);
        Assert.True(actual.LeadInsLocked);
        Assert.Equal(expected.Location, actual.Location);
        Assert.Equal(expected.Rotation, actual.Rotation, 8);
        AssertBox(expected.BoundingBox, actual.BoundingBox);
        Assert.Null(actual.CuttingParameters);
        Assert.Same(loaded.Drawings.First(), actual.BaseDrawing);
        Assert.NotEmpty(actual.Program.SubPrograms);
        foreach (var call in actual.Program.Codes.OfType<SubProgramCall>())
            Assert.Same(actual.Program.SubPrograms[call.Id], call.Program);
    }

    [Fact]
    public void SingleManualLeadIn_LockedPartIsSkippedByAssignerAfterReload()
    {
        var nest = CreateNest();
        var part = nest.Plates[0].Parts[0];
        part.RemoveLeadIns();
        part.Rotate(-part.Rotation);
        part.ApplySingleLeadIn(Parameters(), new Vector(5, 0),
            new Line(new Vector(10, 0), new Vector(0, 0)), ContourType.External);
        part.LeadInsLocked = true;
        var loaded = Read(Save(nest));
        var plate = loaded.Plates[0];
        var restored = plate.Parts[0];
        var program = restored.Program;
        plate.CuttingParameters = Parameters();

        new LeadInAssigner { Sequencer = new LeftSideSequencer() }.Assign(plate);

        Assert.True(restored.HasManualLeadIns);
        Assert.True(restored.LeadInsLocked);
        Assert.Same(program, restored.Program);
    }

    [Fact]
    public void RoundTrip_PreservesProgramsLayersAndTabGapCodeForCode()
    {
        var nest = CreateNest();
        var part = nest.Plates[0].Parts[0];
        var main = NestWriter.GetProgramText(part.Program);
        var subs = NestWriter.GetSubProgramsText(part.Program);
        Assert.Contains(":LEADIN", main);
        Assert.Contains(":LEADOUT", main);
        Assert.Contains(":LEADIN", subs);

        var withoutTab = CreateNest();
        var parameters = Parameters();
        parameters.TabsEnabled = false;
        withoutTab.Plates[0].Parts[0].RemoveLeadIns();
        withoutTab.Plates[0].Parts[0].ApplyLeadIns(parameters, new Vector(-5, -5));
        Assert.NotEqual(main, NestWriter.GetProgramText(withoutTab.Plates[0].Parts[0].Program));

        var restored = Read(Save(nest)).Plates[0].Parts[0];
        Assert.Equal(main, NestWriter.GetProgramText(restored.Program));
        Assert.Equal(subs, NestWriter.GetSubProgramsText(restored.Program));
        var twice = Read(Save(Read(Save(nest)))).Plates[0].Parts[0];
        Assert.Equal(main, NestWriter.GetProgramText(twice.Program));
        Assert.Equal(subs, NestWriter.GetSubProgramsText(twice.Program));
    }

    [Fact]
    public void RemoveAfterReload_RestoresCleanRotatedDrawingAtSameLocation()
    {
        var restored = Read(Save(CreateNest())).Plates[0].Parts[0];
        var expected = new Part(restored.BaseDrawing);
        expected.Rotate(restored.Rotation);
        expected.Location = restored.Location;

        restored.RemoveLeadIns();

        AssertClean(expected, restored);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("empty")]
    [InlineData("missing-subs")]
    [InlineData("corrupt-subs")]
    [InlineData("empty-subs")]
    public void DamagedPartEntry_LoadsCleanWarnsAndKeepsOtherParts(string damage)
    {
        var nest = CreateNest();
        nest.Plates[0].Parts.Add((Part)nest.Plates[0].Parts[0].Clone());
        var bytes = EditArchive(Save(nest), zip =>
        {
            const string name = "parts/plate-1/part-0";
            switch (damage)
            {
                case "missing": zip.GetEntry(name)!.Delete(); break;
                case "corrupt": ReplaceEntry(zip, name, "G-not-a-number\n"); break;
                case "empty": ReplaceEntry(zip, name, "G91\n:empty program\n"); break;
                case "missing-subs": zip.GetEntry(name + "-subs")!.Delete(); break;
                case "corrupt-subs": ReplaceEntry(zip, name + "-subs", ":1\nG-invalid\nM99\n"); break;
                case "empty-subs": ReplaceEntry(zip, name + "-subs", ":1\nG91\nM99\n"); break;
            }
        });
        var reader = new NestReader(new MemoryStream(bytes));
        var loaded = reader.Read();
        var clean = new Part(loaded.Drawings.First());
        clean.Rotate(nest.Plates[0].Parts[0].Rotation);
        clean.Location = nest.Plates[0].Parts[0].Location;

        AssertClean(clean, loaded.Plates[0].Parts[0]);
        Assert.True(loaded.Plates[0].Parts[1].HasManualLeadIns);
        Assert.True(loaded.Plates[0].Parts[1].LeadInsLocked);
        var warning = Assert.Single(reader.Warnings);
        Assert.Contains("Plate 1, part 0", warning);
        Assert.Contains("parts/plate-1/part-0", warning);
    }

    [Fact]
    public void LegacyFlagsWithoutProgram_LoadCleanWithoutWarning()
    {
        var bytes = EditArchive(Save(CreateNest()), zip =>
        {
            var json = JsonNode.Parse(EntryText(zip, "nest.json"))!;
            var part = json["plates"]![0]!["parts"]![0]!.AsObject();
            part.Remove("program");
            part.Remove("drawingHash");
            ReplaceEntry(zip, "nest.json", json.ToJsonString());
        });
        var reader = new NestReader(new MemoryStream(bytes));
        var loaded = reader.Read();
        Assert.False(loaded.Plates[0].Parts[0].HasManualLeadIns);
        Assert.False(loaded.Plates[0].Parts[0].LeadInsLocked);
        Assert.Empty(reader.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedDrawingTextOrHoleText_DropsSavedProgramSilently(bool changeHole)
    {
        var nest = CreateNest();
        if (changeHole)
        {
            // A drawing may itself call hole sub-programs. Keep it clean but give it one.
            var drawing = nest.Drawings.First();
            var sub = new Program();
            sub.Codes.Add(new RapidMove(6, 5));
            sub.Codes.Add(new ArcMove(new Vector(6, 5), new Vector(5, 5), RotationType.CW));
            drawing.Program.SubPrograms[42] = sub;
            drawing.Program.Codes.Add(new SubProgramCall { Id = 42, Program = sub });
        }
        var bytes = EditArchive(Save(nest), zip =>
        {
            var name = changeHole ? "programs/program-1-subs" : "programs/program-1";
            ReplaceEntry(zip, name, EntryText(zip, name).Replace("X6Y5", "X7Y5"));
        });
        var reader = new NestReader(new MemoryStream(bytes));
        var loaded = reader.Read();
        Assert.False(loaded.Plates[0].Parts[0].HasManualLeadIns);
        Assert.False(loaded.Plates[0].Parts[0].LeadInsLocked);
        Assert.Empty(reader.Warnings);
    }

    [Fact]
    public void ArchivePaths_UseSerializedPlateAndPartIndices_AndSkipCleanParts()
    {
        var nest = CreateNest();
        var plate = nest.Plates[0];
        plate.Parts.Insert(0, new Part(nest.Drawings.First()));
        nest.Plates.Insert(0, new Plate()); // empty plates are not serialized
        plate.CutOffs.Add(new CutOff(new Vector(30, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        var second = new Plate { Size = plate.Size };
        second.Parts.Add((Part)plate.Parts[1].Clone());
        nest.Plates.Add(second);
        using var zip = new ZipArchive(new MemoryStream(Save(nest)));
        var names = zip.Entries.Where(e => e.FullName.StartsWith("parts/")).Select(e => e.FullName).ToArray();
        Assert.Equal(new[] { "parts/plate-1/part-1", "parts/plate-1/part-1-subs",
            "parts/plate-2/part-0", "parts/plate-2/part-0-subs" }, names);
    }

    [Fact]
    public void RestoreRejectsNoMotionWithoutChangingPart()
    {
        var part = CreateNest().Plates[0].Parts[0];
        var program = part.Program;
        Assert.False(part.RestoreLeadInProgram(new Program(), false));
        Assert.Same(program, part.Program);
        Assert.True(part.HasManualLeadIns);
        Assert.True(part.LeadInsLocked);
    }

    [Fact]
    public void PlateCuttingParameters_RoundTripIndependentlyWithoutRegeneratingParts()
    {
        var nest = CreateNest();
        var first = nest.Plates[0];
        first.CuttingParameters = Parameters();
        first.CuttingParameters.MachineName = "Saved machine";
        first.CuttingParameters.Assignment.Preference = "TAIL";
        first.CuttingParameters.Sequencing.SmallCutoutWidth = 2.75;
        first.CuttingParameters.TabConfig = new BreakerTab
        {
            Size = 0.3,
            BreakerDepth = 0.07,
            BreakerAngle = 35,
            TabLeadIn = new ArcLeadIn { Radius = 0.12 },
        };
        var second = new Plate { Size = first.Size, CuttingParameters = Parameters() };
        second.CuttingParameters.ExternalLeadIn = new ArcLeadIn { Radius = 0.875 };
        second.Parts.Add(new Part(nest.Drawings.First()));
        nest.Plates.Add(second);
        var program = NestWriter.GetProgramText(first.Parts[0].Program);

        var loaded = Read(Save(nest));

        var parameters = loaded.Plates[0].CuttingParameters;
        Assert.NotNull(parameters);
        Assert.Equal("Saved machine", parameters.MachineName);
        Assert.Equal("TAIL", parameters.Assignment.Preference);
        Assert.Equal(2.75, parameters.Sequencing.SmallCutoutWidth);
        Assert.Equal(0.5, Assert.IsType<LineLeadIn>(parameters.ExternalLeadIn).Length);
        Assert.Equal(0.25, Assert.IsType<LineLeadOut>(parameters.ExternalLeadOut).Length);
        var tab = Assert.IsType<BreakerTab>(parameters.TabConfig);
        Assert.Equal(0.3, tab.Size);
        Assert.Equal(0.07, tab.BreakerDepth);
        Assert.Equal(35, tab.BreakerAngle);
        Assert.Equal(0.12, Assert.IsType<ArcLeadIn>(tab.TabLeadIn).Radius);
        Assert.Equal(0.875, Assert.IsType<ArcLeadIn>(loaded.Plates[1].CuttingParameters.ExternalLeadIn).Radius);
        Assert.NotSame(parameters, loaded.Plates[1].CuttingParameters);
        Assert.Equal(program, NestWriter.GetProgramText(loaded.Plates[0].Parts[0].Program));
        Assert.Null(loaded.Plates[0].Parts[0].CuttingParameters);
        Assert.False(loaded.Plates[1].Parts[0].HasManualLeadIns);
    }

    [Fact]
    public void LegacyPlateWithoutCuttingParameters_KeepsNull()
    {
        var bytes = EditArchive(Save(CreateNest()), zip =>
        {
            var json = JsonNode.Parse(EntryText(zip, "nest.json"))!;
            json["plates"]![0]!.AsObject().Remove("cuttingParameters");
            ReplaceEntry(zip, "nest.json", json.ToJsonString());
        });
        Assert.Null(Read(bytes).Plates[0].CuttingParameters);
    }

    [Fact]
    public void CIFiberPost_MatchesBeforeAndAfterRoundTrip()
    {
        var nest = CreateNest();
        Assert.Equal(Post(nest), Post(Read(Save(nest))));
    }

    private static string Post(Nest nest)
    {
        using var stream = new MemoryStream();
        new CIFiberPostProcessor(new CIFiberPostConfig()).Post(nest, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static byte[] EditArchive(byte[] bytes, Action<ZipArchive> edit)
    {
        using var stream = new MemoryStream();
        stream.Write(bytes);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            edit(zip);
        return stream.ToArray();
    }

    private static string EntryText(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static void ReplaceEntry(ZipArchive zip, string name, string text)
    {
        zip.GetEntry(name)?.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    private static void AssertClean(Part expected, Part actual)
    {
        Assert.False(actual.HasManualLeadIns);
        Assert.False(actual.LeadInsLocked);
        Assert.Null(actual.CuttingParameters);
        Assert.Equal(expected.Location, actual.Location);
        Assert.Equal(expected.Rotation, actual.Rotation, 8);
        AssertBox(expected.BoundingBox, actual.BoundingBox);
        Assert.Equal(NestWriter.GetProgramText(expected.Program), NestWriter.GetProgramText(actual.Program));
    }

    private static CuttingParameters Parameters() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 },
        ExternalLeadOut = new LineLeadOut { Length = 0.25 },
        ArcCircleLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 90 },
        TabsEnabled = true,
        TabConfig = new NormalTab { Size = 0.15 },
    };

    private static Nest CreateNest()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(0, 10));
        program.Codes.Add(new LinearMove(10, 10));
        program.Codes.Add(new LinearMove(10, 0));
        program.Codes.Add(new LinearMove(0, 0));
        program.Codes.Add(new RapidMove(6, 5));
        program.Codes.Add(new ArcMove(new Vector(6, 5), new Vector(5, 5), RotationType.CW));
        var drawing = new Drawing("square-with-hole", program);
        var part = new Part(drawing);
        part.Rotate(System.Math.PI / 2);
        part.Offset(20, 5);
        part.ApplyLeadIns(Parameters(), new Vector(-5, -5));
        part.LeadInsLocked = true;
        var nest = new Nest { Name = "lead-in-round-trip" };
        nest.Drawings.Add(drawing);
        var plate = new Plate { Size = new Size(48, 96) };
        plate.Parts.Add(part);
        nest.Plates.Add(plate);
        return nest;
    }

    private static byte[] Save(Nest nest)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static Nest Read(byte[] bytes) => new NestReader(new MemoryStream(bytes)).Read();

    private static void AssertBox(Box expected, Box actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
        Assert.Equal(expected.Length, actual.Length, 8);
        Assert.Equal(expected.Width, actual.Width, 8);
    }
}
