using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestJobResultBuilderTests
{
    [Fact]
    public void CommitsAssignIndicesAccountingAndCumulativeProgress()
    {
        var job = Job();
        var progress = new Reports();
        var builder = new NestJobResultBuilder(job, progress);
        var finite = job.Plates[0];
        var unlimited = job.Plates[1];

        Assert.False(builder.IsComplete);
        Assert.Equal(0, builder.Placed("a"));
        Assert.Equal(0, builder.SheetsUsed(unlimited));
        Assert.Equal(0, builder.AddSheet(unlimited, new[] { ("b", 1.0, 2.0, 0.5), ("a", 3.0, 4.0, 1.0) }));
        var snapshot = builder.Build(NestJobStopReason.NoPlacementFound);
        Assert.Equal(1, builder.AddSheet(finite, new[] { ("a", 5.0, 6.0, 1.5), ("b", 7.0, 8.0, 2.0) }));
        Assert.Equal(2, builder.AddSheet(unlimited, new[] { ("a", 9.0, 10.0, 2.5) }));
        var result = builder.Build(NestJobStopReason.StockExhausted);

        Assert.True(builder.IsComplete);
        Assert.Equal(3, builder.Placed("a"));
        Assert.Equal(2, builder.Placed("b"));
        Assert.Equal(2, builder.SheetsUsed(unlimited));
        Assert.Equal(1, builder.SheetsUsed(finite));
        Assert.Equal(new[] { 0, 1, 2 }, result.Plates.Select(p => p.PlateIndex));
        Assert.Equal(new[] { "unlimited", "finite", "unlimited" }, result.Plates.Select(p => p.StockId));
        var poses = result.Plates.SelectMany(p => p.Placements).ToArray();
        Assert.Equal(new[] { 0, 1, 2 }, poses.Where(p => p.PartId == "a").Select(p => p.InstanceIndex));
        Assert.Equal(new[] { 0, 1 }, poses.Where(p => p.PartId == "b").Select(p => p.InstanceIndex));
        Assert.Equal(new NestJobPlacement("b", 0, 1, 2, 0.5), poses[0]);
        Assert.Equal(new NestJobPlacement("a", 2, 9, 10, 2.5), poses[^1]);
        Assert.Equal(new[] { new PartFulfillment("a", 3, 3, 0), new PartFulfillment("b", 2, 2, 0) }, result.Fulfillment);
        Assert.All(result.Fulfillment, f => Assert.Equal(f.Requested, f.Placed + f.Unplaced));
        Assert.Equal(new[] { new StockUsage("finite", 1, 1), new StockUsage("unlimited", 2, null) }, result.StockUsage);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(NestJobStopReason.Completed, result.StopReason);
        Assert.Equal(new[]
        {
            new NestJobProgress(NestJobStage.PlateCommitted, "unlimited", 0, 1, 2),
            new NestJobProgress(NestJobStage.PlateCommitted, "finite", 1, 2, 4),
            new NestJobProgress(NestJobStage.PlateCommitted, "unlimited", 2, 3, 5),
        }, progress.Values);
        Assert.Single(snapshot.Plates);
        Assert.Equal(new PartFulfillment("a", 3, 1, 2), snapshot.Fulfillment[0]);
        Assert.Equal(new StockUsage("unlimited", 1, null), snapshot.StockUsage[1]);
    }

    [Theory]
    [InlineData(NestJobStopReason.StockExhausted)]
    [InlineData(NestJobStopReason.NoPlacementFound)]
    [InlineData(NestJobStopReason.PlateLimitReached)]
    public void IncompleteBuildPreservesStopReason(NestJobStopReason reason)
    {
        var result = new NestJobResultBuilder(Job()).Build(reason);

        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(reason, result.StopReason);
        Assert.Empty(result.Plates);
        Assert.Equal(new PartFulfillment("a", 3, 0, 3), result.Fulfillment[0]);
        Assert.Equal(new StockUsage("finite", 0, 2), result.StockUsage[0]);
        Assert.Equal(new StockUsage("unlimited", 0, null), result.StockUsage[1]);
    }

    [Fact]
    public void OverproductionRejectsWholeSheetWithoutConsumingIndicesOrReportingProgress()
    {
        var job = Job();
        var progress = new Reports();
        var builder = new NestJobResultBuilder(job, progress);
        var stock = job.Plates[0];
        builder.AddSheet(stock, new[] { ("b", 0.0, 0.0, 0.0) });

        Assert.Throws<InvalidOperationException>(() => builder.AddSheet(stock,
            new[] { ("a", 0.0, 0.0, 0.0), ("b", 0.0, 0.0, 0.0), ("b", 0.0, 0.0, 0.0) }));

        Assert.Equal(0, builder.Placed("a"));
        Assert.Equal(1, builder.Placed("b"));
        Assert.Equal(1, builder.SheetsUsed(stock));
        Assert.Single(progress.Values);
        Assert.Equal(1, builder.AddSheet(stock, new[] { ("b", 0.0, 0.0, 0.0) }));
        var result = builder.Build(NestJobStopReason.StockExhausted);
        Assert.Equal(1, Assert.Single(result.Plates[1].Placements).InstanceIndex);
        Assert.Equal(new StockUsage("finite", 2, 0), result.StockUsage[0]);
        Assert.Throws<InvalidOperationException>(() => builder.AddSheet(stock, new[] { ("a", 0.0, 0.0, 0.0) }));
    }

    [Fact]
    public void UnknownPartOrForeignStockCannotChangeAccounting()
    {
        var job = Job();
        var builder = new NestJobResultBuilder(job);
        Assert.Throws<ArgumentException>(() => builder.AddSheet(job.Plates[0], new[] { ("unknown", 0.0, 0.0, 0.0) }));
        Assert.Throws<ArgumentException>(() => builder.AddSheet(new NestPlateStock("finite", new Size(10, 10)),
            new[] { ("a", 0.0, 0.0, 0.0) }));
        Assert.Empty(builder.Build(NestJobStopReason.NoPlacementFound).Plates);
        Assert.Equal(0, builder.SheetsUsed(job.Plates[0]));
    }

    [Fact]
    public void EmptyDemandIsComplete()
    {
        var builder = new NestJobResultBuilder(new NestJob(Array.Empty<NestJobPart>(), Array.Empty<NestPlateStock>()));
        Assert.True(builder.IsComplete);
        var result = builder.Build(NestJobStopReason.NoPlacementFound);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(NestJobStopReason.Completed, result.StopReason);
    }

    private static NestJob Job()
    {
        var geometry = PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(1, 1));
        return new NestJob(
            new[] { new NestJobPart("a", geometry, 3), new NestJobPart("b", geometry, 2) },
            new[] { new NestPlateStock("finite", new Size(10, 10), 2), new NestPlateStock("unlimited", new Size(10, 10)) }
        );
    }

    private sealed class Reports : IProgress<NestJobProgress>
    {
        public List<NestJobProgress> Values { get; } = new();
        public void Report(NestJobProgress value) => Values.Add(value);
    }
}
