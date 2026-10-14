using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.IO;

/// <summary>
/// Nests converted from PEP carry etch marks only in each placed part's locked cutting program;
/// the drawing has none. Removing lead-ins rebuilds a part from its drawing, so loading such a
/// nest must give the marks back to the drawing or clearing lead-ins erases them.
/// </summary>
public class ConvertedEtchMarkTests
{
    private const double HalfTurn = System.Math.PI;
    private const double QuarterTurn = System.Math.PI / 2;

    [Theory]
    [InlineData(Mode.Incremental)]
    [InlineData(Mode.Absolute)]
    public void ClearingLeadInsKeepsEtchMarksOfConvertedDrawing(Mode mode)
    {
        var loaded = Read(Save(ConvertedNest(mode)));
        var parts = loaded.Plates[0].Parts;

        AppliedCuttingWork.Clear(loaded.Plates.ToList());

        Assert.All(parts, part => Assert.False(part.HasManualLeadIns));
        AssertMarks(Expected(0, new Vector(0, 0)), parts[0]);
        AssertMarks(Expected(QuarterTurn, new Vector(30, 2)), parts[1]);
        AssertMarks(Expected(HalfTurn, new Vector(50, 10)), parts[2]);
    }

    /// <summary>
    /// Turning a mark is not symmetric, so a slanted placement proves the marks are put back in
    /// the drawing's own frame rather than the frame of the placement they were read from.
    /// </summary>
    [Theory]
    [InlineData(Mode.Incremental)]
    [InlineData(Mode.Absolute)]
    public void MarksOfASlantedPlacementAreTurnedBackIntoTheDrawingFrame(Mode mode)
    {
        const double slant = System.Math.PI / 6;
        var drawing = new Drawing("converted", Build(marks: false, mode));
        var clean = new Part(drawing);
        clean.Rotate(slant);
        clean.Offset(12, 7);
        var nest = NestOf(drawing, SavedPart(drawing, Build(marks: true, mode), slant, new Vector(12, 7)), clean);

        var loaded = Read(Save(nest));
        var parts = loaded.Plates[0].Parts;

        Assert.Equal(2, MarkMoves(loaded.Drawings.Single().Program));
        var inDrawingFrame = MarkSamples(loaded.Drawings.Single().Program, Vector.Zero);
        Assert.Equal(MarkSamples(Build(marks: true, Mode.Incremental), Vector.Zero),
            inDrawingFrame, VectorSame);
        AssertMarks(Expected(slant, new Vector(12, 7)), parts[1]);
        AppliedCuttingWork.Clear(loaded.Plates.ToList());
        AssertMarks(Expected(slant, new Vector(12, 7)), parts[0]);
        AssertMarks(Expected(slant, new Vector(12, 7)), parts[1]);
    }

    [Fact]
    public void LoadingGivesTheMarksToCleanPartsAndLeavesSavedProgramsAlone()
    {
        var nest = ConvertedNest(Mode.Incremental);
        var saved = nest.Plates[0].Parts.Take(2).Select(p => NestWriter.GetProgramText(p.Program)).ToArray();

        var loaded = Read(Save(nest));
        var parts = loaded.Plates[0].Parts;

        Assert.Equal(2, MarkMoves(loaded.Drawings.Single().Program));
        Assert.True(parts[0].LeadInsLocked);
        Assert.Equal(saved[0], NestWriter.GetProgramText(parts[0].Program));
        Assert.Equal(saved[1], NestWriter.GetProgramText(parts[1].Program));
        Assert.False(parts[2].HasManualLeadIns);
        AssertMarks(Expected(HalfTurn, new Vector(50, 10)), parts[2]);
    }

    [Fact]
    public void RecoveredMarksAreSavedOnceAndKeepTheSavedPrograms()
    {
        var once = Read(Save(ConvertedNest(Mode.Incremental)));
        var text = NestWriter.GetProgramText(once.Drawings.Single().Program);

        var twice = Read(Save(once));

        Assert.Equal(text, NestWriter.GetProgramText(twice.Drawings.Single().Program));
        Assert.Equal(2, MarkMoves(twice.Drawings.Single().Program));
        Assert.True(twice.Plates[0].Parts[0].HasManualLeadIns);
        Assert.True(twice.Plates[0].Parts[1].HasManualLeadIns);
    }

    [Fact]
    public void DrawingThatAlreadyHasMarksIsUnchanged()
    {
        var drawing = new Drawing("imported", Build(marks: true, Mode.Incremental));
        var text = NestWriter.GetProgramText(drawing.Program);
        var part = new Part(drawing);
        part.Rotate(QuarterTurn);
        part.Offset(30, 2);
        part.ApplyLeadIns(new OpenNest.CNC.CuttingStrategy.CuttingParameters(), new Vector(-5, -5));
        var nest = NestOf(drawing, part);

        var loaded = Read(Save(nest));
        AppliedCuttingWork.Clear(loaded.Plates.ToList());

        Assert.Equal(text, NestWriter.GetProgramText(loaded.Drawings.Single().Program));
        AssertMarks(Expected(QuarterTurn, new Vector(30, 2)), loaded.Plates[0].Parts[0]);
    }

    [Fact]
    public void PartsWithDifferentMarksLeaveTheDrawingWithout()
    {
        var drawing = new Drawing("converted", Build(marks: false, Mode.Incremental));
        var withMarks = SavedPart(drawing, Build(marks: true, Mode.Incremental), 0, new Vector(0, 0));
        var other = Build(marks: true, Mode.Incremental);
        other.Codes.RemoveAt(other.Codes.FindLastIndex(code => code is ArcMove));
        var withLineOnly = SavedPart(drawing, other, 0, new Vector(20, 0));

        var reader = new NestReader(new MemoryStream(Save(NestOf(drawing, withMarks, withLineOnly))));
        var loaded = reader.Read();

        Assert.Empty(reader.Warnings);
        Assert.Equal(0, MarkMoves(loaded.Drawings.Single().Program));
        Assert.True(loaded.Plates[0].Parts.All(p => p.HasManualLeadIns));
    }

    /// <summary>
    /// A converted nest: the drawing is material only, two placements keep the full PEP cutting
    /// program (etch line and etch circle) at different turns, and one placement is clean.
    /// </summary>
    private static Nest ConvertedNest(Mode mode)
    {
        var drawing = new Drawing("converted", Build(marks: false, mode));
        var clean = new Part(drawing);
        clean.Rotate(HalfTurn);
        clean.Offset(50, 10);
        return NestOf(drawing,
            SavedPart(drawing, Build(marks: true, mode), 0, new Vector(0, 0)),
            SavedPart(drawing, Build(marks: true, mode), QuarterTurn, new Vector(30, 2)),
            clean);
    }

    private static Part SavedPart(Drawing drawing, Program cutting, double rotation, Vector location)
    {
        var part = new Part(drawing);
        part.Rotate(rotation);
        part.Offset(location);
        var program = (Program)cutting.Clone();
        program.Rotate(rotation);
        Assert.True(part.RestoreLeadInProgram(program, locked: true));
        return part;
    }

    /// <summary>A 10 x 6 plate with an etch line and an etch circle; without marks they are rapids.</summary>
    private static Program Build(bool marks, Mode mode)
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(2, 3));
        program.Codes.Add(marks ? new LinearMove(4, 3) { Layer = LayerType.Scribe } : new RapidMove(4, 3));
        program.Codes.Add(new RapidMove(7.5, 3));
        program.Codes.Add(marks
            ? new ArcMove(new Vector(7.5, 3), new Vector(7, 3), RotationType.CCW) { Layer = LayerType.Scribe }
            : new RapidMove(7.5, 3));
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(10, 0));
        program.Codes.Add(new LinearMove(10, 6));
        program.Codes.Add(new LinearMove(0, 6));
        program.Codes.Add(new LinearMove(0, 0));
        program.Mode = mode;
        return program;
    }

    private static Nest NestOf(Drawing drawing, params Part[] parts)
    {
        var nest = new Nest { Name = "converted-etch" };
        nest.Drawings.Add(drawing);
        var plate = new Plate { Size = new Size(48, 96) };
        foreach (var part in parts)
            plate.Parts.Add(part);
        nest.Plates.Add(plate);
        return nest;
    }

    /// <summary>World-space mark samples of the full program turned and placed like a part.</summary>
    private static List<Vector> Expected(double rotation, Vector location)
    {
        var program = Build(marks: true, Mode.Incremental);
        program.Rotate(rotation);
        return MarkSamples(program, location);
    }

    private static void AssertMarks(List<Vector> expected, Part part)
    {
        var actual = MarkSamples(part.Program, part.Location);
        Assert.Equal(expected.Count, actual.Count);
        foreach (var point in expected)
        {
            var index = actual.FindIndex(p => p.DistanceTo(point) <= 1e-4);
            Assert.True(index >= 0, $"No etch mark at {point} on the part at {part.Location}.");
            actual.RemoveAt(index);
        }
    }

    // Turn-independent samples: line ends, and each circle's centre and radius.
    private static List<Vector> MarkSamples(Program program, Vector location) =>
        ConvertProgram.ToGeometry(program)
            .Where(entity => entity.Layer == SpecialLayers.Scribe)
            .SelectMany(entity => entity switch
            {
                Line line => new[] { line.StartPoint + location, line.EndPoint + location },
                Circle circle => new[] { circle.Center + location, new Vector(circle.Radius, 0) },
                Arc arc => new[] { arc.StartPoint() + location, arc.EndPoint() + location },
                _ => Array.Empty<Vector>(),
            })
            .ToList();

    private static bool VectorSame(Vector left, Vector right) => left.DistanceTo(right) <= 1e-6;

    private static int MarkMoves(Program program) =>
        program.Codes.Count(code => code is LinearMove { Layer: LayerType.Scribe } or ArcMove { Layer: LayerType.Scribe });

    private static byte[] Save(Nest nest)
    {
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        return stream.ToArray();
    }

    private static Nest Read(byte[] bytes) => new NestReader(new MemoryStream(bytes)).Read();
}
