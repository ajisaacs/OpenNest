using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Plan Cutting raises the head over cut parts only as a last resort: a route that keeps the
/// head down always wins, and only the rapids no route could keep clear are marked.
/// </summary>
public class HeadRaisePlanningTests
{
    [Fact]
    public void KeptOrderThatMustCrossAWall_IsReadyWithOneRaise()
    {
        // A tall wall is cut first, then the square left of it, then the one right of it:
        // every rapid from the left square to the right one passes over the wall.
        var (nest, plate) = WallPlate();

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), preservePartOrder: true).Plan();
        var result = proposal.Plates[0].Result;

        Assert.True(proposal.Plates[0].IsReady, string.Join("\n", proposal.Describe("in")));
        Assert.Equal(1, result.HeadRaises);
        Assert.False(result.IsSafestRouteFallback);
        var note = Assert.Single(result.Findings);
        Assert.Contains("raises the head", note.Message);
        Assert.Equal(0, note.OtherSourceOrdinal); // The wall.
        Assert.Equal(new[] { false, false, true },
            result.ProposedOrder.Select(p => HeadRaise.IsMarked(p.CopyProgram())));
        Assert.Contains(proposal.Describe("in"), line => line.Contains("1 rapid raises the head"));

        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);
        Assert.True(HeadRaise.IsMarked(plate.Parts[2].Program));
        // The pre-post check accepts the raised crossing; nothing else crosses.
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(nest, Vector.Zero).Findings,
            f => f.Kind == PostVerificationKind.RapidCrossing);
    }

    [Fact]
    public void FreeOrder_CutsAroundTheWall_WithoutRaisingTheHead()
    {
        var (_, plate) = WallPlate();

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), preservePartOrder: false).Plan();
        var result = proposal.Plates[0].Result;

        Assert.True(proposal.Plates[0].IsReady, string.Join("\n", proposal.Describe("in")));
        Assert.Equal(0, result.HeadRaises);
        Assert.All(result.ProposedOrder, p => Assert.False(HeadRaise.IsMarked(p.CopyProgram())));
    }

    [Fact]
    public void StrictPlan_NeverMarksARapid()
    {
        var (_, plate) = WallPlate();

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters(), preservePartOrder: true));

        Assert.NotEqual(CuttingPlanStatus.Ready, result.Status);
        Assert.Equal(0, result.HeadRaises);
    }

    [Fact]
    public void Checker_SetsARaisedRapidsCrossingsApart()
    {
        var wall = LeadPathValidationTests.Rectangle(3, 0, 5, 22);
        var rapid = new Program();
        rapid.Codes.Add(new Comment(HeadRaise.Marker));
        rapid.MoveTo(8, 11);
        rapid.LineTo(8, 12);
        var checker = new ReleasedContourState(reportMissingLeadIns: false);
        checker.Check(ExecutionMotionReader.Read(wall, Vector.Zero, null, default), null, 1);

        var execution = ExecutionMotionReader.Read(rapid, Vector.Zero, new Vector(0, 11), default);
        var (strict, _, raised) = checker.Check(execution, new Vector(0, 11), 2, false, null, default);

        Assert.True(execution.Motions[0].HeadRaised);
        Assert.Equal(1, execution.Motions[0].Code);
        Assert.Empty(strict);
        Assert.Contains(raised, f => f.Kind == PostVerificationKind.RapidCrossing && f.OtherPartNumber == 1);
    }

    [Fact]
    public void Marker_MustBeFollowedByARapid()
    {
        var program = new Program();
        program.Codes.Add(new Comment(HeadRaise.Marker));
        program.LineTo(1, 1);

        Assert.Throws<ArgumentException>(() => HeadRaise.RaisedMask(program.Codes));
        Assert.Throws<ArgumentException>(() => ExecutionMotionReader.Read(program, Vector.Zero, null, default));
    }

    [Fact]
    public void Mark_CopiesTheProgramWithAMarkerBeforeEachRapid()
    {
        var program = LeadPathValidationTests.Rectangle(0, 0, 2, 2);

        var marked = HeadRaise.Mark(program, [0]);

        Assert.False(HeadRaise.IsMarked(program));
        Assert.True(HeadRaise.IsMarker(marked.Codes[0]));
        Assert.IsType<RapidMove>(marked.Codes[1]);
        Assert.Throws<ArgumentException>(() => HeadRaise.Mark(marked, [1])); // Already marked.
        Assert.Throws<ArgumentException>(() => HeadRaise.Mark(program, [1])); // Not a rapid.
    }

    // Plate order: the wall, the square left of it, the square right of it.
    private static (Nest Nest, Plate Plate) WallPlate()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(40, 40);
        plate.Parts.Add(new Part(new Drawing("wall", LeadPathValidationTests.Rectangle(0, 0, 2, 22)), new Vector(5, 1)));
        plate.Parts.Add(new Part(new Drawing("left", LeadPathValidationTests.Rectangle(0, 0, 2, 2)), new Vector(1, 11)));
        plate.Parts.Add(new Part(new Drawing("right", LeadPathValidationTests.Rectangle(0, 0, 2, 2)), new Vector(9, 11)));
        return (nest, plate);
    }
}
