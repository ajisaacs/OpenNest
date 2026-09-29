using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CNC;

public class PlateRapidEnumeratorTests
{
    [Theory]
    [InlineData(1, CutDirection.AwayFromOrigin)]
    [InlineData(2, CutDirection.AwayFromOrigin)]
    [InlineData(3, CutDirection.AwayFromOrigin)]
    [InlineData(4, CutDirection.AwayFromOrigin)]
    [InlineData(1, CutDirection.TowardOrigin)]
    [InlineData(2, CutDirection.TowardOrigin)]
    [InlineData(3, CutDirection.TowardOrigin)]
    [InlineData(4, CutDirection.TowardOrigin)]
    public void Enumerate_AppliedAutomaticCutoffs_ConnectsFromFinalCut(int quadrant, CutDirection direction)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(70, 0));
        program.Codes.Add(new LinearMove(70, 20));
        program.Codes.Add(new LinearMove(0, 20));
        program.Codes.Add(new LinearMove(0, 0));
        var drawing = new Drawing("rectangle", program);
        var plate = new Plate(81, 120) { Quadrant = quadrant, PartSpacing = 0.5 };
        plate.Parts.Add(new Part(drawing,
            new Vector(quadrant is 2 or 3 ? -80 : 10, quadrant is 3 or 4 ? -40 : 20)));
        var settings = new CutOffSettings { CutDirection = direction, Overtravel = 2 };
        var plan = AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = 35 }, settings);
        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(3, plan.Definitions.Count);
        foreach (var definition in plan.Definitions)
            plate.CutOffs.Add(definition);
        plate.RegenerateCutOffs(settings);

        // Two interrupted skeleton cuts followed by an uninterrupted tail separator.
        var cutoffs = plate.Parts.Where(p => p.BaseDrawing.IsCutOff).ToArray();
        Assert.Equal(new[] { 2, 2, 1 }, cutoffs.Select(p => p.Program.Codes.OfType<LinearMove>().Count()));
        var before = plate.Parts.Select(p => NestWriter.GetProgramText(p.Program)).ToArray();
        var expected = new List<RapidEnumerator.Segment>();
        var position = Vector.Zero;
        foreach (var part in plate.Parts)
        {
            Assert.Equal(Mode.Absolute, part.Program.Mode);
            foreach (var motion in part.Program.Codes.Cast<Motion>())
            {
                var destination = part.Location + motion.EndPoint;
                if (motion is RapidMove)
                    expected.Add(new RapidEnumerator.Segment(position, destination));
                position = destination;
            }
        }

        var actual = RapidEnumerator.Enumerate(plate.Parts);

        Assert.Equal(expected, actual);
        Assert.Equal(before, plate.Parts.Select(p => NestWriter.GetProgramText(p.Program)));
        plate.RegenerateCutOffs(settings);
        Assert.Equal(expected, RapidEnumerator.Enumerate(plate.Parts));
    }

    [Theory]
    [InlineData(Mode.Absolute, false)]
    [InlineData(Mode.Incremental, false)]
    [InlineData(Mode.Absolute, true)]
    [InlineData(Mode.Incremental, true)]
    public void Enumerate_OpenProgram_AdvancesPastLastPierce(Mode mode, bool endWithArc)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(2, 3));
        program.Codes.Add(new LinearMove(7, 3));
        if (endWithArc)
            program.Codes.Add(new ArcMove(8, 4, 7, 4));
        program.Mode = mode;
        var first = new Part(new Drawing("open path", program), new Vector(100, 200));
        var next = NextPart();
        var before = NestWriter.GetProgramText(first.Program);

        var segments = RapidEnumerator.Enumerate(new[] { first, next });

        Assert.Equal(2, segments.Count);
        Assert.Equal(new Vector(102, 203), segments[0].To);
        Assert.Equal(endWithArc ? new Vector(108, 204) : new Vector(107, 203), segments[1].From);
        Assert.Equal(new Vector(12, 23), segments[1].To);
        Assert.Equal(before, NestWriter.GetProgramText(first.Program));
    }

    [Fact]
    public void Enumerate_FinalSubprogram_UsesItsCutEndpointForNextPart()
    {
        var hole = new Program(Mode.Incremental);
        hole.Codes.Add(new RapidMove(0.5, 0));
        hole.Codes.Add(new LinearMove(0, 0.1));
        var program = new Program();
        program.Codes.Add(new RapidMove(1, 0));
        program.Codes.Add(new LinearMove(2, 0));
        program.Codes.Add(new SubProgramCall { Id = 1, Program = hole, Offset = new Vector(2, 2) });
        var first = new Part(new Drawing("hole last", program), new Vector(100, 200));

        var segments = RapidEnumerator.Enumerate(new[] { first, NextPart() });

        Assert.Equal(3, segments.Count);
        Assert.Equal(new Vector(102, 200), segments[1].From);
        Assert.Equal(new Vector(102.5, 202), segments[1].To);
        Assert.Equal(new Vector(102.5, 202.1), segments[2].From);
        Assert.Equal(new Vector(12, 23), segments[2].To);
    }

    [Fact]
    public void Enumerate_TrailingRapid_RemainsTheNextPartsStartPosition()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(1, 0));
        program.Codes.Add(new LinearMove(2, 0));
        program.Codes.Add(new RapidMove(3, 4));
        var first = new Part(new Drawing("park after cut", program), new Vector(100, 200));

        var segments = RapidEnumerator.Enumerate(new[] { first, NextPart() });

        Assert.Equal(3, segments.Count);
        Assert.Equal(new Vector(102, 200), segments[1].From);
        Assert.Equal(new Vector(103, 204), segments[1].To);
        Assert.Equal(new Vector(103, 204), segments[2].From);
    }

    [Fact]
    public void Enumerate_EmptyPlate_HasNoRapids()
    {
        Assert.Empty(RapidEnumerator.Enumerate(Array.Empty<Part>()));
    }

    private static Part NextPart()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(2, 3));
        program.Codes.Add(new LinearMove(4, 3));
        return new Part(new Drawing("next", program), new Vector(10, 20));
    }
}
