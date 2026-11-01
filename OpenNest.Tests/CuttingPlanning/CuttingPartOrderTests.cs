using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingPartOrderTests
{
    private static readonly Vector[] Line = [new(1, 0), new(-2, 0), new(3, 0)];

    [Fact]
    public void Plan_ShortensTheNearestNeighborTour()
    {
        // Nearest first gives 0, 2, 1 (1 + 2 + 5 = 8); going left first is 2 + 3 + 2 = 7.
        var order = Plan(Line, NoPrerequisites(3));

        Assert.Equal(new[] { 1, 0, 2 }, order);
    }

    [Fact]
    public void Plan_NeverPlacesAPartBeforeItsPrerequisite()
    {
        // The shortest tour starts with part 1, but part 2 must come before it.
        var prerequisites = NoPrerequisites(3);
        prerequisites[1] = [2];

        var order = Plan(Line, prerequisites);

        Assert.Equal(new[] { 0, 2, 1 }, order);
    }

    [Fact]
    public void Plan_WaitsForAPrerequisiteBeforeTheNearestPart()
    {
        // Part 0 is nearest and the shorter tour, but part 1 must be cut first.
        var prerequisites = NoPrerequisites(2);
        prerequisites[0] = [1];

        Assert.Equal(new[] { 1, 0 }, Plan([new(1, 0), new(5, 0)], prerequisites));
    }

    [Fact]
    public void Plan_OnASubset_TreatsPrerequisitesOutsideItAsDone()
    {
        var prerequisites = NoPrerequisites(3);
        prerequisites[1] = [0];

        var order = CuttingPartOrder.Plan([1, 2], Line, new Vector(4, 0), prerequisites, CancellationToken.None);

        Assert.Equal(new[] { 2, 1 }, order);
    }

    [Fact]
    public void Plan_ReversalsShortenTheTour()
    {
        // Nearest neighbor gives 4, 1, 0, 2, 3. With reversals the tour ends at 4, 3, 1, 0, 2
        // (14.14); moving short runs alone stops at 2, 0, 4, 1, 3 (15.30).
        Vector[] centers = [new(-2, -1), new(0, -2), new(-4, 3), new(2, -4), new(0, -1)];

        Assert.Equal(new[] { 4, 3, 1, 0, 2 }, Plan(centers, NoPrerequisites(5)));
    }

    [Fact]
    public void Plan_MovesARunOfPartsWhereReversalsCannotHelp()
    {
        // Nearest neighbor and 2-opt stop at 1, 3, 2, 0 (13.78); moving part 0 to the front
        // gives 0, 1, 3, 2 (12.16).
        Vector[] centers = [new(-3, 0), new(1, 0), new(4, -3), new(3, 0)];

        Assert.Equal(new[] { 0, 1, 3, 2 }, Plan(centers, NoPrerequisites(4)));
    }

    [Fact]
    public void Plan_EqualTours_PreferTheLowerOrdinalFirst()
    {
        Assert.Equal(new[] { 0, 1 }, Plan([new(0, 5), new(0, -5)], NoPrerequisites(2)));
        Assert.Equal(new[] { 0, 1 }, Plan([new(0, -5), new(0, 5)], NoPrerequisites(2)));
    }

    [Fact]
    public void Plan_PrerequisiteCycle_Throws()
    {
        var prerequisites = NoPrerequisites(2);
        prerequisites[0] = [1];
        prerequisites[1] = [0];

        Assert.Throws<InvalidOperationException>(() => Plan([new(0, 0), new(1, 0)], prerequisites));
    }

    [Fact]
    public void Plan_LeavesAPartFromItsExitPoint()
    {
        // Part 1 is a line entered at (2, 0) and left at (2, 100). Toured from its entry
        // alone, 1, 0, 2 looks shortest; left from its far end, 0, 1, 2 is (5 + 5.39 + 5.39).
        Vector[] entries = [new(0, 5), new(2, 0), new(0, 95)];
        Vector[] exits = [new(0, 5), new(2, 100), new(0, 95)];

        Assert.Equal(new[] { 0, 1, 2 }, CuttingPartOrder.Plan(entries, Vector.Zero, NoPrerequisites(3),
            CancellationToken.None, exits: exits));
    }

    // Part 3 is entered at (0, 6) and left at (0, 13). A reversed run is costed along its own
    // legs, from each part's exit: 0, 2, 4, 1, 3 (14.97), not 3, 1, 4, 2, 0 (20.17).
    [Fact]
    public void Plan_CostsAReversedRunAlongItsOwnLegsFromEachExit()
    {
        Vector[] entries = [new(6, 1), new(2, 6), new(5, 2), new(0, 6), new(4, 2)];
        Vector[] exits = [new(6, 1), new(2, 6), new(5, 2), new(0, 13), new(4, 2)];

        Assert.Equal(new[] { 0, 2, 4, 1, 3 }, CuttingPartOrder.Plan(entries, Vector.Zero, NoPrerequisites(5),
            CancellationToken.None, exits: exits));
    }

    // Part 4 is entered at (4, 4) and left at (4, 13). Nearest first goes on from where each
    // part is left: 4, 2, 1, 0, 3 (19.92), not 0, 3, 2, 1, 4 (20.49).
    [Fact]
    public void Plan_NearestFirstGoesOnFromEachPartsExit()
    {
        Vector[] entries = [new(7, 4), new(5, 7), new(7, 9), new(9, 6), new(4, 4)];
        Vector[] exits = [new(7, 4), new(5, 7), new(7, 9), new(9, 6), new(4, 13)];

        Assert.Equal(new[] { 4, 2, 1, 0, 3 }, CuttingPartOrder.Plan(entries, Vector.Zero, NoPrerequisites(5),
            CancellationToken.None, exits: exits));
    }

    private static int[] Plan(Vector[] centers, IReadOnlyCollection<int>[] prerequisites) =>
        CuttingPartOrder.Plan(centers, Vector.Zero, prerequisites, CancellationToken.None);

    private static IReadOnlyCollection<int>[] NoPrerequisites(int count) =>
        Enumerable.Range(0, count).Select(_ => (IReadOnlyCollection<int>)Array.Empty<int>()).ToArray();
}
