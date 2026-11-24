using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

/// <summary>A part seated in another part's cutout is answered without the triangle test; the
/// answer must be the reference kernel's for every pose, seated or not.</summary>
public class SpacingOutlineCutoutTests
{
    private static Polygon Ngon(double x, double y, double radius, int sides, bool closed = true)
    {
        var polygon = new Polygon();
        for (var i = 0; i < sides; i++)
        {
            var angle = 2 * System.Math.PI * i / sides;
            polygon.Vertices.Add(new Vector(x + radius * System.Math.Cos(angle), y + radius * System.Math.Sin(angle)));
        }
        if (closed)
            polygon.Vertices.Add(polygon.Vertices[0]);
        polygon.UpdateBounds();
        return polygon;
    }

    private static Polygon Path(params (double X, double Y)[] points)
    {
        var polygon = new Polygon();
        foreach (var (x, y) in points)
            polygon.Vertices.Add(new Vector(x, y));
        polygon.Vertices.Add(polygon.Vertices[0]);
        polygon.UpdateBounds();
        return polygon;
    }

    private static bool Reference(SpacingOutline a, SpacingOutline b) =>
        Collision.HasOverlap(a.Perimeter, b.Perimeter, a.Holes, b.Holes);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RingInABoreMatchesTheReferenceAtEveryOffset(bool closed)
    {
        var frame = new SpacingOutline(Ngon(0, 0, 10, 64, closed), new List<Polygon> { Ngon(0, 0, 6, 48, closed) });
        var random = new Random(5826);
        var clear = 0;
        var overlapping = 0;
        for (var sample = 0; sample < 400; sample++)
        {
            var radius = 1 + random.NextDouble() * 4.5;
            var x = random.NextDouble() * 14 - 7;
            var y = random.NextDouble() * 14 - 7;
            var insert = new SpacingOutline(Ngon(x, y, radius, 40, closed),
                new List<Polygon> { Ngon(x, y, radius / 2, 24, closed) });
            var expected = Reference(insert, frame);

            Assert.Equal(expected, insert.Overlaps(frame));
            Assert.Equal(expected, frame.Overlaps(insert));
            if (expected)
                overlapping++;
            else
                clear++;
        }
        Assert.True(clear > 20 && overlapping > 20, $"{clear} clear, {overlapping} overlapping");
    }

    [Fact]
    public void SeatedDiscIsClear()
    {
        var frame = new SpacingOutline(Ngon(0, 0, 10, 64), new List<Polygon> { Ngon(0, 0, 6, 48) });
        var disc = new SpacingOutline(Ngon(0.5, -0.5, 5, 40), new List<Polygon>());

        Assert.False(disc.Overlaps(frame));
        Assert.False(frame.Overlaps(disc));
    }

    [Fact]
    public void OutlineWithEveryVertexInAConcaveCutoutButAnEdgeAcrossItsNotchOverlaps()
    {
        // A U-shaped cutout: the bar's corners sit in the two arms, its middle crosses the
        // material between them.
        var cutout = Path((1, 1), (9, 1), (9, 9), (6, 9), (6, 3), (4, 3), (4, 9), (1, 9));
        var frame = new SpacingOutline(Path((0, 0), (10, 0), (10, 10), (0, 10)), new List<Polygon> { cutout });
        var bar = new SpacingOutline(Path((2, 6), (8, 6), (8, 7), (2, 7)), new List<Polygon>());

        Assert.True(Reference(bar, frame));
        Assert.True(bar.Overlaps(frame));
        Assert.True(frame.Overlaps(bar));
    }

    [Fact]
    public void OutlineInsideAConcaveCutoutIsClear()
    {
        var cutout = Path((1, 1), (9, 1), (9, 9), (6, 9), (6, 3), (4, 3), (4, 9), (1, 9));
        var frame = new SpacingOutline(Path((0, 0), (10, 0), (10, 10), (0, 10)), new List<Polygon> { cutout });
        var bar = new SpacingOutline(Path((2, 1.5), (8, 1.5), (8, 2.5), (2, 2.5)), new List<Polygon>());

        Assert.False(Reference(bar, frame));
        Assert.False(bar.Overlaps(frame));
        Assert.False(frame.Overlaps(bar));
    }
}
