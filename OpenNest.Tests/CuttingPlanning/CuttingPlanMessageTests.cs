using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingPlanMessageTests
{
    [Fact]
    public void Describe_MixedBestEffortAndOpenContour_CountsOnlyBlockedPlateAndExplainsGap()
    {
        var open = new OpenNest.CNC.Program();
        open.MoveTo(0, 0);
        open.LineTo(10, 0);
        open.LineTo(10, 10);
        open.LineTo(0, 10);
        open.LineTo(0, 0.000002);
        var part = new Part(new Drawing("open perimeter", open));
        var original = OwnedProgramCopy.Copy(part.BaseDrawing.Program);
        var plates = new[]
        {
            BestEffortCuttingPlanTests.Plate(BestEffortCuttingPlanTests.TouchingContours()),
            BestEffortCuttingPlanTests.Plate(part),
        };

        var proposal = CuttingPlanBatch.Capture(plates, ExplicitContourTests.Parameters(), false).Plan();
        var text = string.Join("\n", proposal.Describe("in"));

        Assert.StartsWith("Apply is unavailable: 1 of 2 plates could not be planned.", text);
        Assert.Contains("Plate 1: best-effort, unverified.", text);
        Assert.Contains("Plate 2: blocked:", text);
        Assert.Contains("contour 1 is open: endpoint gap 2E-06 model units", text);
        Assert.Contains("closure tolerance 1E-06", text);
        Assert.Contains("Review the source contour in the drawing editor", text);
        Assert.False(proposal.CanApplyWithWarnings);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply(true).Status);
        Assert.True(ProgramContent.Equal(original, part.BaseDrawing.Program));
        Assert.All(plates.SelectMany(p => p.Parts), p => Assert.False(p.HasManualLeadIns));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Describe_NoneIsSupportedButDegenerateLeadMotionStillRefuses(bool keepOrder, bool zeroLength)
    {
        var part = new Part(new Drawing("sample", ExplicitContourTests.Square(false)), new Vector(1, 1));
        var plate = BestEffortCuttingPlanTests.Plate(part);
        var original = OwnedProgramCopy.Copy(part.Program);
        var settings = new CuttingParameters();
        if (zeroLength)
            settings.ExternalLeadIn = new LineLeadIn { Length = 0, ApproachAngle = 90 };

        var proposal = CuttingPlanBatch.Capture([plate], settings, keepOrder).Plan();

        if (zeroLength)
        {
            Assert.False(proposal.CanApply);
            Assert.Contains("Lead motion is missing, degenerate or inconsistent", string.Join("\n", proposal.Describe("in")));
            Assert.True(ProgramContent.Equal(original, part.Program));
            return;
        }

        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        var result = Assert.Single(proposal.Plates).Result;
        Assert.True(result.IndependentlyReplayed);
        Assert.DoesNotContain(result.Findings, f => f.Kind == PostVerificationKind.MissingLeadIn);
        Assert.Contains("Plate 1: ready.", string.Join("\n", proposal.Describe("in")));
        var output = Assert.Single(result.ProposedOrder).CopyProgram();
        Assert.DoesNotContain(ExecutionMotionReader.ReadSupported(output, part.Location, null).Motions,
            m => m.Layer == OpenNest.CNC.LayerType.Leadin && m.Length > 0);
        Assert.True(ProgramContent.Equal(original, part.Program));
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);
    }

    [Fact]
    public void Describe_LockedProgramWithoutLead_IsReadyAndKeepsItsProgram()
    {
        var part = new Part(new Drawing("locked sample", ExplicitContourTests.Square(false)), new Vector(1, 1))
        {
            LeadInsLocked = true,
        };
        var plate = BestEffortCuttingPlanTests.Plate(part);
        var program = part.Program;
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();

        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
        Assert.False(Assert.Single(proposal.Plates[0].Result.ProposedOrder).IsRegenerated);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply().Status);
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
