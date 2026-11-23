using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Plan Cutting keeps every lead it chooses on the sheet, as the pre-post plate edge check
/// requires; a locked lead off the sheet is a fixed-lead warning, never a silent Ready plan.
/// </summary>
public class LeadOffSheetTests
{
    [Fact]
    public void Validator_RefusesALeadPastItsMaterialsSheet()
    {
        var target = Snapshot(LeadPathValidationTests.Rectangle(0, 0, 10, 10));
        var lead = Read(ExternalLead()); // Pierces at (-3, 5).

        AssertClear(LeadPathValidator.Check(lead, target, [target]));
        target.Sheet = new Box(-5, -5, 30, 30);
        AssertClear(LeadPathValidator.Check(lead, target, [target]));
        target.Sheet = new Box(-1, -1, 30, 30);

        var off = LeadPathValidator.Check(lead, target, [target]);

        Assert.True(off.IsComplete, off.Reason);
        Assert.False(off.IsClear);
        Assert.Contains("Lead-in runs 2.000 past the plate's left edge", off.Reason);
    }

    [Fact]
    public void Validator_CollectsAFixedLeadOffTheSheet()
    {
        var target = Snapshot(LeadPathValidationTests.Rectangle(0, 0, 10, 10));
        target.Sheet = new Box(-1, -1, 30, 30);
        var failures = new List<FixedLeadFailure>();

        AssertClear(LeadPathValidator.Check(Read(ExternalLead()), target, [target], _ => true, failures, default));

        var failure = Assert.Single(failures);
        Assert.True(failure.OffSheet);
        Assert.Null(failure.Obstacle);
    }

    [Fact]
    public void Plan_EntersFromASideWithRoomForTheLead()
    {
        // 0.2 from the left and bottom edges: a 1-long lead fits only from the top or right.
        var part = new Part(new Drawing("near", LeadPathValidationTests.Rectangle(0, 0, 10, 10)), new Vector(0.2, 0.2));
        var plate = new Plate(20, 20);
        plate.Parts.Add(part);

        var proposal = CuttingPlanBatch.Capture([plate], Parameters(), false).Plan();

        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);
        var report = PostVerificationAnalyzer.Analyze(new Nest { Plates = { plate } });
        Assert.DoesNotContain(report.Findings, f => f.Kind == PostVerificationKind.OffPlate);
    }

    [Fact]
    public void Plan_NoSideHasRoom_RefusesNamingTheEdge()
    {
        var part = new Part(new Drawing("tight", LeadPathValidationTests.Rectangle(0, 0, 10, 10)), new Vector(0.2, 0.2));
        var plate = new Plate(10.4, 10.4);
        plate.Parts.Add(part);

        var planned = CuttingPlanBatch.Capture([plate], Parameters(), false).Plan();

        Assert.False(planned.CanApply);
        Assert.Contains(planned.Plates[0].Result.Findings, f => f.Message.Contains("past the plate's"));
    }

    [Fact]
    public void Plan_LockedLeadOffTheSheet_IsAFixedLeadWarning()
    {
        var part = new Part(new Drawing("locked", LeadPathValidationTests.Rectangle(0, 0, 10, 10)), new Vector(0.2, 0.2));
        var placed = new Program();
        placed.MoveTo(-3, 5);
        placed.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        placed.LineTo(0, 10); placed.LineTo(10, 10); placed.LineTo(10, 0); placed.LineTo(0, 0); placed.LineTo(0, 5);
        Assert.True(part.RestoreLeadInProgram(placed, false));
        part.LeadInsLocked = true;
        var plate = new Plate(20, 20);
        plate.Parts.Add(part);

        var planned = CuttingPlanBatch.Capture([plate], Parameters(), false).Plan().Plates[0];

        Assert.False(planned.IsReady);
        Assert.True(planned.HasOnlyFixedLeadWarnings);
        var warning = Assert.Single(planned.Result.Findings);
        Assert.Equal(PostVerificationKind.FixedLead, warning.Kind);
        Assert.Contains("Locked lead on cutting contour 1 runs off the plate", warning.Message);
        Assert.Contains("past the plate's left edge", warning.Message);
    }

    private static CuttingParameters Parameters() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 1, ApproachAngle = 90 },
        InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 },
        ExternalLeadOut = new NoLeadOut(),
        InternalLeadOut = new NoLeadOut(),
        PierceClearance = 0,
    };

    private static Program ExternalLead()
    {
        var p = new Program();
        p.MoveTo(-3, 5);
        p.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        p.LineTo(0, 10);
        return p;
    }

    private static LeadMaterialSnapshot Snapshot(Program clean)
    {
        var snapshot = LeadMaterialSnapshot.Capture(clean, Vector.Zero);
        Assert.True(snapshot.IsComplete, snapshot.Reason);
        return snapshot;
    }

    private static OwnedExecution Read(Program p) => ExecutionMotionReader.Read(p, Vector.Zero, null, default);

    private static void AssertClear(LeadPathValidationResult result)
    {
        Assert.True(result.IsComplete, result.Reason);
        Assert.True(result.IsClear, result.Reason);
    }
}
