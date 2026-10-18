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

    private static Item CreateItem() => new Item { Radius = Diameter * 0.5, Center = new Vector(Diameter * 0.5, Diameter * 0.5) };

    private static void AssertNoOverlaps(IReadOnlyList<Item> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                var distance = items[i].Center.DistanceTo(items[j].Center);
                Assert.True(
                    distance >= Diameter - 1e-6,
                    $"Circles {i} at {items[i].Center} and {j} at {items[j].Center} are {distance:F4} apart (< {Diameter}).");
            }
        }
    }
}
