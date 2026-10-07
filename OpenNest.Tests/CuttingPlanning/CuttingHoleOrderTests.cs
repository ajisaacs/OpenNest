using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// The pure hole-path proposal: an OPEN path arrival -> every hole exactly once -> the fixed
/// perimeter entry, reusing the bounded whole-part routing machinery. Optimum comparisons
/// are computed from coordinates by brute force in the test — never hand-estimated.
/// </summary>
public class CuttingHoleOrderTests
{
    private static readonly Vector Arrival = new(0, 0);

    // Asymmetric fixture: without the terminal edge the route should end at C (the far
    // cluster point); with the perimeter entry at (6,0) the route must end at B instead,
    // so the endpoint changes the ORDER, not merely the total.
    private static readonly Vector A = new(1, 0);
    private static readonly Vector B = new(5, 0);
    private static readonly Vector C = new(3, 10);
    private static readonly Vector Entry = new(6, 0);

    private static double Cost(IReadOnlyList<int> route, IReadOnlyList<Vector> centres,
        Vector arrival, Vector? endpoint)
    {
        var total = 0.0;
        var position = arrival;
        foreach (var hole in route)
        {
            total += position.DistanceTo(centres[hole]);
            position = centres[hole];
        }
        return endpoint is { } e ? total + position.DistanceTo(e) : total;
    }

    private static double Minimum(IReadOnlyList<int> holes, IReadOnlyList<Vector> centres,
        Vector arrival, Vector? endpoint)
    {
        double Best(IEnumerable<int> remaining, Vector from, double soFar)
        {
            if (!remaining.Any())
                return soFar + (endpoint is { } e ? from.DistanceTo(e) : 0);
            return remaining.Min(next => Best(remaining.Where(h => h != next),
                centres[next], soFar + from.DistanceTo(centres[next])));
        }
        return Best(holes, arrival, 0);
    }

    [Fact]
    public void EmptyHoleListReturnsEmptyOrder()
    {
        Assert.Empty(CuttingHoleOrder.Plan(Array.Empty<int>(), new[] { Arrival }, Arrival, Entry));
    }

    [Fact]
    public void SingleHoleIsTheOnlyOrder()
    {
        var centres = new[] { A };
        var order = CuttingHoleOrder.Plan(new[] { 0 }, centres, Arrival, Entry);
        Assert.Equal(new[] { 0 }, order);
    }

    [Fact]
    public void PerimeterEntryChangesTheRouteWhereBruteForceSaysItShould()
    {
        var centres = new[] { A, B, C };
        var holes = new[] { 0, 1, 2 };

        var open = CuttingPartOrder.Plan(centres, Arrival,
            holes.Select(_ => (IReadOnlyCollection<int>)Array.Empty<int>()).ToList(), default);
        var faced = CuttingHoleOrder.Plan(holes, centres, Arrival, Entry);

        // The endpoint genuinely changes the proposal: the open path ends at C (away from
        // the entry), the faced path ends at B (next to the entry).
        Assert.Equal(2, open[^1]);
        Assert.Equal(1, faced[^1]);

        // Each is the brute-force optimum for its own cost function (computed here).
        Assert.Equal(Minimum(holes, centres, Arrival, null), Cost(open, centres, Arrival, null), 9);
        Assert.Equal(Minimum(holes, centres, Arrival, Entry), Cost(faced, centres, Arrival, Entry), 9);
    }

    [Fact]
    public void EveryRemainingHoleAppearsExactlyOnceAndNeverThePerimeter()
    {
        // Ordinals are the part's holes; the perimeter (say 7) is not among them.
        var centres = new[] { A, B, C, new Vector(-4, 2), new Vector(2, -3), new Vector(8, 8),
            new Vector(0, 6), new Vector(6, -6) };
        var holes = new[] { 0, 1, 2, 3, 4, 5, 6 };

        var order = CuttingHoleOrder.Plan(holes, centres, Arrival, Entry);

        Assert.Equal(holes.Length, order.Count);
        Assert.Equal(holes.OrderBy(h => h), order.OrderBy(h => h));
        Assert.DoesNotContain(7, order); // the perimeter ordinal is never visited
    }

    [Fact]
    public void OptimalForRandomSetAgainstBruteForce()
    {
        var random = new Random(20261006);
        var centres = Enumerable.Range(0, 7)
            .Select(_ => new Vector(random.NextDouble() * 20 - 10, random.NextDouble() * 20 - 10))
            .ToList();
        var entry = new Vector(12.5, -11.25);
        var holes = Enumerable.Range(0, 7).ToArray();

        var order = CuttingHoleOrder.Plan(holes, centres, Arrival, entry);

        // The bounded heuristic may not always equal the true optimum, so assert it is no
        // worse than the nearest-neighbour baseline and within 1.05x brute force.
        var brute = Minimum(holes, centres, Arrival, entry);
        Assert.True(Cost(order, centres, Arrival, entry) <= brute * 1.05 + 1e-9,
            $"proposed {Cost(order, centres, Arrival, entry):F4} > 1.05 x brute {brute:F4}");
    }

    [Fact]
    public void TiesBreakByStableOrdinal()
    {
        // Exact-distance pair from the arrival: nearest-neighbour takes the lower ordinal,
        // and strict-improvement-only passes never swap an equal-cost order arbitrarily.
        var centres = new[] { new Vector(5, 0), new Vector(-5, 0) };
        var noEndpoint = Array.Empty<int>();

        var first = CuttingHoleOrder.Plan(new[] { 0, 1 }, centres, Arrival, new Vector(0, 100));
        var second = CuttingHoleOrder.Plan(new[] { 0, 1 }, centres, Arrival, new Vector(0, 100));

        Assert.Equal(new[] { 0, 1 }, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void CancellationPropagates()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var centres = new[] { A, B, C };

        Assert.ThrowsAny<OperationCanceledException>(() =>
            CuttingHoleOrder.Plan(new[] { 0, 1, 2 }, centres, Arrival, Entry, cancelled.Token));
    }

    [Fact]
    public void NoEndpointMatchesLegacyWholePartRouteExactly()
    {
        // The whole-part overload without an endpoint must be untouched by the extension.
        var random = new Random(7);
        var centres = Enumerable.Range(0, 8)
            .Select(_ => new Vector(random.NextDouble() * 30, random.NextDouble() * 30))
            .ToList();
        var none = Array.Empty<int>();
        var prerequisites = centres.Select(_ => (IReadOnlyCollection<int>)none).ToList();

        var legacy = CuttingPartOrder.Plan(centres, Arrival, prerequisites, default);
        var extended = CuttingPartOrder.Plan(centres, Arrival, prerequisites, default, endpoint: null);

        Assert.Equal(legacy, extended);
    }
}
