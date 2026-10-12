using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingProposalInspectionTests
{
    [Fact]
    public async Task ReadyProposal_UsesDetachedExactProgramsAndMappingWithoutLiveEvents()
    {
        var plate = Plate();
        plate.Quantity = 3;
        var parts = plate.Parts.ToArray();
        var programs = parts.Select(p => p.Program).ToArray();
        var nested = parts.Select(p => p.BaseDrawing.Quantity.Nested).ToArray();
        var events = 0;
        plate.PartAdded += (_, _) => events++;
        plate.PartRemoved += (_, _) => events++;
        plate.PartsReordered += (_, _) => events++;
        var inspection = new CuttingProposalInspection();
        var work = inspection.BeginPlanning(Capture(plate));
        await work;
        Assert.True(inspection.IsRunning); // Owner callback, not task completion, releases ownership.
        inspection.CompletePlanning(work);
        var proposal = inspection.Proposal;
        Assert.True(proposal.CanApply);
        var preview = Assert.IsType<Plate>(inspection.Preview);
        Assert.Same(preview, inspection.RefreshPreview());
        Assert.Equal(0, preview.Quantity);
        Assert.Equal(new[] { 1, 0 }, proposal.Plates[0].Result.ProposedOrder.Select(p => p.SourceOrdinal));
        Assert.Equal(1, CuttingProposalInspection.ProposedPosition(proposal, 0, 1));
        Assert.Equal(0, CuttingProposalInspection.ProposedPosition(proposal, 0, 2));
        Assert.Null(CuttingProposalInspection.ProposedPosition(proposal, 0, 3));
        Assert.Null(CuttingProposalInspection.ProposedPosition(proposal, -1, 1));
        for (var i = 0; i < preview.Parts.Count; i++)
        {
            Assert.DoesNotContain(preview.Parts[i], parts);
            Assert.True(ProgramContent.Equal(proposal.Plates[0].Result.ProposedOrder[i].CopyProgram(), preview.Parts[i].Program));
        }
        Assert.True(inspection.RequestClose());
        Assert.Null(inspection.Preview);
        Assert.Null(inspection.Proposal);
        Assert.Equal(parts, plate.Parts);
        Assert.Equal(programs, plate.Parts.Select(p => p.Program));
        Assert.Equal(nested, parts.Select(p => p.BaseDrawing.Quantity.Nested));
        Assert.Equal(3, plate.Quantity);
        Assert.Equal(0, events);
        Assert.Null(plate.CuttingParameters);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleBeforeCompletionOrAfterDisplay_RefusesPreview(bool beforeCompletion)
    {
        var plate = Plate();
        var inspection = new CuttingProposalInspection();
        var work = inspection.BeginPlanning(Capture(plate));
        await work;
        if (beforeCompletion)
            plate.Quantity++;
        inspection.CompletePlanning(work);
        if (!beforeCompletion)
        {
            Assert.NotNull(inspection.Preview);
            plate.Parts[0].Offset(0, 1);
        }
        Assert.Null(inspection.RefreshPreview());
        Assert.Null(inspection.Preview);
        Assert.NotEmpty(inspection.Proposal.Describe("in"));
        Assert.All(plate.Parts, p => Assert.False(p.HasManualLeadIns));
        Assert.True(inspection.RequestClose());
    }

    [Fact]
    public async Task UnsupportedGraph_RefusesPreviewAndNeverInvokesForeignClone()
    {
        var plate = Plate();
        var foreign = new ForeignProgram();
        plate.Parts[0].Program.Codes.Add(foreign);
        var inspection = new CuttingProposalInspection();
        var work = inspection.BeginPlanning(Capture(plate));
        await work;
        inspection.CompletePlanning(work);
        Assert.False(inspection.Proposal.CanApply);
        Assert.Null(inspection.RefreshPreview());
        Assert.Equal(0, foreign.Clones);
        Assert.NotEmpty(inspection.Proposal.DiagnosticRows());
        Assert.True(inspection.RequestClose());
    }

    [Fact]
    public async Task CloseDuringWorker_RetainsOwnershipUntilOwnerCompletionAndRejectsLateResult()
    {
        var plate = Plate();
        var programs = plate.Parts.Select(p => p.Program).ToArray();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var inspection = new CuttingProposalInspection();
        var work = inspection.BeginPlanning(Capture(plate), token =>
        {
            entered.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
            Assert.True(token.IsCancellationRequested);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(inspection.RequestClose());
            Assert.True(inspection.IsRunning);
            Assert.Throws<InvalidOperationException>(() => inspection.CompletePlanning(work));
            Assert.True(inspection.IsRunning);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await inspection.BeginPlanning(Capture(plate)));
        }
        finally
        {
            release.Set();
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await work);
        Assert.True(inspection.IsRunning);
        inspection.CompletePlanning(work);
        Assert.False(inspection.IsRunning);
        Assert.True(inspection.RequestClose());
        Assert.Null(inspection.Proposal);
        Assert.Null(inspection.Preview);
        Assert.Equal(programs, plate.Parts.Select(p => p.Program));
    }

    [Fact]
    public async Task FaultedWorker_OffersNoPreviewButCanBeClosed()
    {
        var inspection = new CuttingProposalInspection();
        var work = inspection.BeginPlanning(Capture(Plate()), _ => throw new InvalidOperationException("synthetic"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await work);
        inspection.CompletePlanning(work);
        Assert.False(inspection.IsRunning);
        Assert.Null(inspection.Proposal);
        Assert.True(inspection.RequestClose());
    }

    private static CuttingPlanBatch Capture(Plate plate) =>
        CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false);

    private static Plate Plate()
    {
        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(new Drawing("far", ExplicitContourTests.Square(false)), new Vector(12, 1)));
        plate.Parts.Add(new Part(new Drawing("near", ExplicitContourTests.Square(false)), new Vector(1, 1)));
        return plate;
    }

    private sealed class ForeignProgram : OpenNest.CNC.LinearMove
    {
        public int Clones { get; private set; }
        public override OpenNest.CNC.ICode Clone()
        {
            Clones++;
            throw new InvalidOperationException("Unsupported code must never be cloned.");
        }
    }
}
