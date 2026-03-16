using OpenNest.Geometry;

namespace OpenNest.Tests;

public class RemnantFinderTests
{
    [Fact]
    public void EmptyPlate_ReturnsWholeWorkArea()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        var remnants = finder.FindRemnants();

        Assert.Single(remnants);
        Assert.Equal(100 * 100, remnants[0].Area(), 0.1);
    }

    [Fact]
    public void SingleObstacle_InCorner_FindsLShapedRemnants()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 40, 40));
        var remnants = finder.FindRemnants();

        Assert.True(remnants.Count >= 2);
        var largest = remnants[0];
        Assert.Equal(60 * 100, largest.Area(), 0.1);
    }

    [Fact]
    public void SingleObstacle_InCenter_FindsFourRemnants()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(30, 30, 40, 40));
        var remnants = finder.FindRemnants();

        Assert.True(remnants.Count >= 4);
    }

    [Fact]
    public void MinDimension_FiltersSmallRemnants()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 95, 100));
        var all = finder.FindRemnants(0);
        var filtered = finder.FindRemnants(10);

        Assert.True(all.Count > filtered.Count);
        foreach (var r in filtered)
        {
            Assert.True(r.Width >= 10);
            Assert.True(r.Length >= 10);
        }
    }

    [Fact]
    public void ResultsSortedByAreaDescending()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 50, 50));
        var remnants = finder.FindRemnants();

        for (var i = 1; i < remnants.Count; i++)
            Assert.True(remnants[i - 1].Area() >= remnants[i].Area());
    }

    [Fact]
    public void AddObstacle_UpdatesResults()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        var before = finder.FindRemnants();
        Assert.Single(before);

        finder.AddObstacle(new Box(0, 0, 50, 50));
        var after = finder.FindRemnants();
        Assert.True(after.Count > 1);
    }

    [Fact]
    public void ClearObstacles_ResetsToFullWorkArea()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 50, 50));
        finder.ClearObstacles();
        var remnants = finder.FindRemnants();

        Assert.Single(remnants);
        Assert.Equal(100 * 100, remnants[0].Area(), 0.1);
    }

    [Fact]
    public void FullyCovered_ReturnsEmpty()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 100, 100));
        var remnants = finder.FindRemnants();

        Assert.Empty(remnants);
    }

    [Fact]
    public void MultipleObstacles_FindsGapBetween()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 40, 100));
        finder.AddObstacle(new Box(60, 0, 40, 100));
        var remnants = finder.FindRemnants();

        var gap = remnants.FirstOrDefault(r =>
            r.Width >= 19.9 && r.Width <= 20.1 &&
            r.Length >= 99.9);
        Assert.NotNull(gap);
    }

    [Fact]
    public void FromPlate_CreatesFinderWithPartsAsObstacles()
    {
        var plate = TestHelpers.MakePlate(60, 120,
            TestHelpers.MakePartAt(0, 0, 20));
        var finder = RemnantFinder.FromPlate(plate);
        var remnants = finder.FindRemnants();

        Assert.True(remnants.Count >= 1);
        Assert.True(remnants[0].Area() < plate.WorkArea().Area());
    }
}
