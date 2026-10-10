using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class LazyCuttingSearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RankedRouteDoesNotSpendBudgetEmittingUnusedSiblings(bool preserveOrder)
    {
        var parts = Parts();
        var programs = parts.Select(p => p.Program).ToArray();
        var parameters = ExplicitContourTests.Parameters();
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts, new Vector(-2, -2),
            expansionBudget: 36, confirmedParameters: parameters, preservePartOrder: preserveOrder));
        var result = CuttingPlanService.Plan(snapshot);
        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(new[] { 0, 1 }, result.ProposedOrder.Select(p => p.SourceOrdinal));
        Assert.InRange(result.Expansions, 1, 36);
        var replay = CuttingPlanService.ReplayPrograms(snapshot, result.ProposedOrder, 0, default);
        Assert.Equal(CuttingPlanStatus.Ready, replay.Status);
        Assert.True(replay.IndependentlyReplayed);
        Assert.Equal(programs, parts.Select(p => p.Program));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LazyTraversalKeepsBudgetAndMidSearchCancellationFailClosed(bool preserveOrder)
    {
        var parts = Parts();
        var programs = parts.Select(p => p.Program).ToArray();
        var parameters = ExplicitContourTests.Parameters();
        var bounded = CuttingPlanService.Plan(new CuttingPlanRequest(parts, new Vector(-2, -2),
            expansionBudget: 1, confirmedParameters: parameters, preservePartOrder: preserveOrder));
        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, bounded.Status);
        Assert.Equal(1, bounded.Expansions);
        Assert.Empty(bounded.ProposedOrder);
        Assert.False(bounded.IndependentlyReplayed);
        using var cancellation = new CancellationTokenSource();
        var cancelled = CuttingPlanService.Plan(new CuttingPlanRequest(parts, new Vector(-2, -2),
            confirmedParameters: parameters, preservePartOrder: preserveOrder)
        { ExpansionObserver = n => { if (n == 10) cancellation.Cancel(); } }, cancellation.Token);
        Assert.Equal(CuttingPlanStatus.Cancelled, cancelled.Status);
        Assert.Equal(10, cancelled.Expansions);
        Assert.Empty(cancelled.ProposedOrder);
        Assert.False(cancelled.IndependentlyReplayed);
        Assert.Equal(programs, parts.Select(p => p.Program));
    }

    private static Part[] Parts() =>
    [
        new(new Drawing("first", LeadPathValidationTests.Rectangle(0, 0, 10, 10)), Vector.Zero),
        new(new Drawing("second", LeadPathValidationTests.Rectangle(0, 0, 10, 10)), new Vector(20, 0)),
    ];
}
