using OpenNest.Geometry;
using OpenNest.IO;

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

    [Fact]
    public void ObstacleOutsideWorkArea_IsIgnored()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(200, 200, 50, 50));
        var remnants = finder.FindRemnants();

        Assert.Single(remnants);
        Assert.Equal(100 * 100, remnants[0].Area(), 0.1);
    }

    [Fact]
    public void ObstaclePartiallyOutsideWorkArea_IsClipped()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        // Obstacle extends 20 units past the right edge
        finder.AddObstacle(new Box(80, 0, 40, 100));
        var remnants = finder.FindRemnants();

        // Should find the 80x100 strip on the left
        var left = remnants.FirstOrDefault(r =>
            r.Width >= 79.9 && r.Width <= 80.1 &&
            r.Length >= 99.9);
        Assert.NotNull(left);
    }

    [Fact]
    public void OverlappingObstacles_HandledCorrectly()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 60, 60));
        finder.AddObstacle(new Box(40, 40, 60, 60)); // overlaps first
        var remnants = finder.FindRemnants();

        // No remnant should overlap either obstacle
        foreach (var r in remnants)
        {
            Assert.False(
                r.Left < 60 && r.Right > 0 && r.Bottom < 60 && r.Top > 0
                && r.Left < 100 && r.Right > 40 && r.Bottom < 100 && r.Top > 40,
                "Remnant should not overlap both obstacles simultaneously in their shared region");
        }

        // Total remnant area + obstacle coverage should not exceed work area
        var totalRemnantArea = remnants.Sum(r => r.Area());
        Assert.True(totalRemnantArea < 100 * 100);
        Assert.True(totalRemnantArea > 0);
    }

    [Fact]
    public void ConstructorWithObstaclesList()
    {
        var obstacles = new List<Box>
        {
            new Box(0, 0, 40, 100),
            new Box(60, 0, 40, 100)
        };
        var finder = new RemnantFinder(new Box(0, 0, 100, 100), obstacles);
        var remnants = finder.FindRemnants();

        var gap = remnants.FirstOrDefault(r =>
            r.Width >= 19.9 && r.Width <= 20.1);
        Assert.NotNull(gap);
    }

    [Fact]
    public void AddObstacles_Plural_AddsMultiple()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacles(new[]
        {
            new Box(0, 0, 40, 100),
            new Box(60, 0, 40, 100)
        });
        var remnants = finder.FindRemnants();

        var gap = remnants.FirstOrDefault(r =>
            r.Width >= 19.9 && r.Width <= 20.1);
        Assert.NotNull(gap);
    }

    [Fact]
    public void IterativeWorkflow_AddObstacleThenRequery()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));

        // First fill: obstacle in bottom-left
        finder.AddObstacle(new Box(0, 0, 50, 50));
        var pass1 = finder.FindRemnants();
        Assert.True(pass1.Count >= 2);

        // Simulate filling the largest remnant by adding another obstacle
        var largest = pass1[0];
        finder.AddObstacle(new Box(largest.X, largest.Y, largest.Width / 2, largest.Length));
        var pass2 = finder.FindRemnants();

        // Should have more, smaller remnants now
        var pass2TotalArea = pass2.Sum(r => r.Area());
        var pass1TotalArea = pass1.Sum(r => r.Area());
        Assert.True(pass2TotalArea < pass1TotalArea);
    }

    [Fact]
    public void NoRemnantOverlapsObstacle()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(20, 20, 30, 30));
        finder.AddObstacle(new Box(60, 10, 25, 80));
        var remnants = finder.FindRemnants();

        foreach (var r in remnants)
        {
            // Check no remnant overlaps obstacle 1
            var overlaps1 = r.Left < 50 && r.Right > 20 && r.Bottom < 50 && r.Top > 20;
            Assert.False(overlaps1, $"Remnant ({r.X},{r.Y} {r.Width}x{r.Length}) overlaps obstacle 1");

            // Check no remnant overlaps obstacle 2
            var overlaps2 = r.Left < 85 && r.Right > 60 && r.Bottom < 90 && r.Top > 10;
            Assert.False(overlaps2, $"Remnant ({r.X},{r.Y} {r.Width}x{r.Length}) overlaps obstacle 2");
        }
    }

    [Fact]
    public void ManyObstacles_GridPattern()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));

        // Place a 5x5 grid of 10x10 obstacles with 10-unit gaps
        for (var row = 0; row < 5; row++)
            for (var col = 0; col < 5; col++)
                finder.AddObstacle(new Box(col * 20, row * 20, 10, 10));

        var remnants = finder.FindRemnants();

        // All remnants should be within the work area
        foreach (var r in remnants)
        {
            Assert.True(r.Left >= 0);
            Assert.True(r.Bottom >= 0);
            Assert.True(r.Right <= 100);
            Assert.True(r.Top <= 100);
        }

        // Should find gaps between obstacles
        Assert.True(remnants.Count > 0);
    }

    [Fact]
    public void SingleObstacle_NearEdge_FindsRemnantsOnAllSides()
    {
        // Obstacle near top-left: should find remnants above, below, and to the right.
        var finder = new RemnantFinder(new Box(0, 0, 120, 60));
        finder.AddObstacle(new Box(0, 47, 21, 6));
        var remnants = finder.FindRemnants();

        var above = remnants.FirstOrDefault(r => r.Bottom >= 53 - 0.1 && r.Width > 50);
        var below = remnants.FirstOrDefault(r => r.Top <= 47 + 0.1 && r.Width > 50);
        var right = remnants.FirstOrDefault(r => r.Left >= 21 - 0.1 && r.Length > 50);

        Assert.NotNull(above);
        Assert.NotNull(below);
        Assert.NotNull(right);
    }

    [Fact]
    public void LoadNestFile_FindsGapAboveMainGrid()
    {
        var nestFile = @"C:\Users\AJ\Desktop\no_remnant_found.nest";
        if (!File.Exists(nestFile))
            return; // Skip if file not available.

        var reader = new NestReader(nestFile);
        var nest = reader.Read();
        var plate = nest.Plates[0];

        var finder = RemnantFinder.FromPlate(plate);

        // Use smallest drawing bbox dimension as minDim (same as UI).
        var minDim = nest.Drawings.Min(d =>
            System.Math.Min(d.Program.BoundingBox().Width, d.Program.BoundingBox().Length));

        var tiered = finder.FindTieredRemnants(minDim);

        // Should find a remnant near (0.25, 53.13) — the gap above the main grid.
        var topGap = tiered.FirstOrDefault(t =>
            t.Box.Bottom > 50 && t.Box.Bottom < 55 &&
            t.Box.Left < 1 &&
            t.Box.Width > 100 &&
            t.Box.Length > 5);

        Assert.True(topGap.Box.Width > 0, "Expected remnant above main grid");
    }

    [Fact]
    public void DensePack_FindsGapAtTop()
    {
        // Reproduce real plate: 120x60, 68 parts of SULLYS-004.
        // Main grid tops out at y=53.14 (obstacle). Two rotated parts on the
        // right extend to y=58.49 but only at x > 106. The gap at x < 106
        // from y=53.14 to y=59.8 is ~106 x 6.66 — should be found.
        var workArea = new Box(0.2, 0.8, 119.5, 59.0);
        var obstacles = new List<Box>();
        var spacing = 0.25;

        // Main grid: 5 columns x 12 rows (6 pairs).
        // Even rows: bbox bottom offsets, odd rows: different offsets.
        double[] colX = { 0.25, 21.08, 41.90, 62.73, 83.56 };
        double[] colXOdd = { 0.81, 21.64, 42.46, 63.29, 84.12 };
        double[] evenY = { 3.67, 12.41, 21.14, 29.87, 38.60, 47.33 };
        double[] oddY = { 0.75, 9.48, 18.21, 26.94, 35.67, 44.40 };

        foreach (var cx in colX)
            foreach (var ey in evenY)
                obstacles.Add(new Box(cx - spacing, ey - spacing, 20.65 + spacing * 2, 5.56 + spacing * 2));
        foreach (var cx in colXOdd)
            foreach (var oy in oddY)
                obstacles.Add(new Box(cx - spacing, oy - spacing, 20.65 + spacing * 2, 5.56 + spacing * 2));

        // Right-side rotated parts (only 2 extend high: parts 62 and 66).
        obstacles.Add(new Box(106.70 - spacing, 37.59 - spacing, 5.56 + spacing * 2, 20.65 + spacing * 2));
        obstacles.Add(new Box(114.19 - spacing, 37.59 - spacing, 5.56 + spacing * 2, 20.65 + spacing * 2));
        // Parts 63, 67 (lower rotated)
        obstacles.Add(new Box(105.02 - spacing, 29.35 - spacing, 5.56 + spacing * 2, 20.65 + spacing * 2));
        obstacles.Add(new Box(112.51 - spacing, 29.35 - spacing, 5.56 + spacing * 2, 20.65 + spacing * 2));
        // Parts 60, 64 (upper-right rotated, lower)
        obstacles.Add(new Box(106.70 - spacing, 8.99 - spacing, 5.56 + spacing * 2, 20.65 + spacing * 2));
        obstacles.Add(new Box(114.19 - spacing, 8.99 - spacing, 5.56 + spacing * 2, 20.65 + spacing * 2));
        // Parts 61, 65
        obstacles.Add(new Box(105.02 - spacing, 0.75 - spacing, 5.56 + spacing * 2, 20.65 + spacing * 2));
        obstacles.Add(new Box(112.51 - spacing, 0.75 - spacing, 5.56 + spacing * 2, 20.65 + spacing * 2));

        var finder = new RemnantFinder(workArea, obstacles);
        var remnants = finder.FindRemnants(5.375);

        // The gap at x < 106 from y=53.14 to y=59.8 should be found.
        Assert.True(remnants.Count > 0, "Should find gap above main grid");
        var topRemnant = remnants.FirstOrDefault(r => r.Length >= 5.375 && r.Width > 50);
        Assert.NotNull(topRemnant);

        // Verify dimensions are close to the expected ~104 x 6.6 gap.
        Assert.True(topRemnant.Width > 100, $"Expected width > 100, got {topRemnant.Width:F1}");
        Assert.True(topRemnant.Length > 6, $"Expected length > 6, got {topRemnant.Length:F1}");
    }
}
