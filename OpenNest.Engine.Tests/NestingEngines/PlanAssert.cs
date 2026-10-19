using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>Checks an engine's whole-job plan reports (<see cref="NestJobStage.PlanImproved"/>).</summary>
internal static class PlanAssert
{
    /// <summary>
    /// Solves with and without a progress sink and asserts that plans are reported as proposals
    /// with no committed counts, before any commit; that the last plan is the returned layout;
    /// and that reporting does not change the layout. Returns the reported plans in order.
    /// </summary>
    public static IReadOnlyList<NestJobResult> ReportedAndEndWithTheResult(Func<INestingEngine> engine, NestJob job)
    {
        var reports = new List<NestJobProgress>();
        var result = engine().Solve(job, new Capture(reports.Add));
        var silent = engine().Solve(job);

        var plans = reports.Where(r => r.Stage == NestJobStage.PlanImproved).ToList();
        Assert.NotEmpty(plans);
        Assert.All(plans, p => Assert.Equal((0, 0), (p.CommittedPlates, p.CommittedParts)));
        Assert.All(plans, p => Assert.NotNull(p.Plan));
        var firstCommit = reports.FindIndex(r => r.Stage == NestJobStage.PlateCommitted);
        if (firstCommit >= 0)
            Assert.True(reports.FindLastIndex(r => r.Stage == NestJobStage.PlanImproved) < firstCommit);
        Assert.Equal(Layout(result), Layout(plans[^1].Plan));
        Assert.Equal(Layout(silent), Layout(result));
        return plans.Select(p => p.Plan).ToList();
    }

    public static IEnumerable<(int Plate, string StockId, NestJobPlacement Pose)> Layout(NestJobResult result) =>
        result.Plates.SelectMany(p => p.Placements.Select(pose => (p.PlateIndex, p.StockId, pose)));

    private sealed class Capture(Action<NestJobProgress> report) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value) => report(value);
    }
}
