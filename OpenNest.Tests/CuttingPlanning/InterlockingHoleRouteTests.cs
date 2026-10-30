using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class InterlockingHoleRouteTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InterlockingHoledPartsPlanWithinDefaultBudget(bool leads, bool keep)
    {
        var a = Polygon([new(2.5, 2), new(2.5, 4), new(30, 4), new(30, 10),
            new(2.5, 10), new(2.5, 12), new(32, 12), new(32, 2)]);
        var b = Polygon([new(0, 4.5), new(0, 14.5), new(20, 14.5), new(20, 12.5),
            new(2, 12.5), new(2, 6.5), new(20, 6.5), new(20, 4.5)]);
        foreach (var x in new[] { 3.5, 12, 21, 31 })
            foreach (var y in new[] { 3.0, 11.0 }) Hole(a, x, y);
        foreach (var x in new[] { 1.0, 7, 13, 19 })
            foreach (var y in new[] { 5.5, 13.5 }) Hole(b, x, y);
        var parts = new[] { new Part(new Drawing("left-open bracket", a)), new Part(new Drawing("right-open bracket", b)) };
        var parameters = new CuttingParameters();
        if (leads)
        {
            parameters.ExternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
            parameters.InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
        }
        var before = parts.Select(p => ExplicitContourTests.Fingerprint(p.Program)).ToArray();
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts,
            confirmedParameters: parameters, preservePartOrder: keep));
        var result = CuttingPlanService.Plan(snapshot);
        Assert.True(result.Status == CuttingPlanStatus.Ready,
            $"{result.Status}: {result.Expansions} " + string.Join(";", result.Findings.Select(f => f.Message)));
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(2, result.ProposedOrder.Count);
        Assert.All(result.ProposedOrder, p => Assert.Equal(9, p.ContourChoices.Count));
        var replay = CuttingPlanService.ReplayPrograms(snapshot, result.ProposedOrder, 0, default);
        Assert.Equal(CuttingPlanStatus.Ready, replay.Status);
        Assert.True(replay.IndependentlyReplayed);
        Assert.Equal(before, parts.Select(p => ExplicitContourTests.Fingerprint(p.Program)));
        Assert.InRange(result.Expansions, 1, 20000);
        if (keep)
            Assert.Equal(new[] { 0, 1 }, result.ProposedOrder.Select(p => p.SourceOrdinal));
        var repeated = CuttingPlanService.Plan(snapshot);
        Assert.Equal(CuttingPlanStatus.Ready, repeated.Status);
        Assert.Equal(result.Expansions, repeated.Expansions);
        Assert.Equal(result.ProposedOrder.Select(p => ExplicitContourTests.Fingerprint(p.CopyProgram())),
            repeated.ProposedOrder.Select(p => ExplicitContourTests.Fingerprint(p.CopyProgram())));

        var bounded = CuttingPlanService.Plan(snapshot.WithSearchLimits(1, 16));
        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, bounded.Status);
        Assert.Equal(1, bounded.Expansions);
        Assert.Empty(bounded.ProposedOrder);
        using var cancellation = new CancellationTokenSource();
        var cancelAt = result.Expansions - 1;
        var canceled = CuttingPlanService.Plan(new CuttingPlanRequest(parts,
            confirmedParameters: parameters, preservePartOrder: keep)
        {
            ExpansionObserver = n => { if (n == cancelAt) cancellation.Cancel(); }
        }, cancellation.Token);
        Assert.Equal(CuttingPlanStatus.Canceled, canceled.Status);
        Assert.Equal(cancelAt, canceled.Expansions);
        Assert.Empty(canceled.ProposedOrder);
        Assert.False(canceled.IndependentlyReplayed);
        Assert.Equal(before, parts.Select(p => ExplicitContourTests.Fingerprint(p.Program)));
    }

    private static OpenNest.CNC.Program Polygon(Vector[] vertices)
    {
        var program = new OpenNest.CNC.Program();
        program.MoveTo(vertices[0].X, vertices[0].Y);
        foreach (var point in vertices.Skip(1).Append(vertices[0])) program.LineTo(point.X, point.Y);
        return program;
    }

    private static void Hole(OpenNest.CNC.Program program, double x, double y)
    {
        program.MoveTo(x + 0.2, y);
        program.ArcTo(x + 0.2, y, x, y, RotationType.CCW);
    }
}
