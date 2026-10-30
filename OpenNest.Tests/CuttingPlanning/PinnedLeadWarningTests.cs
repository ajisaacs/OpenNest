using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// D1a for drawing pins: a pinned lead that meets material is a named warning on an UNVERIFIED
/// proposal, never relocated or shortened. The rapids between contours stay strict.
/// </summary>
public class PinnedLeadWarningTests
{
    [Fact]
    public void PinnedLeadIntoNeighbor_IsUnverifiedWithTheRestPlannedAndTheEntryKept()
    {
        var (plate, pinned, neighbor) = NeighborFixture(pins: true);

        var proposal = CuttingPlanBatch.Capture([plate], PinnedContourTests.NestSettings(), false).Plan();

        var planned = Assert.Single(proposal.Plates);
        Assert.True(planned.HasOnlyFixedLeadWarnings, string.Join("\n", proposal.Describe("in")));
        var warning = Assert.Single(planned.Result.Findings);
        Assert.Equal(PostVerificationKind.FixedLead, warning.Kind);
        Assert.Same(pinned, warning.SourcePart);
        Assert.Same(neighbor, warning.OtherSourcePart);
        Assert.Equal(new FixedLeadWarning(1, 1, 1), warning.FixedLead);
        Assert.Equal("Drawing pin 1 lead on cutting contour 1 contacts or enters another part's material. "
            + "The pinned contour is cut exactly as its drawing says. Increase spacing or move this part away "
            + "from the part it meets, then replan.", warning.Message);
        // The search never relocates the pinned entry, and the neighbor is planned normally.
        var proposed = planned.Result.ProposedOrder;
        var placement = Assert.Single(proposed, p => ReferenceEquals(p.SourcePart, pinned));
        Assert.Equal(new Vector(10, 5), Assert.Single(placement.ContourChoices).Point);
        var lead = ExecutionMotionReader.Read(placement.CopyProgram(), pinned.Location, null, default).Motions
            .Single(m => m.Layer == LayerType.Leadin);
        Assert.Equal(1.0, lead.Length, 9); // Not shortened to fit.
        Assert.True(Assert.Single(proposed, p => ReferenceEquals(p.SourcePart, neighbor)).IsRegenerated);
        Assert.Equal(CuttingCommitStatus.InvalidInput, proposal.Apply().Status);
        Assert.Equal(CuttingCommitStatus.Applied, proposal.Apply(acceptWarnings: true).Status);
    }

    [Fact]
    public void UnpinnedControl_IsReady()
    {
        var (plate, _, _) = NeighborFixture(pins: false);

        var proposal = CuttingPlanBatch.Capture([plate], PinnedContourTests.NestSettings(), false).Plan();

        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
    }

    [Fact]
    public void PinsOffOnThePinnedPart_IsReady()
    {
        var (plate, pinned, _) = NeighborFixture(pins: true);
        pinned.DrawingPinsOff = true;

        var proposal = CuttingPlanBatch.Capture([plate], PinnedContourTests.NestSettings(), false).Plan();

        Assert.True(proposal.CanApply, string.Join("\n", proposal.Describe("in")));
    }

    [Fact]
    public void PinnedHoleLeadIntoItsOwnPartsMaterial_Warns()
    {
        // Two square holes; hole 2's pinned 2.5 lead runs from beyond its right edge.
        var program = new Program();
        program.MoveTo(2, 2); program.LineTo(4, 2); program.LineTo(4, 4); program.LineTo(2, 4); program.LineTo(2, 2);
        program.MoveTo(4.5, 2); program.LineTo(6.5, 2); program.LineTo(6.5, 4); program.LineTo(4.5, 4); program.LineTo(4.5, 2);
        program.MoveTo(0, 0); program.LineTo(0, 10); program.LineTo(10, 10); program.LineTo(10, 0); program.LineTo(0, 0);
        var drawing = new Drawing("holes", program);
        drawing.CutPins.Add(new ContourCutPin(new Vector(4.5, 3), new LineLeadIn { Length = 2.5, ApproachAngle = 90 },
            new NoLeadOut()));
        var part = new Part(drawing, new Vector(1, 1));

        var result = CuttingPlanService.Plan(new CuttingPlanRequest([part], new Vector(-2, -2),
            confirmedParameters: PinnedContourTests.NestSettings()));

        Assert.Equal(CuttingPlanStatus.BestEffort, result.Status);
        Assert.True(result.IndependentlyReplayed);
        var warning = Assert.Single(result.Findings);
        Assert.StartsWith("Drawing pin 1 lead on cutting contour", warning.Message);
        Assert.Contains("contacts or enters its own part's material", warning.Message);
        Assert.Null(warning.OtherSourcePart);
        Assert.Equal(1, warning.FixedLead!.PinNumber);
    }

    [Fact]
    public void RapidIntoAPinnedContour_StaysStrict()
    {
        // Holes(): circles at (3, 3) and (7, 3) in a 10 x 10 square; the outside is pinned at
        // (10, 3). Cutting the left hole last and leaving from its far side makes the planner's
        // rapid to the pinned pierce cross both finished holes: a refusal, never a warning.
        var drawing = new Drawing("holes", PreparedContourTests.Holes());
        drawing.CutPins.Add(new ContourCutPin(new Vector(10, 3), new LineLeadIn { Length = 0.25, ApproachAngle = 90 },
            new NoLeadOut()));
        var part = new Part(drawing, Vector.Zero);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], new Vector(-2, -2),
            confirmedParameters: PinnedContourTests.NestSettings()));
        var source = snapshot.Placements[0];
        var prepared = source.Prepared!;
        var choices = new[] { prepared.Entry(1, 0, new Vector(6, 3)), prepared.Entry(0, 0, new Vector(2, 3)),
            prepared.PinnedEntry(2)! };
        var program = prepared.Emit(choices);
        var proposal = source.Propose(program, ExecutionMotionReader.Read(program, Vector.Zero, new Vector(-2, -2), default),
            choices);

        var replay = CuttingPlanService.ReplayPrograms(snapshot, [proposal], 0, default);

        Assert.Equal(CuttingPlanStatus.ConstraintConflict, replay.Status);
        Assert.Contains(replay.Findings, f => f.Kind == PostVerificationKind.RapidCrossing);
        Assert.DoesNotContain(replay.Findings, f => f.Kind == PostVerificationKind.FixedLead);
    }

    [Fact]
    public void FeasibilityProbe_AcceptsAPinnedEntryAsGiven()
    {
        var (plate, pinned, _) = NeighborFixture(pins: true);
        var snapshot = CuttingPlanService.Capture(CuttingPlanRequest.ForPlate(plate,
            confirmedParameters: PinnedContourTests.NestSettings()));
        var source = snapshot.Placements.Single(p => ReferenceEquals(p.SourcePart, pinned));
        var adapter = new ContourEntryFeasibility(source.Prepared!, source.Location, source.Material!,
            snapshot.Placements.Select(p => p.Material!).ToArray());

        var verdict = adapter.Check(source.Prepared!.PinnedEntry(0)!);

        Assert.True(verdict.IsClear);
        Assert.Equal(0, adapter.EvaluationCount);
    }

    /// <summary>
    /// A 10 x 10 square pinned at its right edge midpoint with a 1.0 straight lead, and a
    /// 2 x 2 neighbor 0.3 to its right that the lead runs through.
    /// </summary>
    private static (Plate Plate, Part Pinned, Part Neighbor) NeighborFixture(bool pins)
    {
        var drawing = new Drawing("pinned", ExplicitContourTests.Square(false));
        if (pins)
            drawing.CutPins.Add(new ContourCutPin(new Vector(10, 5), new LineLeadIn { Length = 1.0, ApproachAngle = 90 },
                new NoLeadOut()));
        var pinned = new Part(drawing, Vector.Zero);
        var neighbor = new Part(new Drawing("neighbor", LeadPathValidationTests.Rectangle(0, 0, 2, 2)),
            new Vector(10.3, 4));
        var plate = new Nest().CreatePlate();
        plate.Parts.Add(pinned);
        plate.Parts.Add(neighbor);
        return (plate, pinned, neighbor);
    }
}
