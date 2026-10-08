using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingPlanMessageTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Describe_MissingOrZeroLengthLead_ExplainsSettingsWithoutBlamingSearch(bool keepOrder, bool zeroLength)
    {
        var part = new Part(new Drawing("sample", ExplicitContourTests.Square(false)), new Vector(1, 1));
        var plate = new Nest().CreatePlate();
        plate.Size = new Size(100, 100);
        plate.Parts.Add(part);
        var program = part.Program;
        var original = OwnedProgramCopy.Copy(program);
        var settings = new CuttingParameters();
        if (zeroLength)
            settings.ExternalLeadIn = new LineLeadIn { Length = 0, ApproachAngle = 90 };

        var proposal = CuttingPlanBatch.Capture([plate], settings, keepOrder).Plan();

        Assert.False(proposal.CanApply);
        var result = Assert.Single(proposal.Plates).Result;
        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, result.Status);
        Assert.Contains(result.Findings, f => f.Kind == PostVerificationKind.MissingLeadIn);
        var text = string.Join("\n", proposal.Describe("in"));
        Assert.Contains("Plate 1: blocked: missing or zero-length lead-in.", text);
        Assert.Contains("Open Cutting Settings...", text);
        Assert.Contains("other than None", text);
        Assert.Contains("nonzero length", text);
        Assert.Contains("Part 1 (sample):", text);
        Assert.Contains("Cutting contour 1", text);
        Assert.DoesNotContain("search limit", text);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
        Assert.Same(program, part.Program);
        Assert.True(ProgramContent.Equal(original, part.Program));
        Assert.Null(plate.CuttingParameters);
        Assert.False(part.HasManualLeadIns);

        // Choosing valid settings fixes the refusal; reporting never changes settings or bypasses checks.
        var ready = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), keepOrder).Plan();
        Assert.True(ready.CanApply, string.Join("\n", ready.Describe("in")));
        Assert.True(Assert.Single(ready.Plates).Result.IndependentlyReplayed);
        Assert.DoesNotContain("missing or zero-length", string.Join("\n", ready.Describe("in")));
        Assert.Same(program, part.Program);
        Assert.True(ProgramContent.Equal(original, part.Program));
    }

    [Fact]
    public void Describe_LockedProgramWithoutLead_ExplainsThatSettingsCannotRegenerateIt()
    {
        var part = new Part(new Drawing("locked sample", ExplicitContourTests.Square(false)), new Vector(1, 1))
        {
            LeadInsLocked = true,
        };
        var plate = new Nest().CreatePlate();
        plate.Size = new Size(100, 100);
        plate.Parts.Add(part);
        var program = part.Program;

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();

        Assert.Equal(CuttingPlanStatus.ConstraintConflict, Assert.Single(proposal.Plates).Result.Status);
        Assert.False(proposal.CanApply);
        var text = string.Join("\n", proposal.Describe("in"));
        Assert.Contains("missing or zero-length lead-in", text);
        Assert.Contains("If the affected part is locked, edit its lead-ins or unlock it before replanning.", text);
        Assert.Contains("Part 1 (locked sample):", text);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
        Assert.True(part.LeadInsLocked);
        Assert.Same(program, part.Program);
    }

    [Fact]
    public void Describe_LeadHitsNeighbour_SuggestsSpacingOrShorterLeadWithoutAllowingApply()
    {
        var settings = ExplicitContourTests.Parameters();
        var clean = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        var prepared = PreparedContours.Capture(clean, settings);
        var part = new Part(new Drawing("lead blocked", clean));
        Assert.True(part.RestoreLeadInProgram(prepared.Emit([prepared.ClosestEntry(0, new Vector(-1, 5))]), true));
        var obstacle = new Part(new Drawing("neighbour", LeadPathValidationTests.Rectangle(-0.3, 4.5, -0.1, 5.5)));
        var plate = new Nest().CreatePlate();
        plate.Parts.Add(part);
        plate.Parts.Add(obstacle);
        var original = OwnedProgramCopy.Copy(part.Program);

        var proposal = CuttingPlanBatch.Capture([plate], settings, true).Plan();
        var text = string.Join("\n", proposal.Describe("in"));

        Assert.False(proposal.CanApply);
        Assert.Contains("another placed material", text);
        Assert.Contains("spacing the parts farther apart", text);
        Assert.Contains("reducing the lead-in", text);
        Assert.Contains("then replan", text);
        Assert.DoesNotContain("other than None", text);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
        Assert.True(ProgramContent.Equal(original, part.Program));
        Assert.True(part.LeadInsLocked);
    }

    [Fact]
    public void Describe_NoTestedLeadFits_SuggestsChangesButDoesNotClaimTheyWillWork()
    {
        var settings = ExplicitContourTests.Parameters();
        var part = new Part(new Drawing("inside", LeadPathValidationTests.Rectangle(0, 0, 10, 10)));
        var wrap = new Part(new Drawing("wrap", LeadPathValidationTests.Rectangle(-0.05, -0.05, 10.05, 10.05)));
        var plate = new Nest().CreatePlate();
        plate.Parts.Add(part);
        plate.Parts.Add(wrap);

        var proposal = CuttingPlanBatch.Capture([plate], settings, false).Plan();
        var text = string.Join("\n", proposal.Describe("in"));

        Assert.Contains("No tested lead-in fits", text);
        Assert.Contains("Try reducing the lead-in length", text);
        Assert.Contains("if nearby parts obstruct the lead-in, space the parts farther apart", text);
        Assert.Contains("Replan to check the changes.", text);
        Assert.False(proposal.CanApply);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PostVerificationKind.RapidCrossing)]
    public void Describe_OtherRefusals_KeepTheirStatusAndFindings(PostVerificationKind? kind)
    {
        // The presentation uses the typed finding, not matching words in a diagnostic.
        var finding = new CuttingPlanFinding(null, null, null, null, kind, "MissingLeadIn lookalike text");
        var result = new CuttingPlanResult(CuttingPlanStatus.NoSolutionWithinBudget, findings: [finding]);
        var proposal = new CuttingPlanProposal([new(new Nest().CreatePlate(), 7, result, result, null)],
            ExplicitContourTests.Parameters());

        var text = string.Join("\n", proposal.Describe("in"));

        Assert.Contains("Plate 7: no complete plan was found within the search limit.", text);
        Assert.Contains("planning with the current order was refused:", text);
        Assert.Contains(finding.Message, text);
        Assert.DoesNotContain("Open Cutting Settings...", text);
        Assert.DoesNotContain("missing or zero-length lead-in", text);
        Assert.False(proposal.CanApply);
    }
}
