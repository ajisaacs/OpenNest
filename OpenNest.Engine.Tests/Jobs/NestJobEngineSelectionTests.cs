using OpenNest.Geometry;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Strategy selection is instance-scoped: explicit engine choices complete independently,
/// and unknown strategies are rejected.
/// </summary>
public class NestJobEngineSelectionTests
{
    [Fact]
    public void ExplicitDefaultAndStripSelectionsCompleteIndependently()
    {
        var job = FiniteStockJobTests.Job(1);

        var defaultResult = new NestJobRunner(PlateNesterFactory.Create).Solve(job);
        Assert.Equal(NestJobStatus.Complete, defaultResult.Status);

        var stripResult = new NestJobRunner(PlateNesterFactory.Create).Solve(
            new NestJob(job.Parts, job.Plates, new NestJobOptions("Strip"))
        );
        Assert.Equal(NestJobStatus.Complete, stripResult.Status);
    }

    [Fact]
    public void FactoryResolvesEachNamedBuiltInStrategy()
    {
        var defaultNester = PlateNesterFactory.Create("Default");
        var stripNester = PlateNesterFactory.Create("Strip");
        var verticalNester = PlateNesterFactory.Create("Vertical Remnant");
        var horizontalNester = PlateNesterFactory.Create("Horizontal Remnant");

        Assert.NotNull(defaultNester);
        Assert.NotNull(stripNester);
        Assert.NotNull(verticalNester);
        Assert.NotNull(horizontalNester);
        Assert.NotSame(defaultNester, stripNester);
    }

    [Fact]
    public void UnknownStrategyIsRejected()
    {
        Assert.Throws<NotSupportedException>(() => PlateNesterFactory.Create("Not A Real Engine"));
        var job = FiniteStockJobTests.Job(1);
        Assert.Throws<NotSupportedException>(() =>
            new NestJobRunner(key =>
                throw new NotSupportedException($"Unknown placement strategy: {key}")
            ).Solve(new NestJob(job.Parts, job.Plates, new NestJobOptions("Bogus")))
        );
    }

    [Fact]
    public void StripEngineEndToEndPlacesAndAccounts()
    {
        var drawing = new Drawing("strip part", TestDrawingFactory.Rectangle(30, 30));
        var job = new NestJob(
            new[] { DrawingJobMapper.FromDrawing("part", drawing, 2) },
            new[] { new NestPlateStock("s", new Size(90, 90), 1) }
        );
        var result = new NestJobRunner(PlateNesterFactory.Create).Solve(
            new NestJob(job.Parts, job.Plates, new NestJobOptions("Strip"))
        );

        Assert.True(result.Plates.SelectMany(p => p.Placements).Count() >= 1);
        foreach (var f in result.Fulfillment)
            Assert.Equal(f.Requested, f.Placed + f.Unplaced);
    }
}
