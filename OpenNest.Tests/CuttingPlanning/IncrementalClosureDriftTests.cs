using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class IncrementalClosureDriftTests
{
    // Converted incremental programs round every move, so a hole can end (1e-6, 1e-6) off its start.
    private const double Drift = 0.000001;

    [Theory]
    [InlineData(Drift, Drift)] // Beyond the closure tolerance: material capture refuses it.
    [InlineData(Drift * 0.6, Drift * 0.6)] // Inside it: closed too, so no stage disagrees at the boundary.
    public void DriftedHole_EndsOnItsSeamWithoutMovingLaterMotions(double dx, double dy)
    {
        var program = Drifted(dx, dy);
        var before = Ends(program);

        Assert.Equal(1, IncrementalClosureDrift.Close(program));

        var after = Ends(program);
        var hole = Cuts(program).Take(8).ToArray();
        Assert.True(hole[0].Start!.Value.DistanceTo(hole[^1].End) <= 1e-12);
        // The rapid absorbs the correction: the perimeter and everything after it stay put.
        Assert.All(before.Zip(after).Skip(9), pair => Assert.True(pair.First.DistanceTo(pair.Second) <= 1e-12));
        Assert.Equal(Mode.Incremental, program.Mode);
        var material = LeadMaterialSnapshot.Capture(program, new Vector(30, 20));
        Assert.True(material.IsComplete, material.Reason);
        Assert.Equal(2, PreparedContours.Capture(program, ExplicitContourTests.Parameters()).Count);
        Assert.Equal(0, IncrementalClosureDrift.Close(program));
    }

    [Fact]
    public void ArcLastMove_IsMovedOntoTheSeamOnItsOwnCircle()
    {
        var program = new Program();
        program.MoveTo(14, 3);
        program.LineTo(20, 3);
        program.LineTo(20, 7);
        program.LineTo(14, 7);
        program.ArcTo(new Vector(14 + Drift, 3 + Drift), new Vector(14, 5), RotationType.CCW);
        program.Mode = Mode.Incremental;
        Assert.False(LeadMaterialSnapshot.Capture(program, Vector.Zero).IsComplete);

        Assert.Equal(1, IncrementalClosureDrift.Close(program));

        var arc = Assert.IsType<ArcMove>(program.Codes[^1]);
        var start = new Vector(14, 7);
        var center = start + arc.CenterPoint;
        var end = start + arc.EndPoint;
        Assert.True(end.DistanceTo(new Vector(14, 3)) <= 1e-12);
        Assert.True(System.Math.Abs(center.DistanceTo(start) - center.DistanceTo(end)) <= 1e-12);
        var material = LeadMaterialSnapshot.Capture(program, Vector.Zero);
        Assert.True(material.IsComplete, material.Reason);
    }

    [Fact]
    public void AbsoluteOrFarOpenContours_AreLeftAlone()
    {
        var absolute = Drifted(Drift, Drift, Mode.Absolute);
        var far = Drifted(0.00002, 0);
        var absoluteBefore = OwnedProgramCopy.Copy(absolute);
        var farBefore = OwnedProgramCopy.Copy(far);

        Assert.Equal(0, IncrementalClosureDrift.Close(absolute));
        Assert.Equal(0, IncrementalClosureDrift.Close(far));

        Assert.True(ProgramContent.Equal(absoluteBefore, absolute));
        Assert.True(ProgramContent.Equal(farBefore, far));
    }

    [Fact]
    public void Planner_ClosesDriftOnItsOwnedCopyOnly()
    {
        var part = new Part(new Drawing("drifted hole", Drifted(Drift, Drift)), new Vector(10, 10));
        var original = OwnedProgramCopy.Copy(part.BaseDrawing.Program);

        var proposal = CuttingPlanBatch.Capture([BestEffortCuttingPlanTests.Plate(part)],
            ExplicitContourTests.Parameters(), false).Plan();

        Assert.True(proposal.CanApply, string.Join(Environment.NewLine, proposal.Describe("in")));
        Assert.True(ProgramContent.Equal(original, part.BaseDrawing.Program));
    }

    // An eight-move hole whose last move misses its start by (dx, dy), then the perimeter.
    private static Program Drifted(double dx, double dy, Mode mode = Mode.Incremental)
    {
        var program = new Program();
        program.MoveTo(4, 4);
        program.LineTo(5, 4);
        program.LineTo(6, 4);
        program.LineTo(6, 5);
        program.LineTo(6, 6);
        program.LineTo(5, 6);
        program.LineTo(4, 6);
        program.LineTo(4, 5);
        program.LineTo(4 + dx, 4 + dy);
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 10);
        program.LineTo(0, 10);
        program.LineTo(0, 0);
        program.Mode = mode;
        return program;
    }

    private static Vector[] Ends(Program program) =>
        ExecutionMotionReader.ReadSupported(program, Vector.Zero, null).Motions.Select(m => m.End).ToArray();

    private static IEnumerable<ExecutionMotion> Cuts(Program program) =>
        ExecutionMotionReader.ReadSupported(program, Vector.Zero, null).Motions.Where(m => !m.Rapid);
}
