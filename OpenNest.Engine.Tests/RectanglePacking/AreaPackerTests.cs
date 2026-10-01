using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.RectanglePacking;
using OpenNest.Engine.Tests.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.RectanglePacking;

public class AreaPackerTests
{
    private const double Eps = 1e-9;

    private static NestItem Item(string name, double width, double length, int quantity, int priority = 0) =>
        new()
        {
            Drawing = new Drawing(name, TestDrawingFactory.Rectangle(width, length)),
            Quantity = quantity,
            Priority = priority,
        };

    private static void AssertInsideAndSpaced(IReadOnlyList<Part> parts, Box area, double spacing)
    {
        foreach (var part in parts)
        {
            var b = part.BoundingBox;
            Assert.True(
                b.Left >= area.Left - Eps && b.Bottom >= area.Bottom - Eps
                    && b.Right <= area.Right + Eps && b.Top <= area.Top + Eps,
                $"{part.BaseDrawing.Name} at ({b.Left},{b.Bottom})-({b.Right},{b.Top}) is outside the area"
            );
        }

        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var a = parts[i].BoundingBox;
                var b = parts[j].BoundingBox;
                var gapX = System.Math.Max(b.Left - a.Right, a.Left - b.Right);
                var gapY = System.Math.Max(b.Bottom - a.Top, a.Bottom - b.Top);
                Assert.True(
                    System.Math.Max(gapX, gapY) >= spacing - Eps,
                    $"parts {i} and {j} are {System.Math.Max(gapX, gapY)} apart, spacing is {spacing}"
                );
            }
    }

    [Fact]
    public void DefaultPackArea_PlacesDemandThatCornerPointPackingMissed()
    {
        // Three 5x7 and two 4x3 boxes fit a 12x12 area exactly when packed by free space;
        // packing only at placed parts' corners left one 4x3 out.
        var plate = new Plate(new Size(12, 12)) { PartSpacing = 0 };
        var area = new Box(0, 0, 12, 12);
        var items = new List<NestItem> { Item("tall", 5, 7, 3), Item("small", 4, 3, 2) };

        var parts = PlateFillService.PackArea("Default", plate, area, items, null, CancellationToken.None);

        Assert.Equal(5, parts.Count);
        Assert.Equal(3, parts.Count(p => p.BaseDrawing.Name == "tall"));
        Assert.Equal(2, parts.Count(p => p.BaseDrawing.Name == "small"));
        AssertInsideAndSpaced(parts, area, 0);
    }

    [Fact]
    public void Pack_KeepsSpacingInsideTranslatedArea()
    {
        var area = new Box(10, 20, 30, 20);
        var items = new List<NestItem> { Item("a", 6, 4, 5), Item("b", 4, 3, 4) };

        var parts = AreaPacker.Pack(area, items, 1, new DefaultFillComparer(), CancellationToken.None);

        Assert.Equal(9, parts.Count);
        AssertInsideAndSpaced(parts, area, 1);
    }

    [Fact]
    public void Pack_TurnsPartThatOnlyFitsRotated()
    {
        // 20 long in X, but the area is only 10 wide in X and 30 tall.
        var area = new Box(0, 0, 10, 30);
        var items = new List<NestItem> { Item("long", 20, 5, 1) };

        var parts = AreaPacker.Pack(area, items, 0, new DefaultFillComparer(), CancellationToken.None);

        var part = Assert.Single(parts);
        Assert.Equal(System.Math.PI / 2, part.Rotation, 9);
        AssertInsideAndSpaced(parts, area, 0);
    }

    [Fact]
    public void Pack_ServesLowerPriorityNumberFirst()
    {
        var area = new Box(0, 0, 10, 10);
        var items = new List<NestItem> { Item("later", 10, 10, 1, priority: 1), Item("first", 6, 6, 1, priority: 0) };

        var parts = AreaPacker.Pack(area, items, 0, new DefaultFillComparer(), CancellationToken.None);

        Assert.Equal("first", Assert.Single(parts).BaseDrawing.Name);
    }

    [Fact]
    public void Pack_NeverExceedsQuantityAndSkipsEmptyDrawings()
    {
        var area = new Box(0, 0, 100, 100);
        var items = new List<NestItem>
        {
            Item("two", 3, 3, 2),
            new() { Drawing = new Drawing("empty", new OpenNest.CNC.Program()), Quantity = 4 },
        };

        var parts = AreaPacker.Pack(area, items, 0.5, new DefaultFillComparer(), CancellationToken.None);

        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal("two", p.BaseDrawing.Name));
    }

    [Fact]
    public void Pack_CancelledTokenStillReturnsFirstValidLayout()
    {
        var area = new Box(0, 0, 30, 20);
        var items = new List<NestItem> { Item("a", 6, 4, 5), Item("b", 4, 3, 4) };

        var parts = AreaPacker.Pack(area, items, 1, new DefaultFillComparer(), new CancellationToken(canceled: true));

        Assert.NotEmpty(parts);
        AssertInsideAndSpaced(parts, area, 1);
    }

    [Fact]
    public void Pack_LetsStrategyComparerChooseBetweenEqualLayouts()
    {
        // Two 60x2 strips fit stacked or end to end. Both place everything, so the strategy decides:
        // Vertical Remnant keeps the right side clear, Horizontal Remnant keeps the top clear.
        var area = new Box(0, 0, 130, 30);
        var items = new List<NestItem> { Item("a", 60, 2, 1), Item("b", 60, 2, 1) };

        var vertical = AreaPacker.Pack(area, items, 0, new VerticalRemnantComparer(), CancellationToken.None);
        var horizontal = AreaPacker.Pack(area, items, 0, new HorizontalRemnantComparer(), CancellationToken.None);

        Assert.Equal(2, vertical.Count);
        Assert.Equal(60, vertical.Max(p => p.BoundingBox.Right) - vertical.Min(p => p.BoundingBox.Left), 9);
        Assert.Equal(2, horizontal.Count);
        Assert.Equal(2, horizontal.Max(p => p.BoundingBox.Top) - horizontal.Min(p => p.BoundingBox.Bottom), 9);
    }
}
