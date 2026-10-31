using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.NestingEngines.Irregular;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;
using Irregular = OpenNest.Engine.NestingEngines.Irregular;

namespace OpenNest.Engine.Tests.NestingEngines;

// The best-fit pool is process-wide state and these tests swap the evaluator factory and the
// pool capacity, so they run alone.
[CollectionDefinition(nameof(BestFitPoolCollection), DisableParallelization = true)]
public class BestFitPoolCollection { }

/// <summary>
/// Best-fit candidates for job geometry are pooled by content, so a repeated or re-tuned job of
/// the same parts (fresh snapshots, fresh engine instances) does not search again and places
/// the parts exactly as the first job did.
/// </summary>
[Collection(nameof(BestFitPoolCollection))]
public class IrregularPairPoolTests
{
    private const double Spacing = 0.25;

    /// <summary>Counts best-fit searches of one geometry by wrapping the evaluator factory.</summary>
    private sealed class SearchSpy : IDisposable
    {
        private readonly Func<Drawing, double, IPairEvaluator>? previous;
        private readonly PartGeometrySnapshot geometry;
        private int count;

        public SearchSpy(OpenNest.CNC.Program program)
        {
            geometry = PartGeometrySnapshot.FromProgram(program);
            previous = BestFitCache.CreateEvaluator;
            BestFitCache.CreateEvaluator = (drawing, _) =>
            {
                if (PartGeometrySnapshot.ContentComparer.Equals(
                        PartGeometrySnapshot.FromProgram(drawing.Program), geometry))
                    Interlocked.Increment(ref count);
                return new PairEvaluator();
            };
        }

        public int Count => Volatile.Read(ref count);

        public void Dispose() => BestFitCache.CreateEvaluator = previous!;
    }

    private sealed class FailingEvaluator : IPairEvaluator
    {
        public List<BestFitResult> EvaluateAll(List<PairCandidate> candidates) =>
            throw new InvalidOperationException("search failed");
    }

    // Each test uses its own geometry so pooled sets from other tests cannot satisfy it.
    private static OpenNest.CNC.Program Wedge(double top) => Polyline((0, 0), (8.3, 0), (top, 20.7), (0, 20.7));

    private static (NestJob Job, IReadOnlyList<Irregular.PartType> Types) FreshJob(OpenNest.CNC.Program program, int quantity)
    {
        var job = Job(new[] { Part("wedge", program, quantity, RotationPolicy.Automatic) },
            new[] { Stock("sheet", 17.3, 31.7, spacing: Spacing) });
        return (job, PartCatalog.Build(job));
    }

    private static List<(double, double, double, double, double)> Poses(IReadOnlyDictionary<int, IReadOnlyList<PairPose>> pairs) =>
        pairs.SelectMany(kv => kv.Value.Select(p => (p.A.Rotation, p.B.Rotation, p.Dx, p.Dy, (double)kv.Key))).ToList();

    [Fact]
    public void PairCatalog_SecondJobOfTheSameGeometry_SkipsTheSearchAndOffersTheSamePairs()
    {
        BestFitCache.Clear();
        var program = Wedge(5.9);
        using var spy = new SearchSpy(program);

        var (first, firstTypes) = FreshJob(program, 2);
        var firstPairs = PairCatalog.Build(firstTypes, Spacing, 31.7, 17.3, CancellationToken.None);
        Assert.NotEmpty(firstPairs[0]);
        Assert.Equal(1, spy.Count);
        Assert.True(BestFitCache.IsShared(first.Parts[0].Geometry, Spacing));

        var (second, secondTypes) = FreshJob(program, 2);
        Assert.NotSame(first.Parts[0].Geometry, second.Parts[0].Geometry);
        var secondPairs = PairCatalog.Build(secondTypes, Spacing, 31.7, 17.3, CancellationToken.None);

        Assert.Equal(1, spy.Count);
        Assert.Equal(Poses(firstPairs), Poses(secondPairs));
    }

    private sealed class BlockedEvaluator(ManualResetEventSlim release) : IPairEvaluator
    {
        public List<BestFitResult> EvaluateAll(List<PairCandidate> candidates)
        {
            release.Wait(TimeSpan.FromSeconds(30));
            return new List<BestFitResult>();
        }
    }

    [Fact]
    public void PairCatalog_CancelingStopsTheWaitForASlowSearch()
    {
        // A time limit must not wait for the best-fit search, which can take minutes on one
        // CPU; the search itself finishes in the background.
        BestFitCache.Clear();
        var (_, types) = FreshJob(Wedge(6.3), 2);
        using var release = new ManualResetEventSlim();
        var previous = BestFitCache.CreateEvaluator;
        BestFitCache.CreateEvaluator = (_, _) => new BlockedEvaluator(release);
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var clock = System.Diagnostics.Stopwatch.StartNew();

            Assert.ThrowsAny<OperationCanceledException>(() =>
                PairCatalog.Build(types, Spacing, 31.7, 17.3, limit.Token));

            Assert.True(
                clock.Elapsed < TimeSpan.FromSeconds(5),
                $"waited {clock.Elapsed.TotalSeconds:0.0} s"
            );
        }
        finally
        {
            release.Set();
            BestFitCache.CreateEvaluator = previous!;
            BestFitCache.Clear();
        }
    }

    [Fact]
    public void IrregularEngine_FreshInstanceAndJob_PlacesIdenticallyWithoutSearchingAgain()
    {
        BestFitCache.Clear();
        var program = Wedge(6.1);
        using var spy = new SearchSpy(program);

        var (first, _) = FreshJob(program, 4);
        var firstResult = new IrregularNestingEngine().Solve(first);
        Assert.Equal(NestJobStatus.Complete, firstResult.Status);
        LayoutAssert.Valid(first, firstResult);
        Assert.Equal(1, spy.Count);

        var (second, _) = FreshJob(program, 4);
        var secondResult = new IrregularNestingEngine().Solve(second);

        Assert.Equal(1, spy.Count);
        Assert.Equal(Placements(firstResult), Placements(secondResult));
    }

    [Fact]
    public void PairCatalog_ReusesTheSearchOnlyWhenTheSpacingMatches()
    {
        BestFitCache.Clear();
        var program = Wedge(6.3);
        using var spy = new SearchSpy(program);

        var (job, types) = FreshJob(program, 2);
        PairCatalog.Build(types, Spacing, 31.7, 17.3, CancellationToken.None);
        PairCatalog.Build(types, Spacing, 31.7, 17.3, CancellationToken.None);
        Assert.Equal(1, spy.Count);
        Assert.False(BestFitCache.IsShared(job.Parts[0].Geometry, 0.5));

        PairCatalog.Build(types, 0.5, 31.7, 17.3, CancellationToken.None);
        Assert.Equal(2, spy.Count);
        Assert.True(BestFitCache.IsShared(job.Parts[0].Geometry, 0.5));
    }

    [Fact]
    public void SharedCapacity_DropsTheLeastRecentlyUsedGeometryFirst()
    {
        BestFitCache.Clear();
        var capacity = BestFitCache.SharedCapacity;
        try
        {
            BestFitCache.SharedCapacity = 2;
            var programs = new[] { Wedge(6.5), Wedge(6.7), Wedge(6.9) };
            var parts = programs.Select(p => Part("w", p, 2, RotationPolicy.Automatic)).ToArray();

            Compute(parts[0]);
            Compute(parts[1]);
            Compute(parts[0]); // The first becomes the most recently used.
            Compute(parts[2]);

            Assert.True(BestFitCache.IsShared(parts[0].Geometry, Spacing));
            Assert.False(BestFitCache.IsShared(parts[1].Geometry, Spacing));
            Assert.True(BestFitCache.IsShared(parts[2].Geometry, Spacing));

            using var spy = new SearchSpy(programs[1]);
            Compute(parts[1]);
            Assert.Equal(1, spy.Count);
        }
        finally
        {
            BestFitCache.SharedCapacity = capacity;
        }
    }

    [Fact]
    public void SharedCapacityZero_DisablesPoolingWithoutBreakingTheSearch()
    {
        BestFitCache.Clear();
        var capacity = BestFitCache.SharedCapacity;
        try
        {
            BestFitCache.SharedCapacity = 0;
            var program = Wedge(7.1);
            var part = Part("w", program, 2, RotationPolicy.Automatic);
            using var spy = new SearchSpy(program);
            Assert.NotEmpty(Compute(part));
            Assert.False(BestFitCache.IsShared(part.Geometry, Spacing));
            Assert.NotEmpty(Compute(part));
            Assert.Equal(2, spy.Count);
        }
        finally
        {
            BestFitCache.SharedCapacity = capacity;
        }
    }

    [Fact]
    public void Clear_DropsThePool()
    {
        BestFitCache.Clear();
        var program = Wedge(7.3);
        var part = Part("w", program, 2, RotationPolicy.Automatic);
        using var spy = new SearchSpy(program);
        Compute(part);
        Assert.True(BestFitCache.IsShared(part.Geometry, Spacing));

        BestFitCache.Clear();

        Assert.False(BestFitCache.IsShared(part.Geometry, Spacing));
        Compute(part);
        Assert.Equal(2, spy.Count);
    }

    [Fact]
    public void FailedSearch_IsNotPooled()
    {
        BestFitCache.Clear();
        var program = Wedge(7.5);
        var part = Part("w", program, 2, RotationPolicy.Automatic);
        var previous = BestFitCache.CreateEvaluator;
        try
        {
            BestFitCache.CreateEvaluator = (_, _) => new FailingEvaluator();
            Assert.Throws<InvalidOperationException>(() => Compute(part));
            Assert.False(BestFitCache.IsShared(part.Geometry, Spacing));
        }
        finally
        {
            BestFitCache.CreateEvaluator = previous!;
        }

        Assert.NotEmpty(Compute(part));
        Assert.True(BestFitCache.IsShared(part.Geometry, Spacing));
    }

    [Fact]
    public void ShareByGeometry_LapsesWhenTheProgramIsReplaced()
    {
        BestFitCache.Clear();
        var program = Wedge(7.7);
        var part = Part("w", program, 2, RotationPolicy.Automatic);
        using var spy = new SearchSpy(program);
        var drawing = DrawingJobMapper.CreateDrawing(part);
        BestFitCache.GetOrCompute(drawing, 31.7, 17.3, Spacing);
        Assert.Equal(1, spy.Count);

        drawing.Program = (OpenNest.CNC.Program)drawing.Program.Clone();
        BestFitCache.GetOrCompute(drawing, 31.7, 17.3, Spacing);

        // The drawing no longer vouches for the geometry, so it searches on its own.
        Assert.Equal(2, spy.Count);
    }

    [Fact]
    public void ContentComparer_MatchesExactMotionsOnly()
    {
        var comparer = PartGeometrySnapshot.ContentComparer;
        var a = PartGeometrySnapshot.FromProgram(Wedge(7.9));
        var b = PartGeometrySnapshot.FromProgram(Wedge(7.9));
        var c = PartGeometrySnapshot.FromProgram(Wedge(7.9 + 1e-12));

        Assert.NotSame(a, b);
        Assert.False(a.Equals(b));
        Assert.True(comparer.Equals(a, b));
        Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
        Assert.False(comparer.Equals(a, c));

        var zero = PartGeometrySnapshot.FromProgram(Polyline((0, 0), (5, 0), (5, 5)));
        var negativeZero = PartGeometrySnapshot.FromProgram(Polyline((-0.0, 0), (5, 0), (5, 5)));
        Assert.False(comparer.Equals(zero, negativeZero));

        var suppressed = Wedge(7.9);
        ((OpenNest.CNC.Motion)suppressed.Codes[1]).Suppressed = true;
        Assert.False(comparer.Equals(a, PartGeometrySnapshot.FromProgram(suppressed)));
    }

    private static List<BestFitResult> Compute(NestJobPart part)
    {
        var drawing = DrawingJobMapper.CreateDrawing(part);
        try
        {
            return BestFitCache.GetOrCompute(drawing, 31.7, 17.3, Spacing);
        }
        finally
        {
            BestFitCache.Invalidate(drawing);
        }
    }

    private static List<(string, double, double, double)> Placements(NestJobResult result) =>
        result.Plates.SelectMany(p => p.Placements.Select(q => (q.PartId, q.X, q.Y, q.Rotation))).ToList();
}
