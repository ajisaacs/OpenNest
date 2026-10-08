using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class BestEffortCuttingPlanTests
{
    [Fact]
    public void TouchingContours_OfferAPreviewWithoutChangingTheDrawing()
    {
        var part = TouchingContours();
        var plate = Plate(part);
        var original = OwnedProgramCopy.Copy(part.BaseDrawing.Program);
        var strict = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: ExplicitContourTests.Parameters()));
        Assert.Equal(CuttingPlanStatus.UnsupportedGeometry, strict.Status);

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();

        Assert.NotNull(proposal.BuildPreview(0));
        Assert.False(proposal.CanApply);
        Assert.False(part.HasManualLeadIns);
        Assert.True(ProgramContent.Equal(original, part.BaseDrawing.Program));
    }

    [Fact]
    public void UnverifiedSummary_ShowsEveryWarningBeforeAcceptance()
    {
        var parts = Enumerable.Range(0, 6).Select(index =>
        {
            var part = TouchingContours();
            part.Offset(index * 12, 0);
            return part;
        }).ToArray();
        var proposal = CuttingPlanBatch.Capture([Plate(parts)], ExplicitContourTests.Parameters(), false).Plan();
        Assert.True(proposal.RequiresWarningAcceptance);
        var findings = Assert.Single(proposal.Plates).Result.Findings.ToArray();
        Assert.True(findings.Length > 5);
        Assert.Contains(findings, finding => finding.SourceOrdinal == 5);
        var summary = string.Join("\n", proposal.Describe("in"));
        Assert.Contains("Part 6", summary);
        Assert.DoesNotContain("... and", summary);
    }

    [Fact]
    public void AcceptWarnings_InstallsOwnedProgramsAndLeavesStrictApplyClosed()
    {
        var part = TouchingContours();
        var plate = Plate(part);
        var source = OwnedProgramCopy.Copy(part.BaseDrawing.Program);
        var pose = (part.Location, part.Rotation);
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();
        var result = Assert.Single(proposal.Plates).Result;
        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        Assert.False(result.IndependentlyReplayed);
        Assert.True(proposal.RequiresWarningAcceptance);
        Assert.Contains(result.Findings, f => f.Message.Contains("Material boundaries"));
        Assert.Contains("unverified", string.Join("\n", proposal.Describe("in")));
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
        Assert.Equal(CuttingCommitStatus.InvalidInput, CuttingPlanService.Apply([result]).Status);
        var generated = Assert.Single(result.ProposedOrder);
        Assert.True(generated.IsRegenerated);
        Assert.Equal(3, generated.ContourChoices.Count);
        Assert.Equal(2, generated.ContourChoices[^1].ContourOrdinal);
        var originalCuts = ExecutionMotionReader.ReadSupported(source, part.Location, null).Motions
            .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display).Sum(m => m.Length);
        var emittedCuts = ExecutionMotionReader.ReadSupported(generated.CopyProgram(), part.Location, null).Motions
            .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display).Sum(m => m.Length);
        Assert.Equal(originalCuts, emittedCuts, 8);
        var copy = generated.CopyProgram();
        copy.Codes.Clear();

        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: true).Status);

        Assert.True(part.HasManualLeadIns);
        Assert.True(ProgramContent.Equal(generated.CopyProgram(), part.Program));
        Assert.True(ProgramContent.Equal(source, part.BaseDrawing.Program));
        Assert.Equal(pose, (part.Location, part.Rotation));
        Assert.Equal(CuttingCommitStatus.Stale, proposal.Apply(acceptWarnings: true).Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("unknown")]
    [InlineData("cycle")]
    [InlineData("nonfinite")]
    [InlineData("open")]
    [InlineData("suppressed")]
    public void UnreadableOrUnrepresentableInput_IsNeverWaived(string failure)
    {
        var part = TouchingContours();
        var plate = Plate(part);
        var program = part.BaseDrawing.Program;
        switch (failure)
        {
            case "null": program.Codes = null; break;
            case "unknown": program.Codes.Add(new CustomMove()); break;
            case "cycle": program.Codes.Add(new SubProgramCall { Program = program }); break;
            case "nonfinite": part.Location = new Vector(double.NaN, 1); break;
            case "open": program.Codes.RemoveAt(program.Codes.Count - 1); break;
            case "suppressed": program.Codes.OfType<Motion>().First().Suppressed = true; break;
        }
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();
        Assert.False(proposal.CanApplyWithWarnings);
        Assert.Null(proposal.BuildPreview(0));
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply(acceptWarnings: true).Status);
        Assert.False(part.HasManualLeadIns);
    }

    [Fact]
    public void MixedBatch_StaleOrCancelledChangesNothing()
    {
        var imperfect = TouchingContours();
        var clean = new Part(new Drawing("clean", ExplicitContourTests.Square(false)), new Vector(1, 1));
        var plates = new[] { Plate(clean), Plate(imperfect) };
        var originals = new[] { clean.Program, imperfect.Program };
        var batch = CuttingPlanBatch.Capture(plates, ExplicitContourTests.Parameters(), false);
        var proposal = batch.Plan();
        Assert.True(proposal.RequiresWarningAcceptance);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(CuttingCommitStatus.Cancelled, proposal.Apply(true, cancelled.Token).Status);
        Assert.False(batch.Plan(token: cancelled.Token).CanApplyWithWarnings);
        imperfect.Offset(1, 0);
        Assert.Null(proposal.BuildPreview(1));
        Assert.Equal(CuttingCommitStatus.Stale, proposal.Apply(acceptWarnings: true).Status);
        Assert.Equal(originals, plates.SelectMany(p => p.Parts).Select(p => p.Program));
        Assert.All(plates, p => Assert.Null(p.CuttingParameters));
    }

    [Fact]
    public void LockedPart_KeepsExactProgramWhileOtherPartsRegenerate()
    {
        var part = TouchingContours();
        part.LeadInsLocked = true;
        var program = part.Program;
        var other = new Part(new Drawing("clean", ExplicitContourTests.Square(false)), new Vector(20, 1));
        var proposal = CuttingPlanBatch.Capture([Plate(part, other)], ExplicitContourTests.Parameters(), false).Plan();
        Assert.True(proposal.RequiresWarningAcceptance);
        Assert.False(proposal.Plates[0].Result.ProposedOrder[0].IsRegenerated);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(true).Status);
        Assert.Same(program, part.Program);
        Assert.True(part.LeadInsLocked);
        Assert.True(other.HasManualLeadIns);
    }

    [Fact]
    public void KnownPartOverlap_StillBlocksTheWholeBatch()
    {
        var first = new Part(new Drawing("first", ExplicitContourTests.Square(false)), new Vector(1, 1));
        var second = new Part(new Drawing("second", ExplicitContourTests.Square(false)), new Vector(6, 6));
        var proposal = CuttingPlanBatch.Capture([Plate(TouchingContours()), Plate(first, second)],
            ExplicitContourTests.Parameters(), false).Plan();
        Assert.False(proposal.CanApplyWithWarnings);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply(true).Status);
        Assert.False(first.HasManualLeadIns);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvenCutoffDependencies_AreNotWaived(bool keepOrder)
    {
        var part = TouchingContours();
        var cutProgram = new Program();
        cutProgram.MoveTo(20, 0); cutProgram.LineTo(20, 20);
        var cutoff = new Part(new Drawing("cutoff", cutProgram) { IsCutOff = true });
        var plate = Plate(part, cutoff); // An orphaned cutoff must precede all ordinary parts.
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), keepOrder).Plan();
        if (keepOrder)
        {
            Assert.False(proposal.CanApplyWithWarnings);
            Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply(true).Status);
        }
        else
        {
            Assert.True(proposal.CanApplyWithWarnings);
            Assert.Same(cutoff, proposal.Plates[0].Result.ProposedOrder[0].SourcePart);
            Assert.False(proposal.Plates[0].Result.ProposedOrder[0].IsRegenerated);
        }
    }

    [Fact]
    public void SelfIntersectingClosedContour_StillGetsEveryCutAndALead()
    {
        var program = new Program();
        program.MoveTo(0, 0); program.LineTo(8, 8); program.LineTo(0, 8);
        program.LineTo(8, 0); program.LineTo(0, 0);
        var part = new Part(new Drawing("crossed contour", program), new Vector(1, 1));
        var proposal = CuttingPlanBatch.Capture([Plate(part)], ExplicitContourTests.Parameters(), false).Plan();
        Assert.True(proposal.RequiresWarningAcceptance);
        var generated = Assert.Single(proposal.Plates[0].Result.ProposedOrder);
        Assert.True(generated.IsRegenerated);
        Assert.Single(generated.ContourChoices);
        var moves = ExecutionMotionReader.ReadSupported(generated.CopyProgram(), part.Location, null).Motions;
        Assert.Contains(moves, m => m.Layer == LayerType.Leadin);
        var original = ExecutionMotionReader.ReadSupported(program, part.Location, null).Motions;
        Assert.Equal(original.Where(m => !m.Rapid).Sum(m => m.Length),
            moves.Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display).Sum(m => m.Length), 8);
    }

    [Fact]
    public void ProvenNestedPart_PrecedesItsHostEvenWithAnImperfectNeighbour()
    {
        var hostProgram = ExplicitContourTests.Square(false);
        hostProgram.MoveTo(2, 2); hostProgram.LineTo(8, 2); hostProgram.LineTo(8, 8);
        hostProgram.LineTo(2, 8); hostProgram.LineTo(2, 2);
        var host = new Part(new Drawing("host", hostProgram));
        var innerProgram = new Program();
        innerProgram.MoveTo(0, 0); innerProgram.LineTo(0, 1); innerProgram.LineTo(1, 1);
        innerProgram.LineTo(1, 0); innerProgram.LineTo(0, 0);
        var inner = new Part(new Drawing("insert", innerProgram), new Vector(4, 4));
        var imperfect = TouchingContours();
        imperfect.Offset(20, 0);
        var proposal = CuttingPlanBatch.Capture([Plate(host, inner, imperfect)],
            ExplicitContourTests.Parameters(), false).Plan();
        Assert.True(proposal.RequiresWarningAcceptance, string.Join("\n", proposal.Describe("in")));
        var order = proposal.Plates[0].Result.ProposedOrder.Select(p => p.SourcePart).ToList();
        Assert.True(order.IndexOf(inner) < order.IndexOf(host));
    }

    [Fact]
    public void BestEffortInstallFailure_RollsBackEveryPlate()
    {
        var parts = new[] { TouchingContours(), TouchingContours() };
        var plates = parts.Select(p => Plate(p)).ToArray();
        var originals = parts.Select(p => p.Program).ToArray();
        var proposal = CuttingPlanBatch.Capture(plates, ExplicitContourTests.Parameters(), false).Plan();
        Assert.True(proposal.RequiresWarningAcceptance);
        var commit = CuttingPlanService.Apply(proposal.Plates.Select(p => p.Result), default,
            (plate, _) => { if (ReferenceEquals(plate, plates[1])) throw new InvalidOperationException("test install fault"); },
            acceptUnverified: true);
        Assert.Equal(CuttingCommitStatus.Failed, commit.Status);
        Assert.Equal(originals, parts.Select(p => p.Program));
        Assert.All(parts, p => Assert.False(p.HasManualLeadIns));
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(45)]
    public void RealDxf_PlansAndAppliesWithoutChangingTheDrawing(double degrees)
    {
        var path = TestConfig.GetExistingPath("BestEffortDxfPath");
        Skip.If(path == null, "BestEffortDxfPath not configured in test-config.json or file not found");
        var drawing = OpenNest.IO.CadImporter.ImportDrawing(path);
        var part = new Part(drawing, new Vector(1, 1));
        part.Rotate(degrees * System.Math.PI / 180);
        var plate = Plate(part);
        var source = OwnedProgramCopy.Copy(drawing.Program);
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();
        Assert.True(proposal.CanApplyWithWarnings, string.Join("\n", proposal.Describe("in")));
        Assert.NotNull(proposal.BuildPreview(0));
        Assert.True(Assert.Single(proposal.Plates[0].Result.ProposedOrder).IsRegenerated);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(true).Status);
        Assert.True(ProgramContent.Equal(source, drawing.Program));
        Assert.True(part.HasManualLeadIns);
    }

    private sealed class CustomMove : LinearMove { }

    internal static Part TouchingContours()
    {
        var program = ExplicitContourTests.Square(false);
        // Two ordinary holes share an edge: closed executable contours, ambiguous material.
        program.MoveTo(2, 2); program.LineTo(4, 2); program.LineTo(4, 4);
        program.LineTo(2, 4); program.LineTo(2, 2);
        program.MoveTo(4, 2); program.LineTo(6, 2); program.LineTo(6, 4);
        program.LineTo(4, 4); program.LineTo(4, 2);
        return new Part(new Drawing("touching contours", program), new Vector(1, 1));
    }

    internal static Plate Plate(params Part[] parts)
    {
        var plate = new Plate(new Size(100, 100));
        foreach (var part in parts)
            plate.Parts.Add(part);
        return plate;
    }
}
