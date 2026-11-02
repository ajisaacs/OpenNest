using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

/// <summary>
/// The CL post declares that it preserves the placed order only for a nest whose posted
/// pierces and cuts replay exactly as the pre-post check read them, and withholds it
/// whenever the post would change them.
/// </summary>
public class CincinnatiPlacedOrderTests
{
    [Fact]
    public void ScribesFirst_PostsTheCheckedSequence_AndDeclaresIt()
    {
        var nest = Nest(scribeFirst: true);
        var post = new CincinnatiPostProcessor(CincinnatiReplay.Config(partSubprograms: false));

        Assert.Null(post.PlacedProgramOrderChange(nest));
        Assert.Equal(
            PostVerificationAnalyzer.Analyze(nest).Findings.Select(f => f.Message),
            PostVerificationAnalyzer.AnalyzeForPost(nest, post).Findings.Select(f => f.Message));

        var (pierces, cuts) = Checked(nest);
        Assert.Equal(4, pierces.Count); // scribe, hole, perimeter, square
        var run = CincinnatiReplay.Run(CincinnatiReplay.Post(nest, partSubprograms: false));
        AssertSame(pierces, run.PierceLog.Select(p => p.Point).ToList());
        AssertSame(cuts, run.Cuts);
    }

    [Fact]
    public void ScribeAfterACut_WithholdsTheDeclaration_BecauseThePostCutsItFirst()
    {
        var nest = Nest(scribeFirst: false);
        var post = new CincinnatiPostProcessor(CincinnatiReplay.Config(partSubprograms: false));

        var change = post.PlacedProgramOrderChange(nest);
        Assert.Equal("writes the etch features of part 1 on plate 1 before the cuts placed ahead of them", change);
        var report = PostVerificationAnalyzer.AnalyzeForPost(nest, post);
        Assert.Contains(report.Findings, f => f.Kind == PostVerificationKind.Incomplete && f.Message.Contains(change));
        Assert.False(report.CanPost(false));

        // The withheld declaration is real: the posted pierces differ from the checked ones.
        var (pierces, _) = Checked(nest);
        var run = CincinnatiReplay.Run(CincinnatiReplay.Post(nest, partSubprograms: false));
        Assert.Equal(pierces.Count, run.PierceLog.Count);
        Assert.True(CincinnatiReplay.Near(new Vector(3, 3), run.PierceLog[0].Point));
        Assert.False(CincinnatiReplay.Near(pierces[0], run.PierceLog[0].Point));
    }

    [Fact]
    public void PartSubprograms_WithholdTheDeclaration()
    {
        var post = new CincinnatiPostProcessor(CincinnatiReplay.Config(partSubprograms: true));

        Assert.Contains("part sub-programs", post.PlacedProgramOrderChange(Nest(scribeFirst: true)));
    }

    [Fact]
    public void SmartRapids_WithholdTheDeclaration()
    {
        var config = CincinnatiReplay.Config(partSubprograms: false);
        config.UseSmartRapids = true;

        Assert.Contains("smart rapids", new CincinnatiPostProcessor(config).PlacedProgramOrderChange(Nest(scribeFirst: true)));
    }

    [Fact]
    public void AContourWithoutALeadingRapid_WithholdsTheDeclaration()
    {
        // The post pierces it at its return point instead of wherever the tool already is.
        var program = new Program();
        program.LineTo(4, 0);
        program.LineTo(4, 4);
        program.LineTo(0, 4);
        program.LineTo(0, 0);
        var nest = new Nest();
        nest.CreatePlate().Parts.Add(new Part(new Drawing("square", program), new Vector(2, 2)));
        var post = new CincinnatiPostProcessor(CincinnatiReplay.Config(partSubprograms: false));

        Assert.Equal("adds or drops rapids in part 1 on plate 1", post.PlacedProgramOrderChange(nest));
    }

    // A 20x20 plate part at (1, 1) with a scribe, a hole cut through a hole call and its
    // perimeter, then a 4x4 square at (30, 1). The scribe comes first or after the hole.
    private static Nest Nest(bool scribeFirst)
    {
        var clean = new Program();
        clean.MoveTo(2, 2);
        clean.Codes.Add(new LinearMove(6, 2) { Layer = LayerType.Scribe });
        clean.MoveTo(13, 10);
        clean.ArcTo(13, 10, 10, 10, RotationType.CCW);
        clean.MoveTo(20, 10);
        Outline(clean, 20);

        var hole = new Program();
        hole.MoveTo(0, 0);
        hole.Codes.Add(new LinearMove(3, 0) { Layer = LayerType.Leadin });
        hole.Codes.Add(new ArcMove(new Vector(3, 0), Vector.Zero, RotationType.CCW));
        var placed = new Program();
        if (scribeFirst)
            Scribe(placed);
        placed.Codes.Add(new SubProgramCall { Id = 1, Program = hole, Offset = new Vector(10, 10) });
        if (!scribeFirst)
            Scribe(placed);
        placed.MoveTo(20.5, 10);
        placed.Codes.Add(new LinearMove(20, 10) { Layer = LayerType.Leadin });
        Outline(placed, 20);

        var plate = new Part(new Drawing("plate", clean), new Vector(1, 1));
        Assert.True(plate.RestoreLeadInProgram(placed, false));

        var square = new Program();
        square.MoveTo(4, 2);
        Outline(square, 4);
        var leaded = new Program();
        leaded.MoveTo(4.5, 2);
        leaded.Codes.Add(new LinearMove(4, 2) { Layer = LayerType.Leadin });
        Outline(leaded, 4);
        var second = new Part(new Drawing("square", square), new Vector(30, 1));
        Assert.True(second.RestoreLeadInProgram(leaded, false));

        var nest = new Nest();
        var sheet = nest.CreatePlate();
        sheet.Size = new Size(60, 120);
        sheet.Parts.Add(plate);
        sheet.Parts.Add(second);
        return nest;

        static void Scribe(Program program)
        {
            program.MoveTo(2, 2);
            program.Codes.Add(new LinearMove(6, 2) { Layer = LayerType.Scribe });
        }

        // A size x size square cut counterclockwise from its right-edge midpoint.
        static void Outline(Program program, double size)
        {
            program.LineTo(size, size);
            program.LineTo(0, size);
            program.LineTo(0, 0);
            program.LineTo(size, 0);
            program.LineTo(size, size / 2);
        }
    }

    // The pierces and cuts the pre-post check reads, in plate order and plate coordinates.
    private static (List<Vector> Pierces, List<(Vector From, Vector To)> Cuts) Checked(Nest nest)
    {
        var pierces = new List<Vector>();
        var cuts = new List<(Vector, Vector)>();
        Vector? position = Vector.Zero;
        foreach (var part in nest.Plates[0].Parts)
        {
            var moves = ExecutionMotionReader.Read(part.Program, part.Location, position, default).Motions;
            for (var i = 0; i < moves.Count; i++)
            {
                if (!moves[i].Rapid)
                    cuts.Add((moves[i].Start!.Value, moves[i].End));
                else if (i + 1 < moves.Count && !moves[i + 1].Rapid)
                    pierces.Add(moves[i].End);
            }
            position = moves[^1].End;
        }
        return (pierces, cuts);
    }

    private static void AssertSame(List<Vector> expected, List<Vector> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.True(CincinnatiReplay.Near(expected[i], actual[i]), $"Pierce {i}: {expected[i]} vs {actual[i]}");
    }

    private static void AssertSame(List<(Vector From, Vector To)> expected, List<(Vector From, Vector To)> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.True(
                CincinnatiReplay.Near(expected[i].From, actual[i].From) && CincinnatiReplay.Near(expected[i].To, actual[i].To),
                $"Cut {i}: {expected[i]} vs {actual[i]}");
    }
}
