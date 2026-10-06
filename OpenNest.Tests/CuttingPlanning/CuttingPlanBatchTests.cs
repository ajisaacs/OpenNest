using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingPlanBatchTests
{
    [Fact]
    public void Apply_EveryPlateReady_InstallsAllPlatesAndRecordsOwnedSettingsOnEach()
    {
        var nest = new Nest();
        var first = Plate(nest, Clean("a", 1, 1), Clean("b", 12, 1));
        var second = Plate(nest, Clean("c", 1, 1));
        var parameters = ExplicitContourTests.Parameters();
        var length = ((LineLeadIn)parameters.ExternalLeadIn).Length;

        var proposal = CuttingPlanBatch.Capture([first, second], parameters, preservePartOrder: false).Plan();

        Assert.True(proposal.CanApply);
        Assert.All(proposal.Plates, p => Assert.True(p.IsReady));
        Assert.Equal(3, proposal.Plates.Sum(p => p.RegeneratedCount));
        Assert.All(first.Parts.Concat(second.Parts), part => Assert.False(part.HasManualLeadIns)); // Planning never mutates.
        ((LineLeadIn)parameters.ExternalLeadIn).Length = length * 3; // Confirmed settings were copied at capture.

        var commit = proposal.Apply();

        Assert.Equal(CuttingCommitStatus.Applied, commit.Status);
        foreach (var planned in proposal.Plates)
            foreach (var placement in planned.Result.ProposedOrder)
                Assert.True(ProgramContent.Equal(placement.CopyProgram(), placement.SourcePart.Program));
        Assert.All(first.Parts.Concat(second.Parts), part => Assert.True(part.HasManualLeadIns));
        Assert.NotNull(first.CuttingParameters);
        Assert.NotSame(parameters, first.CuttingParameters);
        Assert.NotSame(first.CuttingParameters, second.CuttingParameters);
        Assert.Equal(length, ((LineLeadIn)first.CuttingParameters.ExternalLeadIn).Length);
        Assert.Equal(length, ((LineLeadIn)second.CuttingParameters.ExternalLeadIn).Length);
    }

    [Fact]
    public void Apply_OneBlockedPlate_RefusesTheWholeBatchAndNamesThePlateAndPart()
    {
        var nest = new Nest();
        var ready = Plate(nest, Clean("open", 1, 1));
        var locked = Clean("locked", 1, 1);
        locked.LeadInsLocked = true; // Locked programs never regenerate: no lead-in is a conflict.
        var blocked = Plate(nest, locked);
        var readyProgram = ready.Parts[0].Program;

        var proposal = CuttingPlanBatch.Capture([ready, blocked], ExplicitContourTests.Parameters(),
            preservePartOrder: false, plateNumbers: [3, 7]).Plan();

        Assert.False(proposal.CanApply);
        Assert.True(proposal.Plates[0].IsReady);
        var refused = proposal.Plates[1];
        Assert.Equal(CuttingPlanStatus.ConstraintConflict, refused.Result.Status);
        Assert.False(refused.KeptCurrentOrder); // Only a budget-exhausted search is retried.
        var text = string.Join("\n", proposal.Describe("in"));
        Assert.StartsWith("Apply is unavailable: 1 of 2 plates", text);
        Assert.Contains("Plate 3: ready.", text);
        Assert.Contains("Plate 7: blocked", text);
        Assert.Contains("- Part 1 (locked):", text);

        var commit = proposal.Apply();

        Assert.Equal(CuttingCommitStatus.InvalidInput, commit.Status);
        Assert.Same(readyProgram, ready.Parts[0].Program);
        Assert.False(ready.Parts[0].HasManualLeadIns);
        Assert.Null(ready.CuttingParameters);
        Assert.Null(blocked.CuttingParameters);
    }

    [Fact]
    public void Plan_FreeSearchOutOfBudget_KeepsTheCurrentOrderAndSaysSo()
    {
        var nest = new Nest();
        var plate = Plate(nest, Grid(4));
        var order = plate.Parts.ToArray();

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false, null,
            reorderBudget: 50, CancellationToken.None).Plan();

        var planned = Assert.Single(proposal.Plates);
        Assert.True(planned.KeptCurrentOrder);
        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, planned.ReorderAttempt!.Status);
        Assert.True(planned.IsReady);
        Assert.False(planned.OrderChanged);
        Assert.Equal(order, planned.Result.ProposedOrder.Select(p => p.SourcePart));
        Assert.Contains("the current order is kept", string.Join("\n", proposal.Describe("in")));
        Assert.True(proposal.CanApply);
    }

    [Fact]
    public void Plan_PreservedOrder_DoesNotSearchForANewOrder()
    {
        var nest = new Nest();
        var plate = Plate(nest, Grid(4));
        var phases = new List<CuttingPlanPhase>();

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), preservePartOrder: true)
            .Plan(new Recorder(phases));

        var planned = Assert.Single(proposal.Plates);
        Assert.True(planned.IsReady);
        Assert.False(planned.KeptCurrentOrder);
        Assert.Equal(new[] { CuttingPlanPhase.KeepingOrder }, phases);
    }

    [Fact]
    public void Apply_AfterALiveEdit_IsStaleAndChangesNothing()
    {
        var nest = new Nest();
        var plate = Plate(nest, Clean("a", 1, 1), Clean("b", 12, 1));
        var programs = plate.Parts.Select(p => p.Program).ToArray();
        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();
        Assert.True(proposal.CanApply);

        plate.Parts[1].Offset(0, 1);
        var commit = proposal.Apply();

        Assert.Equal(CuttingCommitStatus.Stale, commit.Status);
        Assert.Equal(programs, plate.Parts.Select(p => p.Program));
        Assert.All(plate.Parts, part => Assert.False(part.HasManualLeadIns));
        Assert.Null(plate.CuttingParameters);
    }

    [Fact]
    public void Plan_Cancelled_OffersNothingToApply()
    {
        var nest = new Nest();
        var plate = Plate(nest, Clean("a", 1, 1));
        var batch = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var proposal = batch.Plan(token: cancellation.Token);

        Assert.True(proposal.IsCancelled);
        Assert.False(proposal.CanApply);
        Assert.Equal("Planning was cancelled. Nothing has changed.", proposal.Describe("in")[0]);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
        Assert.False(plate.Parts[0].HasManualLeadIns);
        Assert.Null(plate.CuttingParameters);
    }

    [Fact]
    public void BuildPreview_IsDetachedAndShowsTheProposalOrTheCurrentParts()
    {
        var nest = new Nest();
        var plate = Plate(nest, Grid(4));
        plate.Quantity = 3;
        var locked = Clean("locked", 1, 1);
        locked.LeadInsLocked = true;
        var blocked = Plate(nest, locked, Clean("free", 12, 1));
        var drawing = plate.Parts[0].BaseDrawing;
        var nested = drawing.Quantity.Nested;
        var liveParts = plate.Parts.ToArray();
        var livePrograms = liveParts.Select(p => p.Program).ToArray();
        var proposal = CuttingPlanBatch.Capture([plate, blocked], ExplicitContourTests.Parameters(), false).Plan();

        var preview = proposal.BuildPreview(0);
        var current = proposal.BuildPreview(1);

        Assert.Equal(0, preview.Quantity);
        Assert.Equal(plate.Size, preview.Size);
        Assert.Equal(nested, drawing.Quantity.Nested);
        var proposed = proposal.Plates[0].Result.ProposedOrder;
        Assert.Equal(proposed.Count, preview.Parts.Count);
        for (var i = 0; i < proposed.Count; i++)
        {
            Assert.DoesNotContain(preview.Parts[i], liveParts);
            Assert.Same(proposed[i].SourcePart.BaseDrawing, preview.Parts[i].BaseDrawing);
            Assert.Equal(proposed[i].Location, preview.Parts[i].Location);
            Assert.True(ProgramContent.Equal(proposed[i].CopyProgram(), preview.Parts[i].Program));
        }
        Assert.Equal(liveParts, plate.Parts);
        Assert.Equal(livePrograms, plate.Parts.Select(p => p.Program));
        Assert.Equal(blocked.Parts.Select(p => p.Location), current.Parts.Select(p => p.Location));
        Assert.All(current.Parts, part => Assert.DoesNotContain(part, blocked.Parts));
    }

    [Theory]
    [InlineData(0, 20000)]
    [InlineData(10, 20000)]
    [InlineData(100, 40000)]
    [InlineData(6_000_000, int.MaxValue)]
    public void KeepOrderBudget_ScalesWithPartCountFromTheDefault(int parts, int expected) =>
        Assert.Equal(expected, CuttingPlanBatch.KeepOrderBudget(parts));

    [Fact]
    public void Capture_RejectsMissingDuplicateOrMisnumberedPlates()
    {
        var nest = new Nest();
        var plate = Plate(nest, Clean("a", 1, 1));
        var parameters = ExplicitContourTests.Parameters();

        Assert.Throws<ArgumentNullException>(() => CuttingPlanBatch.Capture(null, parameters, false));
        Assert.Throws<ArgumentNullException>(() => CuttingPlanBatch.Capture([plate], null, false));
        Assert.Throws<ArgumentException>(() => CuttingPlanBatch.Capture([], parameters, false));
        Assert.Throws<ArgumentException>(() => CuttingPlanBatch.Capture([plate, plate], parameters, false));
        Assert.Throws<ArgumentException>(() => CuttingPlanBatch.Capture([plate, null], parameters, false));
        Assert.Throws<ArgumentException>(() => CuttingPlanBatch.Capture([plate], parameters, false, [1, 2]));
    }

    private static Plate Plate(Nest nest, params Part[] parts)
    {
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        foreach (var part in parts)
            plate.Parts.Add(part);
        return plate;
    }

    private static Part Clean(string name, double x, double y) =>
        new(new Drawing(name, ExplicitContourTests.Square(false)), new Vector(x, y));

    private static Part[] Grid(int count)
    {
        var drawing = new Drawing("grid", PreparedContourTests.Holes());
        var side = (int)System.Math.Ceiling(System.Math.Sqrt(count));
        return Enumerable.Range(0, count)
            .Select(i => new Part(drawing, new Vector(1 + i % side * 11, 1 + i / side * 11)))
            .ToArray();
    }

    private sealed class Recorder(List<CuttingPlanPhase> phases) : IProgress<CuttingPlanProgress>
    {
        public void Report(CuttingPlanProgress value) => phases.Add(value.Phase);
    }
}
