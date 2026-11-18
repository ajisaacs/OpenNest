using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Tests.BestFit;

namespace OpenNest.Tests.Fill;

/// <summary>
/// The pair filler ranks grids before their final overlap checks run and checks only those it
/// needs (<see cref="PairFiller.CompareByCount"/>); the stripe filler reuses convergences
/// (<see cref="StripeConvergenceCache"/>). Both must leave every layout exactly as computing
/// everything up front would.
/// </summary>
[Collection(nameof(FillCacheCollection))]
public class DeferredGridCheckTests
{
    private const double Spacing = 0.5;

    [Fact]
    public void Resolve_ReturnsExactlyWhatFillReturns()
    {
        foreach (var (area, pattern, direction) in GridCases())
        {
            var expected = new FillLinear(area, Spacing).Fill(pattern, direction);
            var actual = new FillLinear(area, Spacing).FillDeferred(pattern, direction).Resolve();
            Assert.Equal(Poses(expected), Poses(actual));
        }
    }

    [Fact]
    public void CompareByCount_SortsLikeCheckedCounts_AndSkipsChecks()
    {
        var cases = GridCases();
        var expectedFallbacks = 0;
        var skipped = 0;

        for (var seed = 0; seed < 25; seed++)
        {
            var order = Enumerable.Range(0, cases.Count).ToList();
            var random = new Random(seed);
            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var eager = order
                .Select(index =>
                {
                    var (area, pattern, direction) = cases[index];
                    return (Index: index, Parts: new FillLinear(area, Spacing).Fill(pattern, direction));
                })
                .ToList();
            eager.Sort((a, b) => b.Parts.Count.CompareTo(a.Parts.Count));

            var indexOf = new Dictionary<FillLinear.DeferredGrid, int>(ReferenceEqualityComparer.Instance);
            var lazy = new List<FillLinear.DeferredGrid>();
            foreach (var index in order)
            {
                var (area, pattern, direction) = cases[index];
                var grid = new FillLinear(area, Spacing).FillDeferred(pattern, direction);
                if (grid.IsPending && grid.Count != grid.FallbackCount)
                {
                    var tiled = grid.Count;
                    var checkedCount = new FillLinear(area, Spacing).Fill(pattern, direction).Count;
                    if (checkedCount != tiled)
                        expectedFallbacks++;
                }
                indexOf.Add(grid, index);
                lazy.Add(grid);
            }
            PairFiller.SortByCount(lazy);

            Assert.Equal(eager.Select(e => e.Index), lazy.Select(g => indexOf[g]));
            skipped += lazy.Count(g => g.IsPending);

            for (var k = 0; k < lazy.Count; k++)
                Assert.Equal(Poses(eager[k].Parts), Poses(lazy[k].Resolve()));
        }

        // The fixture holds grids whose check fails and changes their count, and the ranking
        // left some checks unrun.
        Assert.True(expectedFallbacks > 0);
        Assert.True(skipped > 0);
    }

    [Fact]
    public void StripeConvergenceCache_HitsReproduceTheConvergedLayouts()
    {
        var drawing = TestHelpers.MakeLShapeDrawing();
        var bestFits = new List<BestFitResult>
        {
            MakePair(drawing, 0, new Vector(10.5, 0)),
            MakePair(drawing, System.Math.PI, new Vector(10.5, 10)),
        };

        List<Part> Run(StripeConvergenceCache cache, NestDirection axis, int maxQuantity)
        {
            var context = new FillContext
            {
                Item = new NestItem { Drawing = drawing, Quantity = maxQuantity },
                WorkArea = new Box(0, 0, 97.5, 47.25),
                Plate = new Plate(47.25, 97.5) { PartSpacing = Spacing },
                MaxQuantity = maxQuantity,
            };
            context.SharedState["BestFits"] = bestFits;
            if (cache != null)
                context.SharedState[StripeConvergenceCache.SharedStateKey] = cache;
            return new StripeFiller(context, axis) { CompleteStripesOnly = true }.Fill();
        }

        foreach (var maxQuantity in new[] { 0, 30 })
        {
            foreach (var axis in new[] { NestDirection.Horizontal, NestDirection.Vertical })
            {
                var cold = Run(null, axis, maxQuantity);
                var cache = new StripeConvergenceCache();
                var filling = Run(cache, axis, maxQuantity);
                var entries = cache.Count;
                var warm = Run(cache, axis, maxQuantity);

                Assert.NotEmpty(cold);
                Assert.True(entries > 0);
                Assert.Equal(entries, cache.Count);
                Assert.Equal(Poses(cold), Poses(filling));
                Assert.Equal(Poses(cold), Poses(warm));
            }
        }
    }

    /// <summary>
    /// Grids across work areas, both directions and three patterns: a single concave part, a
    /// valid pair of it, and a pair of overlapping copies whose checks fail. At widths 16.75 and
    /// 25.75 the overlapping pair's fallback holds fewer and more parts than its tiling.
    /// </summary>
    private static List<(Box Area, Pattern Pattern, NestDirection Direction)> GridCases()
    {
        var concave = FillExtentsTests.MakeFixture("concave");
        var single = new Pattern();
        single.Parts.Add(Part.CreateAtOrigin(concave));
        single.UpdateBounds();

        var first = Part.CreateAtOrigin(concave);
        var second = Part.CreateAtOrigin(concave, System.Math.PI);
        second.Offset(new Vector(10.5, 0));
        var pair = new Pattern();
        pair.Parts.AddRange(new[] { first, second });
        pair.UpdateBounds();

        var overlapping = new Pattern();
        overlapping.Parts.AddRange(new[] { first, first.CloneAtOffset(new Vector(0.25, 0.25)) });
        overlapping.UpdateBounds();
        Assert.True(FillHelpers.HasOverlappingParts(overlapping.Parts));

        var cases = new List<(Box, Pattern, NestDirection)>();
        foreach (var length in new[] { 21.0, 30.0, 40.0 })
            foreach (var width in new[] { 12.0, 16.75, 25.75 })
                foreach (var pattern in new[] { single, pair, overlapping })
                    foreach (var direction in new[] { NestDirection.Horizontal, NestDirection.Vertical })
                        cases.Add((new Box(0, 0, length, width), pattern, direction));
        return cases;
    }

    private static BestFitResult MakePair(Drawing drawing, double rotation, Vector offset)
    {
        var bounds = drawing.Program.BoundingBox();
        return new BestFitResult
        {
            Candidate = new PairCandidate
            {
                Drawing = drawing,
                Part2Rotation = rotation,
                Part2Offset = offset,
                Spacing = Spacing,
            },
            BoundingWidth = bounds.Length + offset.X,
            BoundingHeight = bounds.Width + offset.Y,
            OptimalRotation = 0,
            Keep = true,
            Reason = "Valid",
            HullAngles = new List<double>(),
        };
    }

    private static List<(long X, long Y, long Rotation)> Poses(List<Part> parts) =>
        parts
            .Select(p => (
                BitConverter.DoubleToInt64Bits(p.Location.X),
                BitConverter.DoubleToInt64Bits(p.Location.Y),
                BitConverter.DoubleToInt64Bits(p.Rotation)
            ))
            .ToList();
}
