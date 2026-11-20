using OpenNest.Benchmark;
using OpenNest.Clamps;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Geometry;
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

    /// <summary>
    /// Lattice's first pattern for 24 gussets on this sheet (59" of height to fill) sets two
    /// columns closer than the part spacing, so the band's certification rejects it. The band
    /// search used to stop there, and the gussets, left without a band, filled the gaps as ragged
    /// columns: the layout ended at 39.6". It now asks again just short of the rejected pattern
    /// and bands them in 18.2", ending the layout at 39.1".
    /// </summary>
    [Fact]
    public void GroupBandsWhenItsFirstPatternIsRejected()
    {
        var job = SampleJob(new NestPlateStock("sheet", new Size(60, 120), null, 0.3,
            new Spacing(0.3, 0.75, 0.3, 0.25)), "Gusset", "Base Plate");

        var result = new IrregularNestingEngine().Solve(job);

        Assert.Empty(NestLayoutCheck.Violations(job, result));
        var plate = Assert.Single(NestResultMaterializer.Materialize(job, result).Nest.Plates);
        Assert.Equal(38, plate.Parts.Count);
        var right = plate.Parts.Max(p => p.BoundingBox.Right);
        Assert.True(right < 39.3, $"Layout ends at {right:F3}");
    }

    /// <summary>
    /// All seven parts on the desktop's sheet: a wider bottom edge and a clamp rail along it.
    /// Irregular ended at 106.6": the gussets had no band, building the lower bands spent the work
    /// budget before the narrower bands of the last groups in the order had their trial, and the
    /// hex covers stood in a band taller than another just as narrow, leaving no strip above them
    /// for the links held back. It now ends at 104.0". (A saved nest of these parts, whose
    /// drawings start their contours elsewhere, ends at 103.1", as a hand layout of it does.)
    /// </summary>
    [Fact]
    public void MixedSamplePartsBandAboveAClampRail()
    {
        var clamps = Enumerable.Range(1, 6)
            .Select(i => new ClampZone($"clamp-{i}", new Box(i * 20 - 0.75, 0, 1.5, 0.625))).ToList();
        var job = SampleJob(new NestPlateStock("sheet", new Size(60, 120), null, 0.3,
            new Spacing(0.3, 0.75, 0.3, 0.25), 1, clamps));

        var result = new IrregularNestingEngine().Solve(job);

        Assert.Empty(NestLayoutCheck.Violations(job, result));
        var plate = Assert.Single(NestResultMaterializer.Materialize(job, result).Nest.Plates);
        Assert.Equal(130, plate.Parts.Count);
        var right = plate.Parts.Max(p => p.BoundingBox.Right);
        Assert.True(right < 104.5, $"Layout ends at {right:F3}");
    }

    /// <summary>The sample parts (all of them, or those named) at their manifest quantities on one sheet.</summary>
    private static NestJob SampleJob(NestPlateStock stock, params string[] names)
    {
        var manifest = Path.Combine(PlanningCharacterizationTests.RepositoryRoot(), "test-data", "readme-parts",
            "readme-mixed.manifest.json");
        var parts = DxfManifestLoader.Load(manifest).Requests
            .Where(r => names.Length == 0 || names.Contains(r.Drawing.Name))
            .Select(r => DrawingJobMapper.FromDrawing(r.Drawing.Id.ToString(), r.Drawing, r.Quantity));
        return new NestJob(parts, new[] { stock }, new NestJobOptions(maxPlates: 1));
    }
}
