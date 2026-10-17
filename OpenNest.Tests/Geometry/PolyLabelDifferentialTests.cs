using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// The heap-queue PolyLabel must return bit-identical poles to the former sorted-list
/// implementation (frozen in <see cref="LegacyPolyLabel"/>) for every unbounded call, so
/// existing label positions in PlateView and report diagrams do not move.
/// </summary>
public class PolyLabelDifferentialTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 40).Select(seed => new object[] { seed });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Find_MatchesTheSortedListImplementationBitForBit(int seed)
    {
        var random = new Random(seed);
        for (var round = 0; round < 15; round++)
        {
            var (outer, holes) = RandomShape(random);
            var box = Bounds(outer);
            foreach (var precision in new[] { 0.5, System.Math.Max(box.Length, box.Width) * 0.005, 1e-3 })
            {
                var expected = LegacyPolyLabel.Find(outer, holes, precision);
                var actual = PolyLabel.Find(outer, holes, precision);
                Assert.True(Same(expected, actual),
                    $"seed {seed} round {round} precision {precision}: legacy {expected.X:R},{expected.Y:R} "
                    + $"heap {actual.X:R},{actual.Y:R}");
            }
        }
    }

    [Fact]
    public void Find_SymmetricShapesWithTiedPolesMatchTheSortedList()
    {
        var shapes = new List<(Polygon, List<Polygon>)>
        {
            (Rectangle(0, 0, 10, 10), null),
            (Rectangle(0, 0, 10, 10), new List<Polygon> { Rectangle(4, 4, 2, 2) }),
            (Rectangle(0, 0, 30, 10), new List<Polygon> { Rectangle(9, 4, 2, 2), Rectangle(19, 4, 2, 2) }),
            (Regular(12, 5), null),
            (Regular(4, 100), null),
            (Rectangle(-1e6, -1e6, 3, 1), null),
        };
        foreach (var (outer, holes) in shapes)
            foreach (var precision in new[] { 0.5, 0.01, 1e-4 })
                Assert.True(Same(LegacyPolyLabel.Find(outer, holes, precision), PolyLabel.Find(outer, holes, precision)));
    }

    [Fact]
    public void Find_WithBudgetStaysWithinItOnALongSliverAndLandsInside()
    {
        // 48 x 0.002: the unbounded search refines along the whole flat ridge (seconds).
        var sliver = Rectangle(0, 0, 48, 0.002);

        var pole = PolyLabel.Find(sliver, null, 1e-4, 4096, out var evaluated);

        Assert.InRange(evaluated, 1, 4096);
        Assert.True(sliver.ContainsPoint(pole), $"{pole.X:R},{pole.Y:R}");
        Assert.InRange(pole.Y, 0.0005, 0.0015);
    }

    [Fact]
    public void Find_UnboundedOverloadMatchesTheDefault()
    {
        var shape = Rectangle(0, 0, 200, 10);
        Assert.True(Same(PolyLabel.Find(shape), PolyLabel.Find(shape, null, 0.5, int.MaxValue)));
    }

    // Quality review: at 1e16 a 1-unit grid step no longer changes x (1e16 + 1 == 1e16), and
    // the starting grid loop never ended, allocating cells without limit even with a budget.
    [Fact]
    public async Task Find_FinishesWhenGridStepsStopAdvancingFarFromTheOrigin()
    {
        var far = Rectangle(1e16, 0, 8, 1);

        var search = Task.Run(() => (PolyLabel.Find(far, null, 0.01, 4096), PolyLabel.Find(far, null, 0.01)));

        Assert.Same(search, await Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(20))));
        var (bounded, unbounded) = await search;
        Assert.InRange(bounded.Y, 0, 1);
        Assert.InRange(unbounded.Y, 0, 1);
    }

    // Delta review: past the grid, refining a cell whose half-step no longer moves its centre
    // made four identical children per level, so a fine unbounded search exhausted memory.
    [Fact]
    public async Task Find_UnboundedRefinementFinishesWhenSplitsStopAdvancing()
    {
        var far = Rectangle(1e16, 0, 8, 1);

        var search = Task.Run(() => PolyLabel.Find(far, null, 1e-6));

        Assert.Same(search, await Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(20))));
        Assert.InRange((await search).Y, 0, 1);
    }

    private static (Polygon outer, List<Polygon> holes) RandomShape(Random random)
    {
        var scale = System.Math.Pow(10, random.NextDouble() * 4 - 1);
        var offset = new Vector((random.NextDouble() - 0.5) * 2e4, (random.NextDouble() - 0.5) * 2e4);
        if (random.Next(3) == 0)
        {
            // Square with a few disjoint square holes on a 4x4 grid of slots.
            var outer = Rectangle(offset.X, offset.Y, 4 * scale, 4 * scale);
            var holes = new List<Polygon>();
            foreach (var slot in Enumerable.Range(0, 16).OrderBy(_ => random.Next()).Take(random.Next(4)))
            {
                var size = scale * (0.2 + 0.6 * random.NextDouble());
                holes.Add(Rectangle(offset.X + (slot % 4 + 0.1) * scale, offset.Y + (slot / 4 + 0.1) * scale,
                    size, size));
            }
            return (outer, holes);
        }

        // Star-shaped polygon: sorted angles, random radii; sometimes very elongated.
        var count = random.Next(3, 40);
        var stretch = random.Next(4) == 0 ? 20 + random.NextDouble() * 100 : 1;
        var angles = Enumerable.Range(0, count).Select(_ => random.NextDouble() * 2 * System.Math.PI)
            .OrderBy(a => a).ToArray();
        var polygon = new Polygon();
        foreach (var angle in angles)
        {
            var radius = scale * (0.2 + 0.8 * random.NextDouble());
            polygon.Vertices.Add(new Vector(offset.X + radius * System.Math.Cos(angle) * stretch,
                offset.Y + radius * System.Math.Sin(angle)));
        }
        return (polygon, null);
    }

    private static Box Bounds(Polygon polygon)
    {
        var left = polygon.Vertices.Min(v => v.X);
        var bottom = polygon.Vertices.Min(v => v.Y);
        return new Box(left, bottom, polygon.Vertices.Max(v => v.X) - left, polygon.Vertices.Max(v => v.Y) - bottom);
    }

    private static Polygon Rectangle(double x, double y, double width, double height)
    {
        var polygon = new Polygon();
        polygon.Vertices.Add(new Vector(x, y));
        polygon.Vertices.Add(new Vector(x + width, y));
        polygon.Vertices.Add(new Vector(x + width, y + height));
        polygon.Vertices.Add(new Vector(x, y + height));
        return polygon;
    }

    private static Polygon Regular(int sides, double radius)
    {
        var polygon = new Polygon();
        for (var i = 0; i < sides; i++)
        {
            var angle = 2 * System.Math.PI * i / sides;
            polygon.Vertices.Add(new Vector(radius * System.Math.Cos(angle), radius * System.Math.Sin(angle)));
        }
        return polygon;
    }

    private static bool Same(Vector a, Vector b) =>
        BitConverter.DoubleToInt64Bits(a.X) == BitConverter.DoubleToInt64Bits(b.X)
        && BitConverter.DoubleToInt64Bits(a.Y) == BitConverter.DoubleToInt64Bits(b.Y);
}
