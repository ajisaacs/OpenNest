using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Tests.BestFit;

namespace OpenNest.Tests.Fill;

public class DefaultFillComparerTests
{
    private readonly IFillComparer comparer = new DefaultFillComparer();
    private readonly Box workArea = new(0, 0, 100, 100);

    [Fact]
    public void NullCandidate_ReturnsFalse()
    {
        var current = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        Assert.False(comparer.IsBetter(null, current, workArea));
    }

    [Fact]
    public void EmptyCandidate_ReturnsFalse()
    {
        var current = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        Assert.False(comparer.IsBetter(new List<Part>(), current, workArea));
    }

    [Fact]
    public void NullCurrent_ReturnsTrue()
    {
        var candidate = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        Assert.True(comparer.IsBetter(candidate, null, workArea));
    }

    [Fact]
    public void HigherCount_Wins()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(20, 0, 10),
            TestHelpers.MakePartAt(40, 0, 10),
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(20, 0, 10),
        };
        Assert.True(comparer.IsBetter(candidate, current, workArea));
    }

    [Fact]
    public void SameCount_HigherDensityWins()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(12, 0, 10),
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(50, 0, 10),
        };
        Assert.True(comparer.IsBetter(candidate, current, workArea));
    }

    [Fact]
    public void LowerCount_ReturnsFalse()
    {
        var candidate = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(20, 0, 10),
        };

        Assert.False(comparer.IsBetter(candidate, current, workArea));
    }

    [Fact]
    public void SameCount_LowerDensity_ReturnsFalse()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(50, 0, 10),
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(12, 0, 10),
        };

        Assert.False(comparer.IsBetter(candidate, current, workArea));
    }

    [Fact]
    public void ExactScoreTie_ReturnsFalseInBothOrders()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(5, 7, 10),
            TestHelpers.MakePartAt(25, 7, 10),
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(20, 0, 10),
        };

        Assert.Equal(FillScore.Compute(candidate, workArea), FillScore.Compute(current, workArea));
        Assert.False(comparer.IsBetter(candidate, current, workArea));
        Assert.False(comparer.IsBetter(current, candidate, workArea));
        Assert.False(comparer.IsBetter(candidate, candidate, workArea));
    }

    [Fact]
    public void UnequalCounts_SmallerLayoutIsDenser_ButCountWinsInBothOrders()
    {
        var larger = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(40, 0, 10),
            TestHelpers.MakePartAt(80, 0, 10),
        };
        var smaller = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(12, 0, 10),
        };

        Assert.True(FillScore.Compute(smaller, workArea).Density > FillScore.Compute(larger, workArea).Density);
        Assert.True(comparer.IsBetter(larger, smaller, workArea));
        Assert.False(comparer.IsBetter(smaller, larger, workArea));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(0, 2, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(1, 2, false)]
    [InlineData(2, 0, true)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    public void NullEmptyAndNonemptyInputs_PreserveGuardOrder(int candidateKind, int currentKind, bool expected)
    {
        // 0 = null, 1 = empty, 2 = one valid part; comparing that part to itself ties.
        var inputs = new List<Part>?[]
        {
            null,
            new(),
            new() { TestHelpers.MakePartAt(0, 0, 10) },
        };

        Assert.Equal(expected, comparer.IsBetter(inputs[candidateKind], inputs[currentKind], workArea));
    }

    [Fact]
    public void ValidLayoutMatrix_MatchesReferenceInBothOrdersAndTies_WithoutMutatingInputs()
    {
        var layouts = new List<List<Part>> { new() };
        foreach (var count in new[] { 1, 2, 4, 7 })
            foreach (var size in new[] { 1.0, 3.0 })
                foreach (var pitch in new[] { 4.0, 12.0 })
                    foreach (var origin in new[] { new Vector(0, 0), new Vector(5, 9) })
                    {
                        var parts = new List<Part>();
                        for (var i = 0; i < count; i++)
                            parts.Add(TestHelpers.MakePartAt(origin.X + i % 3 * pitch, origin.Y + i / 3 * pitch, size));
                        // Also vary enumeration order; the comparer must not reorder caller lists.
                        if (origin.X > 0)
                            parts.Reverse();
                        layouts.Add(parts);
                    }

        foreach (var parts in layouts)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                Assert.True(double.IsFinite(part.BaseDrawing.Area));
                Assert.True(part.BaseDrawing.Area > 0);
                Assert.True(workArea.Contains(part.BoundingBox));
                foreach (var value in new[] { part.Left, part.Right, part.Top, part.Bottom, part.Rotation })
                    Assert.True(double.IsFinite(value));
                for (var j = 0; j < i; j++)
                    Assert.False(part.BoundingBox.Intersects(parts[j].BoundingBox));
            }
        }

        var before = layouts.Select(Snapshot).ToArray();
        var workAreaBefore = (workArea.X, workArea.Y, workArea.Length, workArea.Width);
        // Full Cartesian matrix includes both argument orders, self-comparisons,
        // translated/permuted exact ties, and equal/unequal counts and densities.
        foreach (var candidate in layouts)
            foreach (var current in layouts)
            {
                var expected = FillScore.Compute(candidate, workArea) > FillScore.Compute(current, workArea);
                Assert.Equal(expected, comparer.IsBetter(candidate, current, workArea));
            }

        for (var i = 0; i < layouts.Count; i++)
            Assert.Equal(before[i], Snapshot(layouts[i]));
        Assert.Equal(workAreaBefore, (workArea.X, workArea.Y, workArea.Length, workArea.Width));
    }

    private static object[] Snapshot(List<Part> parts)
    {
        var values = new List<object>();
        foreach (var part in parts)
        {
            values.Add(part);
            values.Add(part.BaseDrawing);
            values.Add(part.BaseDrawing.Area);
            values.Add(part.Location);
            values.Add(part.Rotation);
            values.Add(part.BoundingBox);
            values.Add((part.Left, part.Right, part.Top, part.Bottom));
            foreach (var program in new[] { part.Program, part.BaseDrawing.Program })
            {
                values.Add(program);
                values.Add(program.Mode);
                values.Add(program.Rotation);
                foreach (var code in program.Codes)
                {
                    values.Add(code);
                    if (code is Motion motion)
                        values.Add(motion.EndPoint);
                }
            }
        }
        return values.ToArray();
    }
}

#if DEBUG
// PerfCounters is process-wide. This collection excludes all parallel tests;
// always clear counters in finally, including when the skipped-work assertion fails.
[Collection(nameof(FillCacheCollection))]
public class DefaultFillComparerWorkTests
{
    [Theory]
    [InlineData(2, 1, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(2, 2, 2)]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 0)]
    public void IsBetter_ComputesScoresOnlyForNonemptyEqualCounts(int candidateCount, int currentCount, long expectedComputations)
    {
        var candidate = Enumerable.Range(0, candidateCount).Select(i => TestHelpers.MakePartAt(i * 20, 0, 10)).ToList();
        var current = Enumerable.Range(0, currentCount).Select(i => TestHelpers.MakePartAt(i * 20, 0, 10)).ToList();
        var workArea = new Box(0, 0, 100, 100);
        var comparer = new DefaultFillComparer();
        var expected = FillScore.Compute(candidate, workArea) > FillScore.Compute(current, workArea);

        PerfCounters.Reset();
        try
        {
            Assert.Equal(expected, comparer.IsBetter(candidate, current, workArea));
            Assert.Equal(expectedComputations, PerfCounters.FillScoreComputations);
        }
        finally
        {
            PerfCounters.Reset();
        }
    }
}
#endif

public class VerticalRemnantComparerTests
{
    private readonly IFillComparer comparer = new VerticalRemnantComparer();
    private readonly Box workArea = new(0, 0, 100, 100);

    [Fact]
    public void HigherCount_WinsRegardlessOfExtent()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(40, 0, 10),
            TestHelpers.MakePartAt(80, 0, 10),
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(12, 0, 10),
        };
        Assert.True(comparer.IsBetter(candidate, current, workArea));
    }

    [Fact]
    public void SameCount_SmallerXExtent_Wins()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(12, 0, 10),
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(50, 0, 10),
        };
        Assert.True(comparer.IsBetter(candidate, current, workArea));
    }

    [Fact]
    public void SameCount_SameExtent_HigherDensityWins()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(40, 0, 10),
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(40, 40, 10),
        };
        Assert.True(comparer.IsBetter(candidate, current, workArea));
    }

    [Fact]
    public void NullCandidate_ReturnsFalse()
    {
        var current = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        Assert.False(comparer.IsBetter(null, current, workArea));
    }

    [Fact]
    public void NullCurrent_ReturnsTrue()
    {
        var candidate = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        Assert.True(comparer.IsBetter(candidate, null, workArea));
    }
}

public class HorizontalRemnantComparerTests
{
    private readonly IFillComparer comparer = new HorizontalRemnantComparer();
    private readonly Box workArea = new(0, 0, 100, 100);

    [Fact]
    public void SameCount_SmallerYExtent_Wins()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(0, 12, 10),
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(0, 50, 10),
        };
        Assert.True(comparer.IsBetter(candidate, current, workArea));
    }

    [Fact]
    public void HigherCount_WinsRegardlessOfExtent()
    {
        var candidate = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(0, 40, 10),
            TestHelpers.MakePartAt(0, 80, 10),
        };
        var current = new List<Part>
        {
            TestHelpers.MakePartAt(0, 0, 10),
            TestHelpers.MakePartAt(0, 12, 10),
        };
        Assert.True(comparer.IsBetter(candidate, current, workArea));
    }
}
