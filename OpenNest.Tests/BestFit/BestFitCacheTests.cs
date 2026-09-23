using System.Runtime.CompilerServices;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Shapes;

namespace OpenNest.Tests.BestFit;

// BestFitCache is process-wide static state and these tests swap its evaluator factory,
// so they must not run alongside other tests.
[CollectionDefinition(nameof(FillCacheCollection), DisableParallelization = true)]
public class FillCacheCollection { }

[Collection(nameof(FillCacheCollection))]
public class BestFitCacheTests
{
    private const double Spacing = 0.25;

    private static Drawing MakeRotatedTShape()
    {
        var drawing = new TShape { Width = 10, Height = 8 }.GetDrawing();
        drawing.Program.Rotate(Angle.ToRadians(30), drawing.Program.BoundingBox().Center);
        drawing.RecomputeCanonicalAngle();
        return drawing;
    }

    /// <summary>
    /// Counts best-fit computations for one source drawing by wrapping the evaluator factory.
    /// </summary>
    private sealed class EvaluatorSpy : IDisposable
    {
        private readonly Func<Drawing, double, IPairEvaluator> previous;
        private readonly WeakReference<Drawing> source;
        private int count;

        public EvaluatorSpy(Drawing source)
        {
            this.source = new WeakReference<Drawing>(source);
            previous = BestFitCache.CreateEvaluator;
            BestFitCache.CreateEvaluator = (drawing, spacing) =>
            {
                if (this.source.TryGetTarget(out var target)
                    && ReferenceEquals(CanonicalFrame.SourceOf(drawing), target))
                    Interlocked.Increment(ref count);
                return new PairEvaluator();
            };
        }

        public int Count => Volatile.Read(ref count);

        public void Dispose() => BestFitCache.CreateEvaluator = previous;
    }

    [Fact]
    public void GetOrCompute_CanonicalCopiesOfOneDrawing_ShareOneComputation()
    {
        var drawing = MakeRotatedTShape();
        using var spy = new EvaluatorSpy(drawing);

        var first = BestFitCache.GetOrCompute(CanonicalFrame.AsCanonicalCopy(drawing), 60, 40, Spacing);
        var copyOfCopy = CanonicalFrame.AsCanonicalCopy(CanonicalFrame.AsCanonicalCopy(drawing));
        var second = BestFitCache.GetOrCompute(copyOfCopy, 60, 40, Spacing);
        var third = BestFitCache.GetOrCompute(drawing, 60, 40, Spacing);

        Assert.Same(first, second);
        Assert.Same(first, third);
        Assert.Equal(1, spy.Count);
    }

    [Fact]
    public void GetOrCompute_TwoPlateSizes_RunsFinderOnceAndMatchesPerSizeFinder()
    {
        var drawing = MakeRotatedTShape();
        using var spy = new EvaluatorSpy(drawing);

        var sizes = new[] { (Width: 60.0, Height: 40.0), (Width: 14.0, Height: 30.0) };
        var cached = sizes
            .Select(s => BestFitCache.GetOrCompute(drawing, s.Width, s.Height, Spacing))
            .ToList();

        Assert.Equal(1, spy.Count);

        var canonical = CanonicalFrame.AsCanonicalCopy(drawing);
        for (var i = 0; i < sizes.Length; i++)
        {
            var expected = new BestFitFinder(sizes[i].Width, sizes[i].Height)
                .FindBestFits(canonical, Spacing, 0.25);

            Assert.Equal(Signature(expected), Signature(cached[i]));
        }

        // The small plate must actually reject something the large one keeps.
        Assert.True(cached[1].Count(r => r.Keep) < cached[0].Count(r => r.Keep));
    }

    [Fact]
    public void GetOrCompute_ReplacingProgram_InvalidatesEntry()
    {
        var drawing = MakeRotatedTShape();
        using var spy = new EvaluatorSpy(drawing);

        var before = BestFitCache.GetOrCompute(drawing, 60, 40, Spacing);
        drawing.Program = (OpenNest.CNC.Program)drawing.Program.Clone();
        var after = BestFitCache.GetOrCompute(drawing, 60, 40, Spacing);

        Assert.NotSame(before, after);
        Assert.Equal(2, spy.Count);
    }

    [Fact]
    public void GetOrCompute_ReleasedDrawing_IsCollectable()
    {
        var weak = ComputeForTransientDrawing();

        for (var i = 0; i < 3 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ComputeForTransientDrawing()
    {
        var drawing = MakeRotatedTShape();
        var canonical = CanonicalFrame.AsCanonicalCopy(drawing);
        BestFitCache.GetOrCompute(canonical, 60, 40, Spacing);
        FillResultCache.Store(canonical, new Box(0, 0, 60, 40), Spacing, new List<Part> { new Part(canonical) });
        return new WeakReference(drawing);
    }

    [Fact]
    public void Populate_FeedsGetOrComputeAndRoundTripsThroughGetAllForDrawing()
    {
        var drawing = MakeRotatedTShape();
        using var spy = new EvaluatorSpy(drawing);
        var results = new List<BestFitResult>
        {
            new BestFitResult { Candidate = new PairCandidate { Drawing = drawing }, Keep = true },
        };

        BestFitCache.Populate(drawing, 60, 40, Spacing, results);

        Assert.Same(results, BestFitCache.GetOrCompute(CanonicalFrame.AsCanonicalCopy(drawing), 60, 40, Spacing));
        Assert.Equal(0, spy.Count);

        var all = BestFitCache.GetAllForDrawing(drawing);
        Assert.Single(all);
        Assert.Same(results, all[(60, 40, Spacing)]);
    }

    [Fact]
    public void FillResultCache_HitThroughSecondCanonicalCopy_RebindsToSameParts()
    {
        var drawing = MakeRotatedTShape();
        var box = new Box(5, 7, 80, 50);

        var firstCopy = CanonicalFrame.AsCanonicalCopy(drawing);
        var computed = new FillLinear(box, Spacing).Fill(firstCopy, 0, NestDirection.Horizontal);
        Assert.NotEmpty(computed);
        FillResultCache.Store(firstCopy, box, Spacing, computed);

        var secondCopy = CanonicalFrame.AsCanonicalCopy(drawing);
        var hit = FillResultCache.Get(secondCopy, box, Spacing);
        Assert.NotNull(hit);

        // A non-canonical caller must not be served canonical-frame parts.
        Assert.Null(FillResultCache.Get(drawing, box, Spacing));

        var expected = CanonicalFrame.RebindToOriginal(computed.Select(p => (Part)p.Clone()).ToList(), drawing);
        var actual = CanonicalFrame.RebindToOriginal(hit, drawing);

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Same(drawing, actual[i].BaseDrawing);
            Assert.Equal(expected[i].Rotation, actual[i].Rotation, 9);
            Assert.Equal(expected[i].BoundingBox.X, actual[i].BoundingBox.X, 6);
            Assert.Equal(expected[i].BoundingBox.Y, actual[i].BoundingBox.Y, 6);
        }
    }

    private static List<string> Signature(List<BestFitResult> results) =>
        results
            .Select(r =>
                FormattableString.Invariant(
                    $"{r.Candidate.Part2Rotation:F6}|{r.Candidate.Part2Offset.X:F6}|{r.Candidate.Part2Offset.Y:F6}|{r.RotatedArea:F6}|{r.Keep}|{r.Reason}"
                )
            )
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
}
