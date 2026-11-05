using OpenNest.Engine.CirclePacking;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.CirclePacking;

public class StaggeredFillTests
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
    public void HalfPitch_PlacesNoOverlappingCircles(double width, double length)
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(width, length) };

        StaggeredFill.Fill(bin, CreateItem(), StaggerMode.HalfPitch);

        Assert.NotEmpty(bin.Items);
        AssertNoOverlaps(bin.Items);
    }

    [Theory]
    [MemberData(nameof(BinSizes))]
    public void SlackShift_PlacesNoOverlappingCircles(double width, double length)
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(width, length) };

        StaggeredFill.Fill(bin, CreateItem(), StaggerMode.SlackShift);

        Assert.NotEmpty(bin.Items);
        Assert.All(bin.Items, i => Assert.Equal(Diameter, i.Diameter, 9));
        AssertNoOverlaps(bin.Items);
    }

    [Fact]
    public void HalfPitch_SingleCircleWidth_StacksAlongTheOtherAxis()
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(1.5 * Diameter, 1.5 * Diameter) };

        StaggeredFill.Fill(bin, CreateItem(), StaggerMode.HalfPitch);

        AssertNoOverlaps(bin.Items);
    }

    [Theory]
    [MemberData(nameof(BinSizes))]
    public void StaggeredFill_KeepsEveryCircleInsideTheBin(double width, double length)
    {
        var odd = new Bin { Location = new Vector(3, 7), Size = new Size(width, length) };
        var even = new Bin { Location = new Vector(3, 7), Size = new Size(width, length) };

        StaggeredFill.Fill(odd, CreateItem(), StaggerMode.HalfPitch);
        StaggeredFill.Fill(even, CreateItem(), StaggerMode.SlackShift);

        AssertInside(odd);
        AssertInside(even);
    }

    [Fact]
    public void HalfPitch_StretchesStripesToTheBinOnALongSheet()
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(59.375, 119.75) };

        StaggeredFill.Fill(bin, new Item { Radius = 4.125 }, StaggerMode.HalfPitch);

        // Staggered all the way places 108; the last stripes stacked straight fit one more.
        AssertNoOverlaps(bin.Items, 8.25);
        AssertInside(bin);
        Assert.Equal(109, bin.Items.Count);
    }

    [Fact]
    public void SlackShift_TriesTheStripesAlongTheLongSide()
    {
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(59.375, 119.75) };

        StaggeredFill.Fill(bin, new Item { Radius = 4.125 }, StaggerMode.SlackShift);

        AssertNoOverlaps(bin.Items, 8.25);
        Assert.Equal(112, bin.Items.Count);
    }

    [Fact]
    public void HalfPitch_EndsWithStraightStripesWhenTheyFitMore()
    {
        // 13 discs to a stripe: seven staggered stripes place 88 and six straight ones 78, but
        // five staggered and two straight over the fifth place 89.
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(59.8, 119.8) };

        StaggeredFill.Fill(bin, new Item { Radius = 4.6 }, StaggerMode.HalfPitch);

        AssertNoOverlaps(bin.Items, 9.2);
        AssertInside(bin);
        Assert.Equal(89, bin.Items.Count);
        var stripes = bin.Items.GroupBy(i => System.Math.Round(i.Center.Y, 6)).OrderBy(g => g.Key).ToList();
        Assert.Equal(new[] { 13, 12, 13, 12, 13, 13, 13 }, stripes.Select(s => s.Count()));
        var fifth = stripes[4].Select(i => i.Center.X).Order().ToList();
        for (var stripe = 5; stripe < stripes.Count; stripe++)
        {
            Assert.Equal(9.2, stripes[stripe].Key - stripes[stripe - 1].Key, 6);
            Assert.Equal(fifth, stripes[stripe].Select(i => i.Center.X).Order());
        }
    }

    [Fact]
    public void HalfPitch_KeepsStaggeredStripesWhenStraightOnesFitNoMore()
    {
        // Seven staggered stripes place 88; too short for the mixed 89, straight stripes from
        // any switch point place at most 78.
        var bin = new Bin { Location = new Vector(0, 0), Size = new Size(57.2, 119.8) };

        StaggeredFill.Fill(bin, new Item { Radius = 4.6 }, StaggerMode.HalfPitch);

        Assert.Equal(88, bin.Items.Count);
        var stripes = bin.Items.GroupBy(i => System.Math.Round(i.Center.Y, 6)).Select(g => g.Count());
        Assert.Equal(new[] { 13, 12, 13, 12, 13, 12, 13 }, stripes);
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
