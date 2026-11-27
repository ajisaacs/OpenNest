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
    public void KeptOrderThatMustCrossAWallIntoAHole_RaisesAtTheHolesCall()
    {
        // The part right of the wall starts with its round hole, cut as a sub-program, so the
        // rapid over the wall arrives inside that sub-program: its call carries the raise.
        var (nest, plate) = WallPlate(holedRight: true);

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), preservePartOrder: true).Plan();
        var result = proposal.Plates[0].Result;

        Assert.True(proposal.Plates[0].IsReady, string.Join("\n", proposal.Describe("in")));
        Assert.Equal(1, result.HeadRaises);
        var codes = result.ProposedOrder[2].CopyProgram().Codes;
        var marker = codes.FindIndex(HeadRaise.IsMarker);
        Assert.IsType<SubProgramCall>(codes[marker + 1]);
        Assert.All(codes.OfType<SubProgramCall>(), call => Assert.False(HeadRaise.IsMarked(call.Program)));

        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);
        Assert.DoesNotContain(PostVerificationAnalyzer.Analyze(nest, Vector.Zero).Findings,
            f => f.Kind == PostVerificationKind.RapidCrossing);
    }

    [Fact]
    public void Checker_SetsTheRapidIntoAMarkedCallApart()
    {
        var wall = LeadPathValidationTests.Rectangle(3, 0, 5, 22);
        var program = new Program();
        program.Codes.Add(new Comment(HeadRaise.Marker));
        program.Codes.Add(new SubProgramCall { Program = Hole(), Offset = new Vector(8, 11) });
        var checker = new ReleasedContourState(reportMissingLeadIns: false);
        checker.Check(ExecutionMotionReader.Read(wall, Vector.Zero, null, default), null, 1);

        var execution = ExecutionMotionReader.Read(program, Vector.Zero, new Vector(0, 11), default);
        var (strict, _, raised, _) = checker.Check(execution, new Vector(0, 11), 2, false, null, default);

        Assert.True(execution.Motions[0].HeadRaised);
        Assert.Null(execution.Motions[0].Code);
        Assert.Equal(1, execution.Motions[0].CallCode);
        Assert.DoesNotContain(execution.Motions.Skip(1), m => m.HeadRaised);
        Assert.Empty(strict);
        Assert.Contains(raised, f => f.Kind == PostVerificationKind.RapidCrossing && f.OtherPartNumber == 1);
    }

    [Fact]
    public void Mark_MarksASubProgramCall()
    {
        var program = new Program();
        program.Codes.Add(new SubProgramCall { Program = Hole(), Offset = new Vector(1, 1) });

        var marked = HeadRaise.Mark(program, [0]);

        Assert.True(HeadRaise.IsMarker(marked.Codes[0]));
        Assert.IsType<SubProgramCall>(marked.Codes[1]);
        Assert.Equal(new[] { false, true }, HeadRaise.RaisedMask(marked.Codes));
        Assert.Throws<ArgumentException>(() => HeadRaise.Mark(marked, [1])); // Already marked.
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
    public void Repair_CutsThePartBeyondTheWallBeforeIt()
    {
        // A safest route that cuts the wall, the left square, then the right one raises the head
        // over the wall; cutting the right square before the wall keeps it down.
        var (_, plate) = WallPlate();
        var snapshot = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters(), preservePartOrder: false), default);
        var order = new JointCuttingPlanSearch.Resequencer(snapshot, default).Cut([], [0, 1, 2], 0, 2);
        var raised = CuttingPlanService.ReplayPrograms(snapshot, RaisedRapids.Mark(snapshot, order, default), 0,
            default, safestRoute: true);
        Assert.Equal(1, raised.HeadRaises);
        var (index, crossed) = Assert.Single(RaisedRapids.Crossed(snapshot, order, default));
        Assert.Equal(2, index);
        Assert.Equal(new[] { 0 }, crossed); // The wall.

        var repaired = RaisedRapidRepair.Repair(snapshot, order, raised, changed => CuttingPlanService.ReplayPrograms(
            snapshot, RaisedRapids.Mark(snapshot, changed, default), 0, default, safestRoute: true), default);

        Assert.Equal(CuttingPlanStatus.Ready, repaired.Status);
        Assert.Equal(0, repaired.HeadRaises);
        Assert.Equal(new[] { 2, 0, 1 }, repaired.ProposedOrder.Select(p => p.SourceOrdinal));
    }

    [Fact]
    public void Repair_NeverChangesAKeptOrder()
    {
        var (_, plate) = WallPlate();
        var snapshot = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters(), preservePartOrder: true), default);
        var result = CuttingPlanService.PlanSafestRoute(snapshot, default);

        Assert.Equal(1, result.HeadRaises);
        Assert.Equal(new[] { 0, 1, 2 }, result.ProposedOrder.Select(p => p.SourceOrdinal));
    }

    [Fact]
    public void RepairCandidates_MoveThePartOrWhatItCrossed()
    {
        // Part 7 at index 3 crossed part 5 (index 1) and part 9 (index 0).
        int[] sequence = [9, 5, 6, 7, 8];

        var candidates = RaisedRapidRepair.Candidates(sequence, 3, [5, 9]).ToArray();

        Assert.Equal(new[]
        {
            new[] { 7, 9, 5, 6, 8 }, // Before the earliest crossed part.
            new[] { 9, 6, 5, 7, 8 }, // Part 5 just before it,
            new[] { 9, 6, 7, 5, 8 }, // and just after it.
            new[] { 5, 6, 9, 7, 8 },
            new[] { 5, 6, 7, 9, 8 },
        }, candidates);
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
        var (strict, _, raised, _) = checker.Check(execution, new Vector(0, 11), 2, false, null, default);

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

    // Plate order: the wall, the square left of it, the square right of it; with holedRight, the
    // right one is larger and has a round hole in its middle.
    private static (Nest Nest, Plate Plate) WallPlate(bool holedRight = false)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Size = new Size(40, 40);
        plate.Parts.Add(new Part(new Drawing("wall", LeadPathValidationTests.Rectangle(0, 0, 2, 22)), new Vector(5, 1)));
        plate.Parts.Add(new Part(new Drawing("left", LeadPathValidationTests.Rectangle(0, 0, 2, 2)), new Vector(1, 11)));
        var right = LeadPathValidationTests.Rectangle(0, 0, holedRight ? 4 : 2, holedRight ? 4 : 2);
        if (holedRight)
        {
            right.MoveTo(2.5, 2);
            right.ArcTo(2.5, 2, 2, 2, RotationType.CCW);
        }
        plate.Parts.Add(new Part(new Drawing("right", right), new Vector(9, holedRight ? 10 : 11)));
        return (nest, plate);
    }

    // A round hole of radius 0.5 around its sub-program's origin, entered from its center.
    private static Program Hole()
    {
        var hole = new Program();
        hole.MoveTo(0, 0);
        hole.Codes.Add(new LinearMove(0.5, 0) { Layer = LayerType.Leadin });
        hole.ArcTo(0.5, 0, 0, 0, RotationType.CCW);
        return hole;
    }
}
