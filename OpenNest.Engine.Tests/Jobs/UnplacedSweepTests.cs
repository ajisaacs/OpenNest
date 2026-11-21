using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// The last step of <see cref="PlateFillOrchestrator.Nest"/> (Lattice and both Remnant
/// strategies): whatever the fills and the one-remnant pack left unplaced is offered to every
/// remnant of the layout. The fill is a stand-in that lays a known row, so the remnants are known:
/// a 60 x 1 sliver along the top edge first, then a 5 x 10 remnant at the right edge.
/// </summary>
public class UnplacedSweepTests
{
    // Work area x 1..61, y 1..11.
    private static Plate MakePlate() =>
        new(new Size(12, 62)) { EdgeSpacing = new Spacing(1, 1, 1, 1), PartSpacing = 0.5 };

    /// <summary>Ten 5 x 8.5 parts in a row from the origin corner on the first call, then nothing.</summary>
    private static Func<NestItem, Box, IProgress<NestProgress>, CancellationToken, List<Part>> RowOfTen()
    {
        var filled = false;
        return (item, _, _, _) =>
        {
            if (filled)
                return new List<Part>();
            filled = true;
            return Enumerable
                .Range(0, 10)
                .Select(i => new Part(item.Drawing, new Vector(1 + i * 5.5, 1)))
                .ToList();
        };
    }

    private static List<Part> Nest(Plate plate, List<NestItem> items) =>
        PlateFillOrchestrator.Nest(
            plate,
            items,
            new DefaultFillComparer(),
            RowOfTen(),
            (area, packItems, progress, token) =>
                new PlateFiller(plate).PackArea(area, packItems, progress, token),
            null,
            CancellationToken.None
        );

    private static void AssertValid(Plate plate, List<Part> parts, Dictionary<Drawing, (string, int)> demand) =>
        Assert.Empty(NestLayoutCheck.Validate(new List<(Plate, List<Part>)> { (plate, parts) }, demand));

    [Fact]
    public void FillItemLeftShort_IsPackedIntoARemnantThatHoldsIt()
    {
        var plate = MakePlate();
        var fill = new Drawing("fill", TestDrawingFactory.Rectangle(5, 8.5));
        var items = new List<NestItem> { new() { Drawing = fill, Quantity = 12 } };
        var demand = items.ToDictionary(i => i.Drawing, i => (i.Drawing.Name, i.Quantity));

        var parts = Nest(plate, items);

        // The row holds ten; the right remnant has room for one more box and nothing holds a twelfth.
        Assert.Equal(11, parts.Count);
        Assert.Equal(1, items[0].Quantity);
        Assert.Contains(parts, part => part.BoundingBox.Left >= 56 - 1e-6);
        AssertValid(plate, parts, demand);
    }

    [Fact]
    public void PackItemTheFirstRemnantCannotHold_IsPackedIntoALaterOne()
    {
        var plate = MakePlate();
        var fill = new Drawing("fill", TestDrawingFactory.Rectangle(5, 8.5));
        var single = new Drawing("single", TestDrawingFactory.Rectangle(4, 2.5));
        var items = new List<NestItem>
        {
            new() { Drawing = fill, Quantity = 10 },
            new() { Drawing = single, Quantity = 1 },
        };
        var demand = items.ToDictionary(i => i.Drawing, i => (i.Drawing.Name, i.Quantity));

        var parts = Nest(plate, items);

        Assert.Equal(11, parts.Count);
        Assert.Single(parts, part => ReferenceEquals(part.BaseDrawing, single));
        Assert.All(items, item => Assert.Equal(0, item.Quantity));
        AssertValid(plate, parts, demand);
    }

    [Fact]
    public void UnplacedItemNoRemnantHolds_StaysUnplaced()
    {
        var plate = MakePlate();
        var fill = new Drawing("fill", TestDrawingFactory.Rectangle(5, 8.5));
        var wide = new Drawing("wide", TestDrawingFactory.Rectangle(7, 7));
        var items = new List<NestItem>
        {
            new() { Drawing = fill, Quantity = 10 },
            new() { Drawing = wide, Quantity = 1 },
        };

        var parts = Nest(plate, items);

        Assert.Equal(10, parts.Count);
        Assert.Equal(1, items[1].Quantity);
    }
}
