using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingPartOrderTests
{
    private static readonly Vector[] Line = [new(1, 0), new(-2, 0), new(3, 0)];

    [Fact]
    public void Plan_ShortensTheNearestNeighbourTour()
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
        // Nearest neighbour gives 4, 1, 0, 2, 3. With reversals the tour ends at 4, 3, 1, 0, 2
        // (14.14); moving short runs alone stops at 2, 0, 4, 1, 3 (15.30).
        Vector[] centres = [new(-2, -1), new(0, -2), new(-4, 3), new(2, -4), new(0, -1)];

        Assert.Equal(new[] { 4, 3, 1, 0, 2 }, Plan(centres, NoPrerequisites(5)));
    }

    [Fact]
    public void Plan_MovesARunOfPartsWhereReversalsCannotHelp()
    {
        // Nearest neighbour and 2-opt stop at 1, 3, 2, 0 (13.78); moving part 0 to the front
        // gives 0, 1, 3, 2 (12.16).
        Vector[] centres = [new(-3, 0), new(1, 0), new(4, -3), new(3, 0)];

        Assert.Equal(new[] { 0, 1, 3, 2 }, Plan(centres, NoPrerequisites(4)));
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

    private static int[] Plan(Vector[] centres, IReadOnlyCollection<int>[] prerequisites) =>
        CuttingPartOrder.Plan(centres, Vector.Zero, prerequisites, CancellationToken.None);

    private static IReadOnlyCollection<int>[] NoPrerequisites(int count) =>
        Enumerable.Range(0, count).Select(_ => (IReadOnlyCollection<int>)Array.Empty<int>()).ToArray();
}
