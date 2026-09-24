using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Astra.Tests;

public class AstraNestingEngineTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MixedPartsRespectBoundsSpacingRotationAndIdentity(int quadrant)
    {
        var job = new NestJob(new[] {
            Rectangle("a", 4, 2, 12, RotationPolicy.Fixed(System.Math.PI / 2), 7, -3),
            Rectangle("b", 3, 3, 8, RotationPolicy.BoundedSweep(-System.Math.PI / 4, System.Math.PI / 2, System.Math.PI / 4))
        }, new[] { new NestPlateStock("stock", new Size(15, 20), 10, 0.25,
            new Spacing(1, 2, 3, 1), quadrant) });
        var before = job.Parts.Select(p => p.Geometry.Motions.ToArray()).ToArray();
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Validate(job, result);
        for (var i = 0; i < job.Parts.Count; i++) Assert.Equal(before[i], job.Parts[i].Geometry.Motions);
        var again = new AstraNestingEngine().Solve(job);
        Assert.Equal(result.Plates.SelectMany(p => p.Placements), again.Plates.SelectMany(p => p.Placements));
        Assert.Equal(result.Plates.Select(p => p.StockId), again.Plates.Select(p => p.StockId));
    }

    [Fact]
    public void ChoosesSmallestSheetWhenDemandFitsBoth()
    {
        var job = new NestJob(new[] { Rectangle("p", 2, 2, 1) }, new[] {
            new NestPlateStock("large", new Size(20, 20)), new NestPlateStock("small", new Size(2, 2)) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal("small", Assert.Single(result.Plates).StockId);
        Validate(job, result);
    }

    [Theory]
    [InlineData(1, null, NestJobStopReason.StockExhausted)]
    [InlineData(null, 1, NestJobStopReason.PlateLimitReached)]
    public void StopsAtInventoryOrPlateLimit(int? quantity, int? limit, NestJobStopReason reason)
    {
        var job = new NestJob(new[] { Rectangle("p", 2, 2, 3) },
            new[] { new NestPlateStock("s", new Size(2, 2), quantity) }, new NestJobOptions(maxPlates: limit));
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(reason, result.StopReason);
        Assert.Equal(2, Assert.Single(result.Fulfillment).Unplaced);
        Validate(job, result);
    }

    [Fact]
    public void ImpossibleAndEmptyJobsTerminate()
    {
        var part = Rectangle("p", 10, 10, 1);
        Assert.Equal(NestJobStopReason.NoPlacementFound, new AstraNestingEngine().Solve(
            new NestJob(new[] { part }, new[] { new NestPlateStock("s", new Size(2, 2)) })).StopReason);
        Assert.Equal(NestJobStopReason.StockExhausted, new AstraNestingEngine().Solve(
            new NestJob(new[] { part }, Array.Empty<NestPlateStock>())).StopReason);
        Assert.Equal(NestJobStatus.Complete, new AstraNestingEngine().Solve(
            new NestJob(Array.Empty<NestJobPart>(), Array.Empty<NestPlateStock>())).Status);
    }

    [Fact]
    public void CircleBoundsAreAnalyticAndIncrementalGeometryWorks()
    {
        var circle = new Program();
        circle.MoveTo(13, 10);
        circle.ArcTo(13, 10, 10, 10, RotationType.CCW);
        var incremental = new Program(Mode.Incremental);
        incremental.MoveTo(-5, -5);
        incremental.LineTo(2, 0);
        incremental.LineTo(0, 3);
        incremental.LineTo(-2, 0);
        incremental.LineTo(0, -3);
        var job = new NestJob(new[] {
            new NestJobPart("circle", PartGeometrySnapshot.FromProgram(circle), 5),
            new NestJobPart("incremental", PartGeometrySnapshot.FromProgram(incremental), 5)
        }, new[] { new NestPlateStock("s", new Size(20, 20), partSpacing: 0.4) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Validate(job, result);
    }

    [Fact]
    public void CancellationBeforeAndDuringSolveThrows()
    {
        var job = new NestJob(new[] { Rectangle("p", 2, 2, 10) },
            new[] { new NestPlateStock("s", new Size(10, 10)) });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new AstraNestingEngine().Solve(job, token: cts.Token));
        using var during = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new AstraNestingEngine().Solve(job,
            new CallbackProgress(_ => during.Cancel()), during.Token));
    }

    [Fact]
    public void PriorityWinsScarceSpaceAndProgressReflectsCommits()
    {
        var low = Rectangle("low", 2, 2, 1);
        var high = new NestJobPart("high", low.Geometry, 1, priority: 9);
        var job = new NestJob(new[] { low, high }, new[] { new NestPlateStock("s", new Size(2, 2), 1) });
        var updates = new List<NestJobProgress>();
        var result = new AstraNestingEngine().Solve(job, new CallbackProgress(updates.Add));
        Assert.Equal("high", Assert.Single(Assert.Single(result.Plates).Placements).PartId);
        Assert.Equal(NestJobStage.PlateCommitted, updates.Last().Stage);
        Assert.Equal(1, updates.Last().CommittedParts);
    }

    [Fact]
    public void ConcaveAndHoledPartsRemainValid()
    {
        var l = new Program();
        l.MoveTo(0, 0); l.LineTo(6, 0); l.LineTo(6, 2);
        l.LineTo(2, 2); l.LineTo(2, 6); l.LineTo(0, 6); l.LineTo(0, 0);
        var holed = DrawingJobMapper.ToProgram(Rectangle("template", 8, 8, 1).Geometry);
        holed.MoveTo(2, 2); holed.LineTo(2, 6); holed.LineTo(6, 6);
        holed.LineTo(6, 2); holed.LineTo(2, 2);
        var job = new NestJob(new[] {
            new NestJobPart("concave", PartGeometrySnapshot.FromProgram(l), 7),
            new NestJobPart("hole", PartGeometrySnapshot.FromProgram(holed), 3)
        }, new[] { new NestPlateStock("s", new Size(20, 30), partSpacing: 0.2) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Validate(job, result);
    }

    [Fact]
    public void SeededMixedRectanglesPassBenchmarkValidator()
    {
        var random = new Random(7301);
        for (var trial = 0; trial < 12; trial++)
        {
            var parts = Enumerable.Range(0, 6).Select(i => Rectangle($"p{i}",
                random.Next(1, 9), random.Next(1, 9), random.Next(1, 6),
                i % 2 == 0 ? RotationPolicy.Automatic : RotationPolicy.Fixed(0))).ToArray();
            var job = new NestJob(parts, new[] {
                new NestPlateStock("small", new Size(15, 20), 1, 0.1),
                new NestPlateStock("large", new Size(25, 30), partSpacing: 0.3)
            });
            var result = new AstraNestingEngine().Solve(job);
            Assert.Equal(NestJobStatus.Complete, result.Status);
            Validate(job, result);
        }
    }

    [Fact]
    public void ComplementaryTrianglesShareOneEnvelope()
    {
        var triangle = new Program(); triangle.MoveTo(0, 0); triangle.LineTo(10, 0);
        triangle.LineTo(0, 10); triangle.LineTo(0, 0);
        var job = new NestJob(new[] { new NestJobPart("t", PartGeometrySnapshot.FromProgram(triangle), 2) },
            new[] { new NestPlateStock("s", new Size(10, 10), 1) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Single(result.Plates);
        Validate(job, result);
    }

    [Fact]
    public void PlacesInsertInsideFrameHole()
    {
        var frame = DrawingJobMapper.ToProgram(Rectangle("template", 10, 10, 1).Geometry);
        frame.MoveTo(1, 1); frame.LineTo(1, 9); frame.LineTo(9, 9);
        frame.LineTo(9, 1); frame.LineTo(1, 1);
        var job = new NestJob(new[] { new NestJobPart("frame", PartGeometrySnapshot.FromProgram(frame), 1),
            Rectangle("insert", 7, 7, 1) }, new[] { new NestPlateStock("s", new Size(10, 10), 1, 0.25) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Single(result.Plates);
        Validate(job, result);
    }

    [Fact]
    public void PlateLimitSelectsSheetThatCompletesDemand()
    {
        var job = new NestJob(new[] { Rectangle("p", 5, 5, 10) }, new[] {
            new NestPlateStock("small", new Size(10, 10)), new NestPlateStock("large", new Size(20, 20))
        }, new NestJobOptions(maxPlates: 1));
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal("large", Assert.Single(result.Plates).StockId);
        Validate(job, result);
    }

    [Fact]
    public void AutomaticDiagonalIsRetainedWhenItIsTheOnlyStockFit()
    {
        var job = new NestJob(new[] { Rectangle("diagonal", 10, 1, 1, RotationPolicy.Automatic) },
            new[] { new NestPlateStock("s", new Size(8, 8), 1) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Validate(job, result);
    }

    [Fact]
    public void DenseTrianglesRespectPositiveSpacing()
    {
        var triangle = new Program(); triangle.MoveTo(0, 0); triangle.LineTo(10, 0);
        triangle.LineTo(0, 10); triangle.LineTo(0, 0);
        var job = new NestJob(new[] { new NestJobPart("t", PartGeometrySnapshot.FromProgram(triangle), 20) },
            new[] { new NestPlateStock("s", new Size(24, 48), partSpacing: 0.15) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Validate(job, result);
    }

    [Fact]
    public void LargeBatchDoesNotLoseEfficientLargeSheetPlans()
    {
        var job = new NestJob(new[] { Rectangle("p", 6, 4, 100, RotationPolicy.Automatic) }, new[] {
            new NestPlateStock("small", new Size(8, 12), partSpacing: 0.1),
            new NestPlateStock("large", new Size(20, 30), partSpacing: 0.1) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.True(result.Plates.Sum(p => p.Stock.Size.Length * p.Stock.Size.Width) <= 3600);
        Validate(job, result);
    }

    [Fact]
    public void ExactPositiveSpacingRectangleGridStillFits()
    {
        var job = new NestJob(new[] { Rectangle("p", 2, 2, 4) },
            new[] { new NestPlateStock("s", new Size(4.25, 4.25), 1, 0.25) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Validate(job, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    public void MixedRotatedContoursPassIndependentValidation(double spacing)
    {
        for (var trial = 0; trial < 5; trial++)
        {
            var t = new Program(); t.MoveTo(0, 0); t.LineTo(4 + trial, 0);
            t.LineTo(1, 3 + trial); t.LineTo(0, 0);
            var job = new NestJob(new[] {
                new NestJobPart("t", PartGeometrySnapshot.FromProgram(t), 7),
                Rectangle("r", 3, 2, 5, RotationPolicy.Fixed(trial * System.Math.PI / 7))
            }, new[] { new NestPlateStock("s", new Size(20, 25), partSpacing: spacing) });
            var result = new AstraNestingEngine().Solve(job);
            Assert.Equal(NestJobStatus.Complete, result.Status);
            Validate(job, result);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CurvedCutoutsAcceptInsertsWhenOnlyOneRingFitsTheStock(int quadrant)
    {
        var ring = new OpenNest.Shapes.RingShape { OuterDiameter = 10, InnerDiameter = 7 }.GetDrawing();
        var insert = new OpenNest.Shapes.CircleShape { Diameter = 6 }.GetDrawing();
        var job = new NestJob(new[] {
            new NestJobPart("ring", PartGeometrySnapshot.FromProgram(ring.Program), 1),
            new NestJobPart("insert", PartGeometrySnapshot.FromProgram(insert.Program), 1)
        }, new[] { new NestPlateStock("s", new Size(10.8, 10.6), 1, partSpacing: 0.175,
            edgeSpacing: new Spacing(0.2, 0.3, 0.4, 0.5), quadrant: quadrant) });
        var result = new AstraNestingEngine().Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, Assert.Single(result.Plates).Placements.Count);
        Validate(job, result);
    }

    [Fact]
    public void CurvedConcavityInterlocksWithoutFineMeshMinkowskiExplosion()
    {
        var c = new Program();
        c.MoveTo(6, 0); c.ArcTo(0, -6, 0, 0, RotationType.CCW);
        c.LineTo(0, -4); c.ArcTo(4, 0, 0, 0, RotationType.CW); c.LineTo(6, 0);
        var job = new NestJob(new[] { new NestJobPart("C", PartGeometrySnapshot.FromProgram(c), 8) },
            new[] { new NestPlateStock("s", new Size(25, 43), 1, 0.25,
                new Spacing(0.2, 0.3, 0.4, 0.5), 3) });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = new AstraNestingEngine().Solve(job, token: cancellation.Token);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Single(result.Plates);
        Validate(job, result);
    }

    private sealed class CallbackProgress(Action<NestJobProgress> callback) : IProgress<NestJobProgress>
    { public void Report(NestJobProgress value) => callback(value); }

    private static NestJobPart Rectangle(string id, double w, double h, int count,
        RotationPolicy? rotation = null, double x = 0, double y = 0)
    {
        var p = new Program();
        p.MoveTo(x, y); p.LineTo(x + w, y); p.LineTo(x + w, y + h);
        p.LineTo(x, y + h); p.LineTo(x, y);
        return new(id, PartGeometrySnapshot.FromProgram(p), count, rotation: rotation ?? RotationPolicy.Fixed(0));
    }

    private static void Validate(NestJob job, NestJobResult result)
    {
        var materialized = NestResultMaterializer.Materialize(job, result);
        var requirements = job.Parts.ToDictionary(p => materialized.DrawingsByPartId[p.Id],
            p => (Name: p.Id, Quantity: p.Quantity));
        var validation = OpenNest.Benchmark.NestValidator.Validate(
            materialized.Nest.Plates.Select(p => (p, p.Parts.ToList())).ToList(), requirements);
        OpenNest.Benchmark.NestValidator.ValidateAgainstJob(job, result,
            job.Parts.ToDictionary(p => p.Id, p => p.Id), validation);
        Assert.True(validation.Valid, string.Join("; ", validation.Violations));
        foreach (var sheet in result.Plates)
        {
            var s = sheet.Stock;
            var left = (s.Quadrant is 1 or 4 ? 0 : -s.Size.Length) + s.EdgeSpacing.Left;
            var bottom = (s.Quadrant is 1 or 2 ? 0 : -s.Size.Width) + s.EdgeSpacing.Bottom;
            var right = left + s.Size.Length - s.EdgeSpacing.Left - s.EdgeSpacing.Right;
            var top = bottom + s.Size.Width - s.EdgeSpacing.Bottom - s.EdgeSpacing.Top;
            foreach (var pose in sheet.Placements)
            {
                var part = job.Parts.Single(p => p.Id == pose.PartId);
                Assert.True(part.Rotation.Allows(pose.Rotation));
                var geometry = ConvertProgram.ToGeometry(DrawingJobMapper.ToProgram(part.Geometry))
                    .Where(e => !ReferenceEquals(e.Layer, SpecialLayers.Rapid)).ToArray();
                foreach (var entity in geometry) { entity.Rotate(pose.Rotation); entity.Offset(pose.X, pose.Y); }
                var b = (L: geometry.Min(e => e.Left), B: geometry.Min(e => e.Bottom),
                    R: geometry.Max(e => e.Right), T: geometry.Max(e => e.Top));
                Assert.True(b.L >= left - 1e-7 && b.B >= bottom - 1e-7 && b.R <= right + 1e-7 && b.T <= top + 1e-7);
            }
        }
        foreach (var part in job.Parts)
        {
            var placed = result.Plates.SelectMany(s => s.Placements).Where(p => p.PartId == part.Id).ToArray();
            Assert.Equal(Enumerable.Range(0, placed.Length), placed.Select(p => p.InstanceIndex).Order());
            var fulfillment = result.Fulfillment.Single(f => f.PartId == part.Id);
            Assert.Equal(placed.Length, fulfillment.Placed);
            Assert.Equal(part.Quantity, fulfillment.Placed + fulfillment.Unplaced);
        }
        foreach (var usage in result.StockUsage)
        {
            var stock = job.Plates.Single(s => s.Id == usage.StockId);
            Assert.Equal(result.Plates.Count(s => s.StockId == stock.Id), usage.Used);
            Assert.Equal(stock.Quantity - usage.Used, usage.Remaining);
            Assert.True(usage.Remaining is null or >= 0);
        }
    }
}
