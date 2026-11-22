using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// The memo may only ever answer what <see cref="SpacingOutline.Overlaps"/> answers for the
/// same two perimeters: it saves the test, never changes its verdict.
/// </summary>
public class OutlineOverlapMemoTests
{
    [Fact]
    public void VerdictsMatchTheDirectTest_FirstAndRemembered()
    {
        var memo = new OutlineOverlapMemo(10_000, 10_000);
        var outlines = Outlines().ToList();

        // Twice: the first pass tests and remembers, the second answers from memory, with fresh
        // outline objects of the same vertices as a later fill would bring.
        for (var pass = 0; pass < 2; pass++)
        {
            var fresh = Outlines().ToList();
            for (var i = 0; i < fresh.Count; i++)
                for (var j = 0; j < fresh.Count; j++)
                {
                    var expected = outlines[i].Overlaps(outlines[j]);
                    var actual = memo.Overlaps(fresh[i], memo.ShapeOf(fresh[i]), fresh[j], memo.ShapeOf(fresh[j]));
                    Assert.True(expected == actual, $"pass {pass}, outlines {i} and {j}");
                }
        }
    }

    [Fact]
    public void OutlinesOfTheSameVerticesShareAnEntry_OthersDoNot()
    {
        var memo = new OutlineOverlapMemo(10_000, 10_000);
        var shape = memo.ShapeOf(Bar(0, 0));

        Assert.Same(shape, memo.ShapeOf(Bar(0, 0)));
        Assert.NotSame(shape, memo.ShapeOf(Bar(0, 1e-12)));
        // The same loop started at another vertex is another vertex order.
        Assert.NotSame(shape, memo.ShapeOf(Rotated(Bar(0, 0))));
    }

    [Fact]
    public void OutlinesWithCutoutsAreTestedDirectly()
    {
        var memo = new OutlineOverlapMemo(10_000, 10_000);
        var ring = new SpacingOutline(Loop((0, 0), (10, 0), (10, 10), (0, 10)),
            new List<Polygon> { Loop((2, 2), (8, 2), (8, 8), (2, 8)) });
        var inside = new SpacingOutline(Loop((4, 4), (6, 4), (6, 6), (4, 6)), new List<Polygon>());

        Assert.Null(memo.ShapeOf(ring));
        Assert.False(memo.Overlaps(inside, memo.ShapeOf(inside), ring, memo.ShapeOf(ring)));
        Assert.False(memo.Overlaps(ring, memo.ShapeOf(ring), inside, memo.ShapeOf(inside)));
    }

    [Fact]
    public void FullGenerationsDropOldEntriesWithoutChangingVerdicts()
    {
        // Room for two bars and one verdict per generation: nearly every lookup turns one over.
        var memo = new OutlineOverlapMemo(8, 1);
        for (var pass = 0; pass < 3; pass++)
            for (var i = 0; i < 12; i++)
                for (var j = 0; j < 12; j++)
                {
                    var a = Bar(i * 0.75, 0);
                    var b = Bar(j * 0.75, 0.5);
                    var expected = Bar(i * 0.75, 0).Overlaps(Bar(j * 0.75, 0.5));
                    Assert.Equal(expected, memo.Overlaps(a, memo.ShapeOf(a), b, memo.ShapeOf(b)));
                }
    }

    // Convex and concave outlines that overlap, touch, nest and miss each other.
    private static IEnumerable<SpacingOutline> Outlines()
    {
        yield return Bar(0, 0);
        yield return Bar(0.5, 0.25);
        yield return Bar(2, 0); // Shares an edge with the first.
        yield return Bar(5, 5);
        yield return Outline((0, 0), (6, 0), (6, 1), (1, 1), (1, 6), (0, 6)); // An L.
        yield return Outline((1.5, 1.5), (4, 1.5), (4, 4), (1.5, 4)); // In the L's notch.
        yield return Outline((0.5, 0.5), (3, 0.5), (3, 3), (0.5, 3)); // Over the L's corner.
        yield return Outline((0.2, 0.2), (0.4, 0.2), (0.4, 0.4), (0.2, 0.4)); // Inside the first bar.
    }

    private static SpacingOutline Bar(double x, double y) =>
        Outline((x, y), (x + 2, y), (x + 2, y + 1), (x, y + 1));

    private static SpacingOutline Rotated(SpacingOutline outline)
    {
        var vertices = outline.Perimeter.Vertices.Take(outline.Perimeter.Vertices.Count - 1).ToList();
        vertices.Add(vertices[0]);
        vertices.RemoveAt(0);
        return Outline(vertices.Select(v => (v.X, v.Y)).ToArray());
    }

    private static SpacingOutline Outline(params (double X, double Y)[] points) =>
        new(Loop(points), new List<Polygon>());

    private static Polygon Loop(params (double X, double Y)[] points)
    {
        var polygon = new Polygon();
        foreach (var (x, y) in points)
            polygon.Vertices.Add(new Vector(x, y));
        polygon.Close();
        polygon.UpdateBounds();
        return polygon;
    }
}
