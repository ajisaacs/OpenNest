using OpenNest.Benchmark;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.Engine;

/// <summary>
/// The help tutorial's parts (30 gussets, 20 ells, 10 plates, 12 flanges on a 60 x 120 sheet) nest
/// with like parts grouped. Before grouping, Irregular ended the layout at 38.06"; a hand-grouped
/// layout of the same parts ends at 34.1".
/// </summary>
public class GroupedNestingTests
{
    [Fact]
    public void TutorialPartsNestInAShorterStripWhenGrouped()
    {
        var path = Path.Combine(PlanningCharacterizationTests.RepositoryRoot(), "test-data", "help-tutorial", "first-nest.nest");
        var job = Assert.Single(JobLoader.Load(path)).BuildNestJob(1);

        var result = new IrregularNestingEngine().Solve(job);

        var plate = Assert.Single(NestResultMaterializer.Materialize(job, result).Nest.Plates);
        Assert.Equal(72, plate.Parts.Count);
        var right = plate.Parts.Max(p => p.BoundingBox.Right);
        Assert.True(right < 35.5, $"Layout ends at {right:F3}");
    }
}
