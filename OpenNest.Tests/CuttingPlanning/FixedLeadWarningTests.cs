using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// D1a: a violation inside a fixed emission (a locked program) is a named warning on an
/// UNVERIFIED proposal. Every planner-chosen motion keeps its strict checks, and hard blocks
/// (overlap, malformed programs) still refuse.
/// </summary>
public class FixedLeadWarningTests
{
    [Fact]
    public void LockedHoleLeadIntoNestedPart_IsAnUnverifiedProposalWithEveryOtherPartPlanned()
    {
        var (plate, host, nested, nest) = PlanningCharacterizationTests.LockedHoleLeadFixture();
        var hostProgram = host.Program;

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();

        var planned = Assert.Single(proposal.Plates);
        var result = planned.Result;
        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        Assert.True(result.IndependentlyReplayed);
        Assert.False(planned.IsReady);
        Assert.True(planned.CanApplyWithWarnings);
        Assert.True(planned.HasOnlyFixedLeadWarnings);
        Assert.False(proposal.CanApply);
        Assert.True(proposal.RequiresWarningAcceptance);
        // The nested part is regenerated and strictly planned; only the locked host warns.
        Assert.Equal([nested, host], result.ProposedOrder.Select(p => p.SourcePart));
        Assert.True(result.ProposedOrder[0].IsRegenerated);
        Assert.False(result.ProposedOrder[1].IsRegenerated);
        var warning = Assert.Single(result.Findings);
        Assert.Equal(PostVerificationKind.FixedLead, warning.Kind);
        Assert.Same(host, warning.SourcePart);
        Assert.Same(nested, warning.OtherSourcePart);
        Assert.Equal(new FixedLeadWarning(1, null, 0), warning.FixedLead);
        Assert.Contains("Locked lead on cutting contour 1 contacts or enters another part's material", warning.Message);
        Assert.NotNull(warning.Witness?.Location);
        var row = Assert.Single(proposal.DiagnosticRows());
        Assert.Equal((2, 1), (row.PartNumber, row.OtherPartNumber));

        // CuttingPlanService.Apply keeps refusing unverified results; only acceptance applies.
        Assert.Equal(CuttingCommitStatus.InvalidInput, CuttingPlanService.Apply([result]).Status);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
        Assert.Same(hostProgram, host.Program);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: true).Status);
        Assert.Same(hostProgram, host.Program); // The locked program is never replaced.
        Assert.True(host.LeadInsLocked);
        Assert.True(nested.HasManualLeadIns);

        // The same violation reappears in the separate pre-post review.
        var review = PostVerificationAnalyzer.Analyze(nest);
        var posted = Assert.Single(review.Findings, f => f.Kind == PostVerificationKind.FixedLead);
        Assert.Equal((2, 1), (posted.PartNumber, posted.OtherPartNumber));
        Assert.Contains("Locked lead on cutting contour 1 contacts or enters the material of part 1", posted.Message);
        Assert.True(review.HasWarnings);
        Assert.Contains("Fixed leads: 1 warning(s)", review.ToDisplayText());
    }

    [Fact]
    public void LockedTabbedPerimeter_IsAWarningNotARefusal()
    {
        var (plate, part) = PlanningCharacterizationTests.LockedTabbedFixture();
        var program = part.Program;

        var planned = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan().Plates[0];

        Assert.True(planned.HasOnlyFixedLeadWarnings);
        var warning = Assert.Single(planned.Result.Findings);
        Assert.Equal(PostVerificationKind.FixedLead, warning.Kind);
        Assert.Contains("retention cannot be certified", warning.Message);
        Assert.Same(part, Assert.Single(planned.Result.ProposedOrder).SourcePart);
        Assert.Same(program, part.Program);
    }

    [Fact]
    public void RegeneratedHoleLead_AvoidsTheNestedPartAsBefore()
    {
        var (plate, host, nested, nest) = PlanningCharacterizationTests.LockedHoleLeadFixture();
        host.RemoveLeadIns();
        Assert.False(host.LeadInsLocked);

        var planned = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan().Plates[0];

        Assert.True(planned.IsReady, string.Join("\n", planned.Result.Findings.Select(f => f.Message)));
        Assert.Empty(planned.Result.Findings);
    }

    [Fact]
    public void RegeneratedLeadThatCannotFit_IsStillRefused()
    {
        var plate = new Nest().CreatePlate();
        plate.Parts.Add(new Part(new Drawing("tight", TightHole()), new Vector(1, 1)));

        var planned = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan().Plates[0];

        Assert.False(planned.CanApplyWithWarnings);
        Assert.Empty(planned.Result.ProposedOrder);
        Assert.DoesNotContain(planned.Result.Findings, f => f.Kind == PostVerificationKind.FixedLead);
        Assert.Contains(planned.Result.Findings, f => f.Message.Contains("No tested lead-in fits"));
    }

    [Fact]
    public void KnownPartOverlap_StillBlocksAPlateWithLockedWarnings()
    {
        var (plate, _, _, _) = PlanningCharacterizationTests.LockedHoleLeadFixture();
        plate.Parts.Add(new Part(new Drawing("overlapping", LeadPathValidationTests.Rectangle(0, 0, 2, 2)),
            new Vector(19, 19)));

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();

        var planned = Assert.Single(proposal.Plates);
        Assert.NotEmpty(planned.Overlap.Pairs);
        Assert.False(planned.CanApplyWithWarnings);
        Assert.False(proposal.CanApplyWithWarnings);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply(acceptWarnings: true).Status);
    }

    [Fact]
    public void MalformedLockedProgram_IsStillRefused()
    {
        var part = new Part(new Drawing("square", ExplicitContourTests.Square(false)), new Vector(1, 1));
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10); program.LineTo(10, 10); program.LineTo(10, 0); program.LineTo(0, 0);
        program.Codes.Add(new LinearMove(double.PositiveInfinity, 0));
        Assert.True(part.RestoreLeadInProgram(program, true));
        var plate = new Nest().CreatePlate();
        plate.Parts.Add(part);

        var planned = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan().Plates[0];

        Assert.False(planned.CanApplyWithWarnings);
        Assert.Empty(planned.Result.ProposedOrder);
        Assert.DoesNotContain(planned.Result.Findings, f => f.Kind == PostVerificationKind.FixedLead);
    }

    [Fact]
    public void MixedBatch_OneWarnedPlateAndOneReadyPlate_AppliesOnlyWithAcceptance()
    {
        var (warned, host, _, _) = PlanningCharacterizationTests.LockedHoleLeadFixture();
        var ready = new Nest().CreatePlate();
        ready.Parts.Add(new Part(new Drawing("clean", ExplicitContourTests.Square(false)), new Vector(1, 1)));

        var proposal = CuttingPlanBatch.Capture([warned, ready], ExplicitContourTests.Parameters(), false).Plan();

        Assert.True(proposal.Plates[0].HasOnlyFixedLeadWarnings);
        Assert.True(proposal.Plates[1].IsReady);
        Assert.False(proposal.CanApply);
        Assert.True(proposal.CanApplyWithWarnings);
        Assert.StartsWith("Unverified plan available", proposal.Describe("in")[0]);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
        Assert.False(ready.Parts[0].HasManualLeadIns);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: true).Status);
        Assert.True(ready.Parts[0].HasManualLeadIns);
        Assert.True(host.LeadInsLocked);
    }

    [Fact]
    public void PlannerRapidIntoALockedPart_StaysStrict()
    {
        // The locked square's own program is clean; only the planner's rapid into it could
        // cross the regenerated square cut first. A preserved order forces the search to pick
        // a departure for the first square that does not cross it, so the plate stays Ready.
        var parameters = ExplicitContourTests.Parameters();
        var first = new Part(new Drawing("first", ExplicitContourTests.Square(false)), new Vector(0, 0));
        var locked = new Part(new Drawing("locked", ExplicitContourTests.Square(false)), new Vector(20, 0));
        var prepared = PreparedContours.Capture(locked.BaseDrawing.Program, parameters);
        Assert.True(locked.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, new Vector(-1, 5))]), true));

        var result = CuttingPlanService.Plan(new CuttingPlanRequest([first, locked], new Vector(30, 5),
            confirmedParameters: parameters, preservePartOrder: true));

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void PrePost_LockedPartWithoutViolations_AddsNoFixedLeadFinding()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        var part = new Part(new Drawing("square", ExplicitContourTests.Square(false)), new Vector(1, 1));
        var prepared = PreparedContours.Capture(part.BaseDrawing.Program, ExplicitContourTests.Parameters());
        Assert.True(part.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, new Vector(-1, 5))]), true));
        plate.Parts.Add(part);

        var review = PostVerificationAnalyzer.Analyze(nest);

        Assert.DoesNotContain(review.Findings, f => f.Kind is PostVerificationKind.FixedLead or PostVerificationKind.Incomplete);
        Assert.DoesNotContain("Fixed leads", review.ToDisplayText());
    }

    // A 10 x 10 square with a 0.1 x 0.1 square hole: no 0.3 line lead fits inside it.
    private static Program TightHole()
    {
        var program = new Program();
        program.MoveTo(5, 5); program.LineTo(5.1, 5); program.LineTo(5.1, 5.1); program.LineTo(5, 5.1); program.LineTo(5, 5);
        program.MoveTo(0, 0); program.LineTo(0, 10); program.LineTo(10, 10); program.LineTo(10, 0); program.LineTo(0, 0);
        return program;
    }
}
