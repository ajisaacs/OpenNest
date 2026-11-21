using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// The Strip layout's pack step (<see cref="PlateFiller.PackLeftovers"/>): leftovers go to the
/// remnants in <see cref="RemnantFinder"/> order, and a first remnant that holds none of them
/// (often a sliver along an edge, first because it touches the edge and is long) must not end
/// the search. The parts already on the plate are placed by hand here, so the remnants are known.
/// </summary>
public class StripLeftoverPackTests
{
    // Work area x 1..61, y 1..11.
    private static Plate MakePlate() =>
        new(new Size(12, 62)) { EdgeSpacing = new Spacing(1, 1, 1, 1), PartSpacing = 0.5 };

    private static Part Block(string name, double length, double width, double x, double y) =>
        new(new Drawing(name, TestDrawingFactory.Rectangle(length, width)), new Vector(x, y));

    /// <summary>
    /// Two blocks 8.5 high. They leave, in remnant order: a 60 x 1 sliver along the top edge, a
    /// 4.5 x 10 remnant at the right edge and a 4.25 x 10 gap between the blocks. Each of the two
    /// tall remnants holds at most three 4 x 2.5 rectangles at the 0.5 spacing.
    /// </summary>
    private static List<Part> TwoBlocks() =>
        [Block("block-a", 30, 8.5, 1, 1), Block("block-b", 19.75, 8.5, 36.25, 1)];

    private static List<Box> Remnants(Plate plate, List<Part> placed) =>
        new RemnantFinder(
            plate.WorkArea(),
            placed.Select(part => part.BoundingBox.Offset(plate.PartSpacing)).ToList()
        ).FindRemnants();

    private static PlateFiller StripFiller(Plate plate) =>
        new(plate, PlateFillPolicy.Fill, PlateLayout.Strips);

    private static List<Part> Pack(Plate plate, List<Part> placed, List<NestItem> items) =>
        StripFiller(plate).PackLeftovers(
            plate.WorkArea(),
            placed,
            items,
            Array.Empty<Box>(),
            null,
            CancellationToken.None
        );

    private static bool Inside(Box area, Part part)
    {
        const double slack = 1e-6;
        var box = part.BoundingBox;
        return box.Left >= area.Left - slack
            && box.Right <= area.Right + slack
            && box.Bottom >= area.Bottom - slack
            && box.Top <= area.Top + slack;
    }

    private static void AssertValid(Plate plate, List<Part> placed, List<Part> packed)
    {
        var all = placed.Concat(packed).ToList();
        var requirements = all
            .GroupBy(part => part.BaseDrawing)
            .ToDictionary(group => group.Key, group => (group.Key.Name, group.Count()));
        Assert.Empty(NestLayoutCheck.Validate(new List<(Plate, List<Part>)> { (plate, all) }, requirements));
    }

    private static string Poses(IEnumerable<Part> parts) =>
        string.Join(
            "\n",
            parts
                .Select(p => $"{p.BaseDrawing.Name}@({p.Location.X:F6},{p.Location.Y:F6},{p.Rotation:F6})")
                .Order(StringComparer.Ordinal)
        );

    [Fact]
    public void LeftoverThatDoesNotFitTheFirstRemnant_IsPackedInALaterOne()
    {
        var plate = MakePlate();
        var placed = TwoBlocks();
        var leftover = new Drawing("leftover", TestDrawingFactory.Rectangle(4, 2.5));
        var items = new List<NestItem> { new() { Drawing = leftover, Quantity = 1 } };

        var remnants = Remnants(plate, placed);
        // The fixture only tests the change while the first remnant is the sliver.
        Assert.Equal(1, remnants[0].Width, 6);
        Assert.Empty(StripFiller(plate).PackArea(remnants[0], items, null, CancellationToken.None));

        var packed = Pack(plate, placed, items);

        var part = Assert.Single(packed);
        Assert.Same(leftover, part.BaseDrawing);
        Assert.True(Inside(remnants[1], part), "the leftover belongs in the second remnant");
        Assert.Equal(1, items[0].Quantity);
        AssertValid(plate, placed, packed);
    }

    [Fact]
    public void LeftoversOneRemnantCannotHold_GoOnToTheNextRemnant()
    {
        var plate = MakePlate();
        var placed = TwoBlocks();
        var leftover = new Drawing("leftover", TestDrawingFactory.Rectangle(4, 2.5));
        var items = new List<NestItem> { new() { Drawing = leftover, Quantity = 5 } };
        var remnants = Remnants(plate, placed);

        var packed = Pack(plate, placed, items);

        Assert.Equal(5, packed.Count);
        Assert.Equal(3, packed.Count(part => Inside(remnants[1], part)));
        Assert.Equal(2, packed.Count(part => Inside(remnants[2], part)));
        AssertValid(plate, placed, packed);
    }

    [Fact]
    public void FirstRemnantThatHoldsEveryLeftover_PacksAsItAlwaysDid()
    {
        var plate = MakePlate();
        var placed = TwoBlocks();
        // 6 x 1 strips: all three lie in the top sliver, the first remnant.
        var leftover = new Drawing("leftover", TestDrawingFactory.Rectangle(6, 1));
        var items = new List<NestItem> { new() { Drawing = leftover, Quantity = 3 } };
        var first = Remnants(plate, placed)[0];
        var expected = StripFiller(plate).PackArea(first, items, null, CancellationToken.None);
        Assert.Equal(3, expected.Count);

        var packed = Pack(plate, placed, items);

        Assert.Equal(Poses(expected), Poses(packed));
        AssertValid(plate, placed, packed);
    }

    [Fact]
    public void EmptyPlate_PacksTheWholeWorkArea()
    {
        var plate = MakePlate();
        var leftover = new Drawing("leftover", TestDrawingFactory.Rectangle(4, 2.5));
        var items = new List<NestItem> { new() { Drawing = leftover, Quantity = 4 } };
        var expected = StripFiller(plate).PackArea(plate.WorkArea(), items, null, CancellationToken.None);

        var packed = Pack(plate, new List<Part>(), items);

        Assert.Equal(4, packed.Count);
        Assert.Equal(Poses(expected), Poses(packed));
    }

    [Fact]
    public void LeftoverNoRemnantHolds_IsLeftUnplaced()
    {
        var plate = MakePlate();
        var placed = TwoBlocks();
        var items = new List<NestItem>
        {
            new() { Drawing = new Drawing("too-big", TestDrawingFactory.Rectangle(8, 8)), Quantity = 2 },
        };

        Assert.Empty(Pack(plate, placed, items));
    }

    /// <summary>End to end: a filled strip leaves the single part no room in the first remnant.</summary>
    [Fact]
    public void StripNest_PacksTheSinglePartBesideAFilledStrip()
    {
        var plate = MakePlate();
        var fill = new Drawing("fill", TestDrawingFactory.Rectangle(5, 8.5));
        var single = new Drawing("single", TestDrawingFactory.Rectangle(4, 2.5));
        var items = new List<NestItem>
        {
            new() { Drawing = fill, Quantity = 10 },
            new() { Drawing = single, Quantity = 1 },
        };
        var requirements = items.ToDictionary(i => i.Drawing, i => (i.Drawing.Name, i.Quantity));

        var parts = StripFiller(plate).Nest(items, null, CancellationToken.None);

        Assert.Equal(10, parts.Count(part => ReferenceEquals(part.BaseDrawing, fill)));
        Assert.Single(parts, part => ReferenceEquals(part.BaseDrawing, single));
        Assert.All(items, item => Assert.Equal(0, item.Quantity));
        Assert.Empty(NestLayoutCheck.Validate(new List<(Plate, List<Part>)> { (plate, parts) }, requirements));
    }
}
