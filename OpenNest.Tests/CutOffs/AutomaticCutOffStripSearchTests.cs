namespace OpenNest.Tests.CutOffs;

public class AutomaticCutOffStripSearchTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EqualLength_PiercesThenDeviationBreakTies(bool pierceTieBreak)
    {
        var result = AutomaticCutOffStripSearch.Solve(0, new[] { new[] { 18.0, 35.0 } },
            new[] { 35.0 }, 35, 17.5, x => (10, pierceTieBreak && x == 35 ? 2 : 1));
        Assert.Equal(new[] { pierceTieBreak ? 18.0 : 35.0 }, result);
    }

    [Fact]
    public void GlobalOptimum_IsBetterThanGreedyFirstLine()
    {
        var result = AutomaticCutOffStripSearch.Solve(0,
            new[] { new[] { 20.0, 35.0 }, new[] { 40.0, 55.0, 70.0 } },
            new[] { 35.0, 70.0 }, 35, 17.5, x => (x == 20 || x == 70 ? 0 : x == 35 ? 1 : 100, 1));
        Assert.Equal(new[] { 35.0, 70.0 }, result);
    }

    [Fact]
    public void LastTieBreak_IsLexicographicOverTheWholePrefix_NotTheLastPosition()
    {
        // Both paths are equally far from the nominal list, length and pierces are equal.
        var result = AutomaticCutOffStripSearch.Solve(0,
            new[] { new[] { 20.0, 25.0 }, new[] { 45.0, 50.0 } },
            new[] { 22.5, 47.5 }, 35, 17.5, _ => (10, 1));
        Assert.Equal(new[] { 20.0, 45.0 }, result);
    }

    [Fact]
    public void SeededFiniteCandidateSearch_EqualsIndependentExhaustiveOracle()
    {
        var random = new Random(93512);
        for (var sample = 0; sample < 200; sample++)
        {
            var rows = new[] { new[] { 18.0, 23, 29, 35 }, new[] { 38.0, 47, 56, 62, 70 }, new[] { 60.0, 74, 81, 91, 105 } };
            var costs = rows.SelectMany(row => row).Distinct().ToDictionary(x => x,
                _ => (Length: random.Next(0, 4) + random.Next(0, 2) * 0.0000001, SegmentCount: random.Next(0, 3)));
            var nominal = new[] { 35.0, 70, 105 };
            var paths = new List<double[]>();
            foreach (var first in rows[0])
                foreach (var second in rows[1])
                    foreach (var third in rows[2])
                        if (second - first >= 17.5 && second - first <= 35 && third - second >= 17.5 && third - second <= 35)
                            paths.Add(new[] { first, second, third });
            var expected = paths.OrderBy(path => path.Sum(x => System.Math.Round(costs[x].Length, 6)))
                .ThenBy(path => path.Sum(x => costs[x].SegmentCount))
                .ThenBy(path => Enumerable.Range(0, 3).Sum(i => System.Math.Abs(nominal[i] - path[i])))
                .ThenBy(path => path[0]).ThenBy(path => path[1]).ThenBy(path => path[2]).First();
            var actual = AutomaticCutOffStripSearch.Solve(0, rows, nominal, 35, 17.5, x => costs[x]);
            Assert.Equal(expected, actual);
        }
    }
}
