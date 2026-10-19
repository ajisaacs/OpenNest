using System.Linq;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;

namespace OpenNest.Tests.Cincinnati;

/// <summary>
/// Posts real lead-in programs (incremental hole sub-programs, per-drawing hole ids)
/// for off-origin parts of several drawings, then replays the G-code to check where
/// each hole is actually cut.
/// </summary>
public class HolePlacementRegressionTests
{
    private static readonly Vector HoleCenter = new(3, 3);

    [Fact]
    public void InlineSheet_HolesCutAtPartLocationWithOwnDrawingRadius()
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        var large = AddPart(plate, "Large", 0.5, new Vector(10, 20));
        var small = AddPart(plate, "Small", 0.25, new Vector(30, 20));
        nest.Plates.Add(plate);

        var run = CincinnatiReplay.Run(CincinnatiReplay.Post(nest, partSubprograms: false));

        AssertHole(run, large, 0.5);
        AssertHole(run, small, 0.25);
        Assert.Equal(2, run.FullCircles.Count);
    }

    [Fact]
    public void PartSubprograms_HolesCutAtPartLocationWithOwnDrawingRadius()
    {
        // One part per sheet: each sheet re-anchors with G92 X#5021 Y#5022.
        var nest = new Nest("N");
        var first = new Plate(48, 96);
        var second = new Plate(48, 96);
        var large = AddPart(first, "Large", 0.5, new Vector(10, 20));
        var small = AddPart(second, "Small", 0.25, new Vector(30, 40));
        nest.Plates.Add(first);
        nest.Plates.Add(second);

        var run = CincinnatiReplay.Run(CincinnatiReplay.Post(nest, partSubprograms: true));

        AssertHole(run, large, 0.5);
        AssertHole(run, small, 0.25);
        Assert.Equal(2, run.FullCircles.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameDrawingHoleIds_DifferentRadii_PostSeparateSubprograms(bool partSubprograms)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        var large = AddPart(plate, "Large", 0.5, new Vector(10, 20));
        var small = AddPart(plate, "Small", 0.25, new Vector(30, 20));
        nest.Plates.Add(plate);
        // Planned emission labels holes 1, 2, ... within each drawing's program.
        HoleCall(large).Id = 1;
        HoleCall(small).Id = 1;

        var output = CincinnatiReplay.Post(nest, partSubprograms);

        var holeSubs = output.Split('\n').Count(l => l.Trim() == "( PART: HOLE )");
        Assert.Equal(2, holeSubs);
        var run = CincinnatiReplay.Run(output);
        AssertHole(run, large, 0.5);
        AssertHole(run, small, 0.25);
    }

    [Fact]
    public void InlineSheet_PartStartingWithHole_KeepsPartCommentAndSafetyHeadraiseBeforeItsRapid()
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        AddPart(plate, "First", 0.5, new Vector(0, 0));
        AddPart(plate, "Second", 0.5, new Vector(20, 0));
        nest.Plates.Add(plate);

        var lines = CincinnatiReplay.Post(nest, partSubprograms: false).Split('\n').Select(l => l.Trim()).ToList();

        var second = lines.IndexOf("( PART: Second )");
        Assert.True(second > 0);
        Assert.EndsWith("G52 X23 Y3", lines[second - 1]);
        Assert.Single(lines, l => l == "( PART: First )");
        // The safety raise ends the previous part, before the rapid to this one.
        Assert.Equal("M47 P2000 (Safety Headraise)", lines[second - 2]);
        Assert.Equal("M47", lines[second + 3]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Post_PartsStartingWithHoleCall_HaveNoEmptyPierce(bool partSubprograms)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        var part = AddPart(plate, "Large", 0.5, new Vector(10, 20));
        nest.Plates.Add(plate);
        Assert.IsType<SubProgramCall>(part.Program.Codes[0]);

        var run = CincinnatiReplay.Run(CincinnatiReplay.Post(nest, partSubprograms));

        Assert.Equal(0, run.EmptyPierces);
        Assert.Equal(2, run.Pierces);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Post_LeavesNestHoleProgramsUntouched(bool partSubprograms)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        var part = AddPart(plate, "Large", 0.5, new Vector(10, 20));
        nest.Plates.Add(plate);
        var hole = HoleCall(part).Program;
        var before = hole.Codes.Count;

        CincinnatiReplay.Post(nest, partSubprograms);

        Assert.Equal(Mode.Incremental, hole.Mode);
        Assert.Equal(before, hole.Codes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Post_FollowsPlateSequence(bool partSubprograms)
    {
        var nest = new Nest("N");
        var plate = new Plate(48, 96);
        // Plate order deliberately differs from bottom-then-left order.
        AddPart(plate, "Third", 0.5, new Vector(30, 40));
        AddPart(plate, "First", 0.5, new Vector(0, 0));
        AddPart(plate, "Second", 0.5, new Vector(20, 0));
        nest.Plates.Add(plate);

        var output = CincinnatiReplay.Post(nest, partSubprograms);

        var sheet = output[output.IndexOf(":101", System.StringComparison.Ordinal)..];
        var order = sheet
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("( PART: ", System.StringComparison.Ordinal))
            .Take(3)
            .ToArray();
        Assert.Equal(new[] { "( PART: Third )", "( PART: First )", "( PART: Second )" }, order);
    }

    internal static Part AddPart(Plate plate, string name, double holeRadius, Vector location)
    {
        var pgm = new Program(Mode.Absolute);
        pgm.Codes.Add(new RapidMove(0, 0));
        pgm.Codes.Add(new LinearMove(10, 0));
        pgm.Codes.Add(new LinearMove(10, 10));
        pgm.Codes.Add(new LinearMove(0, 10));
        pgm.Codes.Add(new LinearMove(0, 0));
        var start = new Vector(HoleCenter.X + holeRadius, HoleCenter.Y);
        pgm.Codes.Add(new RapidMove(start));
        pgm.Codes.Add(new ArcMove(start, HoleCenter, RotationType.CW));

        var part = new Part(new Drawing(name) { Program = pgm });
        part.ApplyLeadIns(
            new CuttingParameters
            {
                ArcCircleLeadIn = new LineLeadIn { Length = 0.125, ApproachAngle = 90 },
                ArcCircleLeadOut = new NoLeadOut(),
                ExternalLeadIn = new LineLeadIn { Length = 0.25, ApproachAngle = 90 },
                ExternalLeadOut = new NoLeadOut(),
            },
            new Vector(10, 10)
        );
        Assert.Equal(Mode.Incremental, HoleCall(part).Program.Mode);
        part.Location = location;
        plate.Parts.Add(part);
        return part;
    }

    internal static SubProgramCall HoleCall(Part part) =>
        part.Program.Codes.OfType<SubProgramCall>().Single();

    internal static void AssertHole(CincinnatiReplay.Result run, Part part, double radius)
    {
        var expected = part.Location + HoleCenter;
        Assert.Contains(
            run.FullCircles,
            c => c.Center.DistanceTo(expected) < 1e-3 && System.Math.Abs(c.Radius - radius) < 1e-3
        );
    }
}
