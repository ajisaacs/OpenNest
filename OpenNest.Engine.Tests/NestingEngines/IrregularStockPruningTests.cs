using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// A tail re-plan's decode skips stock trials that can neither be chosen nor change the choice:
/// it packs fewer sheets and decides exactly as when it packs every trial.
/// </summary>
public class IrregularStockPruningTests
{
    private static NestJob MixedJob(NestJobOptions? options) =>
        Job(
            new[] { Part("disc", Disc(3), 10), Part("ell", LShape(12, 8, 4), 10), Part("tri", Triangle(9, 7), 8) },
            // Small sheets first, so a trial is packed before the large sheet is considered.
            new[] { Stock("small", 20, 30, spacing: 0.5), Stock("sheet", 40, 60, spacing: 0.5), Stock("large", 120, 200, spacing: 0.5) },
            options
        );

    private static List<(string, string, int, double, double, double)> Placements(NestJobResult result) =>
        result.Plates.SelectMany(p => p.Placements.Select(q => (p.Stock.Id, q.PartId, q.InstanceIndex, q.X, q.Y, q.Rotation)))
            .ToList();

    public static TheoryData<bool> Salvage => new() { false, true };

    [Theory]
    [MemberData(nameof(Salvage))]
    public void SkippedTrialsChangeNoChoice(bool salvage)
    {
        var options = salvage ? new NestJobOptions(salvageRate: 0.5, minimumSalvageDimension: 1) : null;
        var all = 0;
        var pruned = 0;
        var full = new IrregularNestingEngine { PruneStockTrials = false, SheetPacked = () => all++ }.Solve(MixedJob(options));
        var skipped = new IrregularNestingEngine { SheetPacked = () => pruned++ }.Solve(MixedJob(options));

        Assert.Equal(Placements(full), Placements(skipped));
        Assert.NotEmpty(Placements(full));
        Assert.True(pruned < all, $"packed {pruned} of {all} sheets");
    }
}
