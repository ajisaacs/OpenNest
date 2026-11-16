using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// A stock that takes the whole job on one sheet but loses the decode's per-sheet pick is still
/// a complete plan: it competes with the decode's own plan and is reported.
/// </summary>
public class IrregularPassedOverStockTests
{
    // Five 10 x 10 squares. "small" holds four, so the look-ahead rates it 441 + 100 * (441 / 400),
    // about 551, under the strip's 572; the fifth square then needs a second small sheet (882).
    private static NestJob UnderestimatedLeftovers() => Job(
        new[] { Part("square", Rectangle(10, 10), 5) },
        new[] { Stock("small", 21, 21, spacing: 0.25), Stock("strip", 11, 52, spacing: 0.25) }
    );

    [Fact]
    public void APassKeepsTheStockThatFinishedTheJob_WhenItsOwnPlanEndsUpDearer()
    {
        var job = UnderestimatedLeftovers();

        var result = new IrregularNestingEngine().PlanForTesting(job, 0, null);

        LayoutAssert.Valid(job, result);
        var sheet = Assert.Single(result.Plates);
        Assert.Equal("strip", sheet.StockId);
        Assert.Equal(5, sheet.Placements.Count);
    }

    [Fact]
    public void TheDearerStockThatAlsoFinishesTheJob_IsReportedAsAnAlternative()
    {
        var job = Job(
            new[] { Part("square", Rectangle(10, 10), 4) },
            new[] { Stock("big", 60, 120, spacing: 0.25), Stock("small", 25, 25, spacing: 0.25) }
        );
        var reports = new List<NestJobProgress>();

        var result = new IrregularNestingEngine().Solve(job, new Inline(reports.Add));

        Assert.Equal("small", Assert.Single(result.Plates).StockId);
        var onBig = reports.Where(r => r.Stage == NestJobStage.PlanAlternative)
            .Select(r => r.Plan!)
            .Where(p => p.Plates.Count == 1 && p.Plates[0].StockId == "big")
            .ToList();
        Assert.NotEmpty(onBig);
        Assert.All(onBig, plan =>
        {
            LayoutAssert.Valid(job, plan);
            Assert.Equal(4, plan.Plates[0].Placements.Count);
        });
    }

    [Fact]
    public void ReportingNeverChangesTheLayout()
    {
        var job = UnderestimatedLeftovers();

        var silent = new IrregularNestingEngine().Solve(job);
        var reported = new IrregularNestingEngine().Solve(job, new Inline(_ => { }));

        Assert.Equal(
            silent.Plates.Select(p => (p.StockId, string.Join(";", p.Placements.Select(q => $"{q.X:R},{q.Y:R},{q.Rotation:R}")))),
            reported.Plates.Select(p => (p.StockId, string.Join(";", p.Placements.Select(q => $"{q.X:R},{q.Y:R},{q.Rotation:R}")))));
    }

    private sealed class Inline(Action<NestJobProgress> report) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value) => report(value);
    }
}
