using OpenNest.CNC;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class CuttingNestedOrderTests
{
    // Host 0 with a cutout holding parts 1-4 in a row, and loose parts 5 and 6 beside the host.
    private static readonly Vector[] Centres =
    [
        new(50, 50), new(20, 50), new(40, 50), new(60, 50), new(80, 50), new(120, 10), new(120, 90),
    ];

    [Fact]
    public void WithoutNestedParts_TheOrderIsThePlainTour()
    {
        var parts = Enumerable.Range(0, Centres.Length).ToArray();
        var none = Centres.Select(_ => (IReadOnlyCollection<int>)Array.Empty<int>()).ToArray();

        var order = CuttingNestedOrder.Plan(parts, Vector.Zero, Context(none, none));

        Assert.Equal(CuttingPartOrder.Plan(parts, Centres, Vector.Zero, none, default), order);
    }

    [Fact]
    public void InnerParts_AreCutAsOneRunEndingBesideTheHost_AfterThePartsAroundIt()
    {
        var parts = Enumerable.Range(0, Centres.Length).ToArray();
        var (prerequisites, inside) = Nested();

        var order = CuttingNestedOrder.Plan(parts, Vector.Zero, Context(prerequisites, inside));

        Assert.Equal(parts, order.Order());
        // Loose parts first, then the whole inner run, then the host.
        Assert.Equal([5, 6], order.Take(2).Order());
        Assert.Equal([1, 2, 3, 4], order.Skip(2).Take(4).Order());
        Assert.Equal(0, order[^1]);
        // The tool comes from part 6 on the right, so the run ends at the right-hand inner part.
        Assert.Equal(4, order[^2]);
    }

    [Fact]
    public void LearnedRule_AcrossTheGroupStillHolds()
    {
        var parts = Enumerable.Range(0, Centres.Length).ToArray();
        var (prerequisites, inside) = Nested();
        // "Cut inner part 2 before loose part 5": the host group can no longer simply go last.
        prerequisites[5] = [2];

        var order = CuttingNestedOrder.Plan(parts, Vector.Zero, Context(prerequisites, inside)).ToList();

        Assert.Equal(parts, order.Order());
        Assert.True(order.IndexOf(2) < order.IndexOf(5));
        Assert.All(new[] { 1, 2, 3, 4 }, inner => Assert.True(order.IndexOf(inner) < order.IndexOf(0)));
    }

    [Fact]
    public void RemainingParts_AreOrderedFromTheResumePosition()
    {
        var (prerequisites, inside) = Nested();

        var order = CuttingNestedOrder.Plan([0, 2, 3, 4], new Vector(20, 50), Context(prerequisites, inside));

        Assert.Equal(0, order[^1]);
        Assert.Equal([2, 3, 4], order.Take(3).Order());
    }

    [Fact]
    public void CrowdedCutout_IsLeftThroughTheHostsOwnContours()
    {
        // A 40x40 frame with a radius-15 cutout packed with nine 5x5 parts, corner bolt holes
        // and a loose part beside it. From the middle of the pack no straight move reaches the frame.
        var frame = new Program();
        frame.MoveTo(35, 20);
        frame.ArcTo(35, 20, 20, 20, RotationType.CCW);
        foreach (var (x, y) in new[] { (3.0, 3.0), (37, 3), (37, 37), (3, 37) })
        {
            frame.MoveTo(x + 0.5, y);
            frame.ArcTo(x + 0.5, y, x, y, RotationType.CCW);
        }
        frame.MoveTo(0, 0);
        frame.LineTo(0, 40); frame.LineTo(40, 40); frame.LineTo(40, 0); frame.LineTo(0, 0);
        var parameters = ExplicitContourTests.Parameters();
        var small = new Drawing("inner", LeadPathValidationTests.Rectangle(0, 0, 5, 5));
        var parts = new List<Part> { new(new Drawing("frame", frame), new Vector(0, 0)) };
        foreach (var x in new[] { 10.5, 17.5, 24.5 })
            foreach (var y in new[] { 10.5, 17.5, 24.5 })
                parts.Add(new Part(small, new Vector(x, y)));
        parts.Add(new Part(small, new Vector(45, 10)));

        var result = CuttingPlanService.Plan(new CuttingPlanRequest(parts, confirmedParameters: parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.True(result.IndependentlyReplayed);
        var order = result.ProposedOrder.Select(p => p.SourceOrdinal).ToArray();
        Assert.Equal(10, order[0]);
        Assert.Equal(0, order[^1]);
        // The occupied cutout is the frame's first cut: the tool is already inside it.
        Assert.Equal(0, result.ProposedOrder[^1].ContourChoices[0].ContourOrdinal);
    }

    private static (IReadOnlyCollection<int>[] Prerequisites, IReadOnlyCollection<int>[] Inside) Nested()
    {
        var prerequisites = Centres.Select(_ => (IReadOnlyCollection<int>)Array.Empty<int>()).ToArray();
        var inside = Centres.Select(_ => (IReadOnlyCollection<int>)Array.Empty<int>()).ToArray();
        prerequisites[0] = [1, 2, 3, 4];
        inside[0] = [1, 2, 3, 4];
        return (prerequisites, inside);
    }

    // The host's cutout spans x 5..95 at y 50; its nearest point to a position is that position's x clamped.
    private static CuttingNestedOrder.Context Context(IReadOnlyCollection<int>[] prerequisites,
        IReadOnlyCollection<int>[] inside) =>
        new(Centres, prerequisites, inside,
            (_, position) => new Vector(System.Math.Clamp(position.X, 5, 95), 50), default);
}
