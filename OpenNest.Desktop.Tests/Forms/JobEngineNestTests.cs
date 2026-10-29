using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;

namespace OpenNest.Desktop.Tests.Forms;

public class JobEngineNestTests
{
    private sealed class Sink<T> : IProgress<T>
    {
        public List<T> Reports { get; } = [];

        public void Report(T value) => Reports.Add(value);
    }

    [Fact]
    public void EvaluatingReport_BecomesAOneBasedPlateDescription()
    {
        var progress = new Sink<NestProgress>();

        JobEngineNest.CreateProgress("Irregular", progress)
            .Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, "48x96", 2, 2, 30));

        var report = Assert.Single(progress.Reports);
        Assert.Equal(NestPhase.Custom, report.Phase);
        Assert.Equal(3, report.PlateNumber);
        Assert.Equal("Irregular: evaluating plate 3 on stock 48x96", report.Description);
    }

    [Fact]
    public void UnknownPlateIndex_CountsFromTheCommittedPlates()
    {
        var progress = new Sink<NestProgress>();

        JobEngineNest.CreateProgress("Fill", progress)
            .Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, "s", -1, 4, 0));

        Assert.Equal(5, Assert.Single(progress.Reports).PlateNumber);
    }

    [Fact]
    public void CommittedReport_NamesTheCommittedCounts()
    {
        var progress = new Sink<NestProgress>();

        JobEngineNest.CreateProgress("Fill", progress)
            .Report(new NestJobProgress(NestJobStage.PlateCommitted, "s", 0, 2, 17));

        var report = Assert.Single(progress.Reports);
        Assert.Equal(2, report.PlateNumber);
        Assert.Equal("Fill: committed plate 2 (17 parts placed)", report.Description);
    }

    [Fact]
    public void LegacyProgress_PassesThroughUnchanged()
    {
        var progress = new Sink<NestProgress>();
        var legacy = new NestProgress { Phase = NestPhase.Linear, PlateNumber = 9 };

        JobEngineNest.CreateProgress("Fill", progress)
            .Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, "s", 0, 0, 0, legacy));

        Assert.Same(legacy, Assert.Single(progress.Reports));
    }

    [Fact]
    public void ImprovedPlan_GoesToThePlanSinkOnly()
    {
        var progress = new Sink<NestProgress>();
        var plans = new Sink<IReadOnlyList<ProposedPlate>>();
        IReadOnlyList<ProposedPlate> sheets = [];

        JobEngineNest.CreateProgress("Irregular", progress, plans)
            .Report(new NestJobProgress(NestJobStage.PlanImproved, "s", 0, 0, 0) { PlanSheets = sheets });

        Assert.Empty(progress.Reports);
        Assert.Same(sheets, Assert.Single(plans.Reports));
    }

    [Fact]
    public void ImprovedPlan_WithoutAPlanSink_IsDescribed()
    {
        var progress = new Sink<NestProgress>();

        JobEngineNest.CreateProgress("Irregular", progress)
            .Report(new NestJobProgress(NestJobStage.PlanImproved, "s", 0, 0, 0));

        Assert.Equal("Irregular: found a better plan (0 sheets)", Assert.Single(progress.Reports).Description);
    }

    [Fact]
    public void NullReport_IsIgnored()
    {
        var progress = new Sink<NestProgress>();

        JobEngineNest.CreateProgress("Fill", progress).Report(null!);

        Assert.Empty(progress.Reports);
    }
}
