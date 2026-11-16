using OpenNest.Engine.RectanglePacking;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.RectanglePacking;

public class RectFillTests
{
    private const double Eps = 1e-9;

    // 11.8 x 3.8 rectangles at 0.25 spacing on a 60 x 120 sheet whose work area starts above
    // the clamp rails (0.25 edge + 0.625 rail). Packed tight to the origin, the spiral leaves a
    // 3.55 wide center gap a 4.05 part cannot use while 2.8 of slack sits at the far edge;
    // moving the far blocks out by 0.5 fits one more part in the center.
    [Fact]
    public void FillBest_SpreadsTheSpiralWhenSlackFitsAnotherPartInTheCenter()
    {
        const double spacing = 0.25;
        var bin = new Bin { Location = new Vector(0.25, 0.875), Size = new Size(58.875 + spacing, 119.5 + spacing) };
        var item = new Item { Size = new Size(3.8 + spacing, 11.8 + spacing) };

        RectFill.FillBest(bin, item);

        Assert.Equal(135, bin.Items.Count);
        AssertInsideAndApart(bin);
    }

    private static void AssertInsideAndApart(Bin bin)
    {
        foreach (var it in bin.Items)
            Assert.True(
                it.Left >= bin.Left - Eps && it.Bottom >= bin.Bottom - Eps
                    && it.Right <= bin.Right + Eps && it.Top <= bin.Top + Eps,
                $"item at ({it.Left},{it.Bottom})-({it.Right},{it.Top}) is outside the bin"
            );

        for (var i = 0; i < bin.Items.Count; i++)
            for (var j = i + 1; j < bin.Items.Count; j++)
            {
                var a = bin.Items[i];
                var b = bin.Items[j];
                var gapX = System.Math.Max(b.Left - a.Right, a.Left - b.Right);
                var gapY = System.Math.Max(b.Bottom - a.Top, a.Bottom - b.Top);
                Assert.True(System.Math.Max(gapX, gapY) >= -Eps, $"items {i} and {j} overlap");
            }
    }
}
