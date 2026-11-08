using OpenNest.Benchmark;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.Engine;

/// <summary>
/// Seven unlike sample parts (130 copies: base plates, L brackets, gussets, flanges, links, clamp
/// jaws, hex covers) on a 60 x 120 sheet. Before banding, Irregular ended the layout at 107.7":
/// each group packed as ragged columns, the links scattered into the gaps between them. Banded,
/// each group stands in its own full-height column and the layout ends at 102.9".
/// </summary>
public class BandedNestingTests
{
    [Fact]
    public void MixedSamplePartsNestInTidyBands()
    {
        var manifest = Path.Combine(PlanningCharacterizationTests.RepositoryRoot(), "test-data", "readme-parts",
            "readme-mixed.manifest.json");
        var job = DxfManifestLoader.Load(manifest).BuildNestJob(1);

        var result = new IrregularNestingEngine().Solve(job);

        Assert.Empty(NestLayoutCheck.Violations(job, result));
        var plate = Assert.Single(NestResultMaterializer.Materialize(job, result).Nest.Plates);
        Assert.Equal(130, plate.Parts.Count);
        var right = plate.Parts.Max(p => p.BoundingBox.Right);
        Assert.True(right < 104.5, $"Layout ends at {right:F3}");
    }
}
