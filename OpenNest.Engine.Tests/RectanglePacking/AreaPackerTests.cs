using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.NestingEngines.Rectangles;
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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    public void PackArea_AllowsBoundaryOverhangAndValidates(double spacing, double edge)
    {
        var plate = new Plate(new Size(10 + 2 * edge, 10 + 2 * edge))
        {
            PartSpacing = spacing,
            EdgeSpacing = new Spacing(edge, edge),
        };
        var area = plate.WorkArea();
        var items = new List<NestItem> { Item("panel", 10 + SheetPacker.OverhangAllowance * 0.8, 4, 2) };

        var parts = PlateFillService.PackArea("Default", plate, area, items, null, CancellationToken.None);
        var again = PlateFillService.PackArea("Default", plate, area, items, null, CancellationToken.None);

        Assert.Equal(2, parts.Count);
        Assert.Equal(parts.Select(p => (p.Location, p.Rotation)), again.Select(p => (p.Location, p.Rotation)));
        Assert.All(parts, p =>
        {
            Assert.Equal(area.Left, p.BoundingBox.Left, 9);
            Assert.True(p.BoundingBox.Right > area.Right);
            Assert.True(p.BoundingBox.Right <= area.Right + NestTolerances.WorkAreaSlack);
        });
        Assert.True(parts[1].BoundingBox.Bottom - parts[0].BoundingBox.Top >= spacing - Eps);
        var requirements = items.ToDictionary(i => i.Drawing, i => (i.Drawing.Name, i.Quantity));
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) }, requirements));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PackArea_TopOverhangKeepsSpacingInEveryQuadrant(int quadrant)
    {
        var plate = new Plate(new Size(12, 12))
        {
            Quadrant = quadrant,
            EdgeSpacing = new Spacing(1, 1),
            PartSpacing = 1,
        };
        var area = plate.WorkArea();
        var items = new List<NestItem> { Item("panel", 4, 10 + SheetPacker.OverhangAllowance * 0.8, 2) };
        var parts = PlateFillService.PackArea("Default", plate, area, items, null, CancellationToken.None);

        Assert.Equal(2, parts.Count);
        Assert.All(parts, p =>
        {
            Assert.Equal(area.Bottom, p.BoundingBox.Bottom, 9);
            Assert.True(p.BoundingBox.Top > area.Top);
            Assert.True(p.BoundingBox.Top <= area.Top + NestTolerances.WorkAreaSlack);
        });
        var ordered = parts.OrderBy(p => p.BoundingBox.Left).ToList();
        Assert.True(ordered[1].BoundingBox.Left - ordered[0].BoundingBox.Right >= 1 - Eps);
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) },
            items.ToDictionary(i => i.Drawing, i => (i.Drawing.Name, i.Quantity))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PackArea_FullAreaDemandCapIncludesSlack(bool rotated)
    {
        var plate = new Plate(new Size(6, 10)) { PartSpacing = 0 };
        var excess = SheetPacker.OverhangAllowance * 0.8;
        var items = new List<NestItem> { rotated ? Item("panel", 6 + excess, 10 + excess, 1)
            : Item("panel", 10 + excess, 6 + excess, 1) };

        var part = Assert.Single(PlateFillService.PackArea("Default", plate, plate.WorkArea(), items, null, CancellationToken.None));

        Assert.True(part.BoundingBox.Right <= 10 + NestTolerances.WorkAreaSlack);
        Assert.True(part.BoundingBox.Top <= 6 + NestTolerances.WorkAreaSlack);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PackArea_RejectsBeyondBoundaryAllowance(double spacing)
    {
        var plate = new Plate(new Size(6, 10)) { PartSpacing = spacing };
        var items = new List<NestItem> { Item("panel", 10 + NestTolerances.WorkAreaSlack * 1.1, 4, 1) };
        Assert.Empty(PlateFillService.PackArea("Default", plate, plate.WorkArea(), items, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 0, 5, 6, false)]
    [InlineData(5, 0, 5, 6, true)]
    [InlineData(0, 0, 10, 3, false)]
    [InlineData(0, 3, 10, 3, true)]
    public void PackArea_OnlyMatchingPositivePlateEdgesAllowSlack(double x, double y, double w, double h, bool fits)
    {
        var plate = new Plate(new Size(6, 10)) { PartSpacing = 0.5 };
        var area = new Box(x, y, w, h);
        var excess = SheetPacker.OverhangAllowance * 0.8;
        var items = new List<NestItem> { w == 5 ? Item("panel", w + excess, h - 0.25, 1) : Item("panel", 8, h + excess, 1) };
        var parts = PlateFillService.PackArea("Default", plate, area, items, null, CancellationToken.None);
        Assert.Equal(fits ? 1 : 0, parts.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MaxRects_InternalFreeEdgesStayStrict(bool horizontal)
    {
        var sheet = new MaxRectsSheet(10, 10, SheetPacker.OverhangAllowance, SheetPacker.OverhangAllowance);
        sheet.Place(horizontal ? new Rect(5, 0, 5, 10) : new Rect(0, 5, 10, 5));
        var excess = SheetPacker.OverhangAllowance * 0.8;
        Assert.Null(sheet.FindBest(horizontal ? 5 + excess : 10, horizontal ? 10 : 5 + excess, FitRule.BottomLeft));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MaxRects_BoundaryFreeEdgesAllowSlackAfterPlacement(bool horizontal)
    {
        var sheet = new MaxRectsSheet(10, 10, SheetPacker.OverhangAllowance, SheetPacker.OverhangAllowance);
        sheet.Place(horizontal ? new Rect(0, 0, 5, 10) : new Rect(0, 0, 10, 5));
        var excess = SheetPacker.OverhangAllowance * 0.8;
        var fit = sheet.FindBest(horizontal ? 5 + excess : 10, horizontal ? 10 : 5 + excess, FitRule.BottomLeft);
        Assert.NotNull(fit);
        Assert.Equal(horizontal ? new Rect(5, 0, 5, 10) : new Rect(0, 5, 10, 5), fit.Value.Place);
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
