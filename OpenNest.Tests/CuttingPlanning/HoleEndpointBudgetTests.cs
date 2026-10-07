using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class HoleEndpointBudgetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterFixedApproachTriesOtherEndpointsBeforeHoleCombinations(bool preserveOrder)
    {
        var parameters = HoleLookAheadTests.Parameters();
        var clean = HoleLookAheadTests.MixedHoles();
        foreach (var centre in new[] { new Vector(4, 8), new Vector(8, 8) })
        {
            clean.MoveTo(centre.X + 0.75, centre.Y);
            clean.ArcTo(centre.X + 0.75, centre.Y, centre.X, centre.Y, RotationType.CCW);
        }
        var first = new Part(new Drawing("four holes", clean));
        var nextClean = LeadPathValidationTests.Rectangle(0, 0, 2, 2);
        nextClean.MoveTo(-22, 10);
        nextClean.Codes.Add(new LinearMove(-21, 10) { Layer = LayerType.Scribe });
        nextClean.MoveTo(0, 10);
        nextClean.Codes.Add(new LinearMove(1, 10) { Layer = LayerType.Scribe });
        var next = new Part(new Drawing("fixed marked part", nextClean), new Vector(20, 2));
        var nextPrepared = PreparedContours.Capture(nextClean, parameters);
        Assert.True(next.RestoreLeadInProgram(nextPrepared.Emit(
            [nextPrepared.ClosestEntry(0, new Vector(3, 1))]), true));
        var before = new[] { first, next }.Select(p => ExplicitContourTests.Fingerprint(p.Program)).ToArray();
        var start = new Vector(-2, 4);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([first, next], start,
            confirmedParameters: parameters, preservePartOrder: preserveOrder));
        var source = snapshot.Placements[0];
        var prepared = source.Prepared;
        var adapter = new ContourEntryFeasibility(prepared, source.Location, source.Material,
            snapshot.Placements.Select(p => p.Material).ToArray());
        var target = new Vector(21, 3);
        var outside = ContourEntrySelection.Select(prepared.AutomaticEntryCandidatesWithFallbacks(4, target)
            .RankTowardNextCut(target, start), c => adapter.Check(c.Choice)).Choices[0];
        var centres = prepared.HoleCentres().Select(c => c ?? Vector.Zero).ToArray();
        var route = CuttingHoleOrder.Plan(new[] { 0, 1, 2, 3 }, centres, start,
            PreferredContourEntries.Pierce(prepared, outside, default));
        var preferred = PreferredContourEntries.TryPlan(prepared, outside, route, centres, start,
            c => adapter.Check(c.Choice));
        Assert.True(preferred.IsPreferred, preferred.Reason);
        var execution = ExecutionMotionReader.Read(prepared.Emit(preferred.HoleChoices.Append(outside).ToArray()),
            Vector.Zero, start, default);
        var checker = new ReleasedContourState();
        Assert.Empty(checker.Check(execution, start, 1));
        Assert.Contains(checker.Check(snapshot.Placements[1].Execution, execution.DeparturePoint, 2),
            f => f.Kind == PostVerificationKind.RapidCrossing);

        var result = CuttingPlanService.Plan(snapshot);

        Assert.True(result.Status == CuttingPlanStatus.Ready,
            $"{result.Status}, {result.Expansions} expansions: " + string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(new[] { 0, 1 }, result.ProposedOrder.Select(p => p.SourceOrdinal));
        Assert.Equal(5, result.ProposedOrder[0].ContourChoices.Count);
        Assert.Equal(4, result.ProposedOrder[0].ContourChoices[^1].ContourOrdinal);
        Assert.NotEqual(outside.Point, result.ProposedOrder[0].ContourChoices[^1].Point);
        Assert.InRange(result.Expansions, 1, 20000);
        var replay = CuttingPlanService.ReplayPrograms(snapshot, result.ProposedOrder, 0, default);
        Assert.Equal(CuttingPlanStatus.Ready, replay.Status);
        Assert.True(replay.IndependentlyReplayed);
        Assert.Equal(before, new[] { first, next }.Select(p => ExplicitContourTests.Fingerprint(p.Program)));
        Assert.Equal(before[1], ExplicitContourTests.Fingerprint(result.ProposedOrder[1].CopyProgram()));

        var bounded = CuttingPlanService.Plan(new CuttingPlanRequest([first, next], start,
            expansionBudget: 1, confirmedParameters: parameters, preservePartOrder: preserveOrder));
        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, bounded.Status);
        Assert.Equal(1, bounded.Expansions);
        Assert.Empty(bounded.ProposedOrder);
        Assert.False(bounded.IndependentlyReplayed);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(CuttingPlanStatus.Cancelled, CuttingPlanService.Plan(snapshot, cancelled.Token).Status);
        Assert.Equal(before, new[] { first, next }.Select(p => ExplicitContourTests.Fingerprint(p.Program)));
    }
}
