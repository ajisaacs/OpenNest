using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// A committed live plate must carry exactly the clamp keep-outs its layout was solved and
/// validated against, in plate coordinates, including sheets the commit had to create.
/// </summary>
public class ClampZoneCommitTests
{
    // A 1.5 x 0.625 clamp on the front edge of a 48 x 96 sheet (Length 96 runs along X).
    private const double ZoneX = 5.25;

    private static ClampZone Zone() => new("clamp-1", new Box(ZoneX, 0, 1.5, 0.625));

    /// <summary>Two different one-part sheets so repeat grouping cannot merge them.</summary>
    private sealed class TwoSheetStub : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            var stock = job.Plates[0];
            var work = stock.WorkArea;
            var builder = new NestJobResultBuilder(job);
            builder.AddSheet(stock, new[] { (job.Parts[0].Id, work.Left + 20, work.Bottom + 10, 0.0) });
            builder.AddSheet(stock, new[] { (job.Parts[0].Id, work.Left + 40, work.Bottom + 20, 0.0) });
            return builder.Build(NestJobStopReason.Completed);
        }
    }

    private static NestPipelineResult Result(Drawing drawing, int quadrant, params ClampZone[] zones)
    {
        var result = NestPipeline.Run(new TwoSheetStub(), "stub", new NestPipelineRequest("stub",
            new[] { new NestItem { Drawing = drawing, Quantity = 2 } },
            new[]
            {
                new NestPlateStock("sheet", new Size(48, 96), null, 0.25, new Spacing(1, 1, 1, 1),
                    quadrant, cost: null, clampZones: zones),
            }));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(2, result.Plates.Count);
        return result;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryCommittedPlateCarriesTheSolvedZonesInPlateCoordinates(int quadrant)
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        nest.CreatePlate(); // empty template without zones; the second sheet is created by commit
        using var manager = new PlateManager(nest);

        var applied = NestPipelineCommit.ApplyToEmptyPlates(Result(drawing, quadrant, Zone()), manager);

        Assert.Equal(2, applied.Count);
        Assert.All(applied, plate =>
        {
            var zone = Assert.Single(plate.ClampZones);
            Assert.Equal("clamp-1", zone.Id);
            // Exact bits: the inverse of the stock's placement-frame translation.
            Assert.Equal(ZoneX, zone.Bounds.X);
            Assert.Equal(0, zone.Bounds.Y);
            Assert.Equal(1.5, zone.Bounds.Length);
            Assert.Equal(0.625, zone.Bounds.Width);
        });
    }

    [Fact]
    public void MatchingPlateZonesAreKeptWithoutChangeEvents()
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var template = nest.CreatePlate();
        template.Quadrant = 3;
        template.Size = new Size(48, 96);
        var original = Zone();
        template.ClampZones.Add(original);
        var changes = 0;
        template.ClampZonesChanged += (_, _) => changes++;
        using var manager = new PlateManager(nest);

        var applied = NestPipelineCommit.ApplyToEmptyPlates(Result(drawing, 3, Zone()), manager);

        Assert.Same(template, applied[0]);
        Assert.Same(original, Assert.Single(template.ClampZones));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ZonesTheLayoutWasNotSolvedWithAreRemoved()
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var template = nest.CreatePlate();
        template.ClampZones.Add(Zone());
        using var manager = new PlateManager(nest);

        var applied = NestPipelineCommit.ApplyToEmptyPlates(Result(drawing, 1), manager);

        Assert.All(applied, plate => Assert.Empty(plate.ClampZones));
    }
}
