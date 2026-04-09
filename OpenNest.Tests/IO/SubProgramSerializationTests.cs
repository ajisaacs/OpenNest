using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.IO;

public class SubProgramSerializationTests
{
    [Fact]
    public void NestWriter_WritesSubProgramCall_WithOffset()
    {
        var nest = CreateNestWithHoleSubProgram();

        using var stream = new MemoryStream();
        var writer = new NestWriter(nest);
        writer.Write(stream);
        stream.Position = 0;

        var reader = new NestReader(stream);
        var loaded = reader.Read();

        var drawing = loaded.Drawings.First();
        var calls = drawing.Program.Codes.OfType<SubProgramCall>().ToList();
        Assert.Single(calls);
        Assert.Equal(5, calls[0].Offset.X, 1);
        Assert.Equal(5, calls[0].Offset.Y, 1);
    }

    [Fact]
    public void NestWriter_WritesSubPrograms_AndRestoresOnLoad()
    {
        var nest = CreateNestWithHoleSubProgram();

        using var stream = new MemoryStream();
        var writer = new NestWriter(nest);
        writer.Write(stream);
        stream.Position = 0;

        var reader = new NestReader(stream);
        var loaded = reader.Read();

        var drawing = loaded.Drawings.First();
        Assert.True(drawing.Program.SubPrograms.Count > 0);

        var call = drawing.Program.Codes.OfType<SubProgramCall>().First();
        Assert.True(drawing.Program.SubPrograms.ContainsKey(call.Id));
    }

    private static Nest CreateNestWithHoleSubProgram()
    {
        var sub = new Program(Mode.Incremental);
        sub.Codes.Add(new LinearMove(0.1, 0) { Layer = LayerType.Leadin });
        sub.Codes.Add(new ArcMove(new Vector(0, 0), new Vector(-0.5, 0), RotationType.CW));

        var pgm = new Program(Mode.Absolute);
        pgm.SubPrograms[42] = sub;
        pgm.Codes.Add(new SubProgramCall { Id = 42, Program = sub, Offset = new Vector(5, 5) });
        // Add perimeter so the drawing has non-zero geometry
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(10, 10));
        pgm.Codes.Add(new LinearMove(0, 10));
        pgm.Codes.Add(new LinearMove(0, 0));

        var drawing = new Drawing("TestPart") { Program = pgm };
        var nest = new Nest();
        nest.Drawings.Add(drawing);

        var plate = new Plate { Size = new Size(48, 96) };
        plate.Parts.Add(new Part(drawing));
        nest.Plates.Add(plate);

        return nest;
    }
}
