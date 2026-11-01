using OpenNest.Engine.CirclePacking;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.CirclePacking;

public class FillEndOverlapTests
{
    private const double Diameter = 10;

    public static IEnumerable<object[]> BinSizes()
    {
        for (var width = 1.0; width <= 6.0; width += 0.05)
        {
            foreach (var length in new[] { 1.0, 1.5, 1.95, 2.5, 2.9, 3.7, 6.0 })
                yield return new object[] { width * Diameter, length * Diameter };
        }
    }

    [Theory]
    [MemberData(nameof(BinSizes))]
    public void FillEndOdd_PlacesNoOverlappingCircles(double width, double length)
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(width, length) };

        new FillEndOdd(bin).Fill(CreateItem());

        Assert.NotEmpty(bin.Items);
        AssertNoOverlaps(bin.Items);
    }

    [Theory]
    [MemberData(nameof(BinSizes))]
    public void FillEndEven_PlacesNoOverlappingCircles(double width, double length)
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(width, length) };

        new FillEndEven(bin).Fill(CreateItem());

        Assert.NotEmpty(bin.Items);
        Assert.All(bin.Items, i => Assert.Equal(Diameter, i.Diameter, 9));
        AssertNoOverlaps(bin.Items);
    }

    [Fact]
    public void FillEndOdd_SingleCircleWidth_StacksAlongTheOtherAxis()
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(1.5 * Diameter, 1.5 * Diameter) };

        new FillEndOdd(bin).Fill(CreateItem());

        AssertNoOverlaps(bin.Items);
    }

    [Theory]
    [MemberData(nameof(BinSizes))]
    public void FillEnd_KeepsEveryCircleInsideTheBin(double width, double length)
    {
        var odd = new Bin { Location = new Vector(3, 7), Size = new Size(width, length) };
        var even = new Bin { Location = new Vector(3, 7), Size = new Size(width, length) };

        new FillEndOdd(odd).Fill(CreateItem());
        new FillEndEven(even).Fill(CreateItem());

        AssertInside(odd);
        AssertInside(even);
    }

    [Fact]
    public void FillEndOdd_StretchesStripesToTheBinOnALongSheet()
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(59.375, 119.75) };

        new FillEndOdd(bin).Fill(new Item { Radius = 4.125 });

        AssertNoOverlaps(bin.Items, 8.25);
        Assert.Equal(108, bin.Items.Count);
    }

    [Fact]
    public void FillEndEven_TriesTheStripesAlongTheLongSide()
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(59.375, 119.75) };

        new FillEndEven(bin).Fill(new Item { Radius = 4.125 });

        AssertNoOverlaps(bin.Items, 8.25);
        Assert.Equal(112, bin.Items.Count);
    }

    private static Item CreateItem() => new Item { Radius = Diameter * 0.5, Center = new Vector(Diameter * 0.5, Diameter * 0.5) };

    private static void AssertInside(Bin bin)
    {
        Assert.All(bin.Items, i =>
        {
            Assert.True(i.Center.X - i.Radius >= bin.Left - 1e-6 && i.Center.X + i.Radius <= bin.Right + 1e-6, $"{i.Center} leaves {bin}");
            Assert.True(i.Center.Y - i.Radius >= bin.Bottom - 1e-6 && i.Center.Y + i.Radius <= bin.Top + 1e-6, $"{i.Center} leaves {bin}");
        });
    }

    private static void AssertNoOverlaps(IReadOnlyList<Item> items, double diameter = Diameter)
    {
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var distance = items[i].Center.DistanceTo(items[j].Center);
                Assert.True(
                    distance >= diameter - 1e-6,
                    $"Circles {i} at {items[i].Center} and {j} at {items[j].Center} are {distance:F4} apart (< {diameter}).");
            }
        }
    }
}
