using System;
using OpenNest.Geometry;
using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests.Geometry;

/// <summary>
/// The shared box and rotation helpers replaced private copies in the splitter, maximal
/// rectangles, nested-part review, packers and aligner. The frozen copies below are those
/// donors' bodies; the shared helpers must return the same bits.
/// </summary>
public class BoundingBoxHelperTests
{
    private const double Eps = Tolerance.Epsilon;

    private static readonly Box Outer = new(0, 0, 10, 10);

    [Theory]
    [InlineData(-1, 0, 11, 10)] // left
    [InlineData(0, 0, 11, 10)] // right
    [InlineData(0, -1, 10, 11)] // bottom
    [InlineData(0, 0, 10, 11)] // top
    public void IsWithin_AllowsEpsilonOnEachSideButNotMore(double x, double y, double w, double h)
    {
        Box Grown(double d) => new(x * d, y * d, 10 + (w - 10) * d, 10 + (h - 10) * d);

        Assert.True(BoundingBox.IsWithin(Grown(Eps), Outer));
        Assert.False(BoundingBox.IsWithin(Grown(2 * Eps), Outer));
    }

    [Fact]
    public void IsWithin_ArgumentOrderIsInnerThenOuter()
    {
        var inner = new Box(2, 2, 3, 3);

        Assert.True(BoundingBox.IsWithin(inner, Outer));
        Assert.False(BoundingBox.IsWithin(Outer, inner));
    }

    [Fact]
    public void Helpers_MatchFrozenDonorsBitForBit()
    {
        var random = new Random(20261101);
        for (var i = 0; i < 2000; i++)
        {
            var a = RandomBox(random);
            var b = RandomBox(random);

            Assert.Equal(FrozenIsContainedIn(a, b), BoundingBox.IsWithin(a, b));
            AssertSameBits(FrozenUnion(a, b), BoundingBox.Union(a, b));

            var v = new Vector(Value(random), Value(random));
            var angle = (random.NextDouble() - 0.5) * 100;
            var expected = FrozenRotate(v, angle);
            var actual = v.Rotate(angle);
            AssertSameBits(expected.X, actual.X);
            AssertSameBits(expected.Y, actual.Y);
        }
    }

    [Fact]
    public void Union_PropagatesNaNLikeTheDonors()
    {
        var a = new Box(double.NaN, 0, 1, 1);
        var b = new Box(0, 0, 2, 2);

        AssertSameBits(FrozenUnion(a, b), BoundingBox.Union(a, b));
        Assert.False(BoundingBox.IsWithin(a, b));
    }

    private static double Value(Random random) => (random.NextDouble() - 0.5) * 1e3;

    private static Box RandomBox(Random random)
    {
        // Near-equal boxes exercise the epsilon edges as well as clear in/out cases.
        var scale = random.Next(2) == 0 ? 1e-9 : 10;
        return new Box(random.NextDouble() * scale, random.NextDouble() * scale,
            random.NextDouble() * scale, random.NextDouble() * scale);
    }

    private static void AssertSameBits(Box expected, Box actual)
    {
        AssertSameBits(expected.Left, actual.Left);
        AssertSameBits(expected.Bottom, actual.Bottom);
        AssertSameBits(expected.Right, actual.Right);
        AssertSameBits(expected.Top, actual.Top);
    }

    private static void AssertSameBits(double expected, double actual) =>
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    // Frozen from MaximalRectangles.IsContainedIn (identical to DrawingSplitter.BoxContainsBox
    // and NestedPartContainment.Within).
    private static bool FrozenIsContainedIn(Box inner, Box outer)
    {
        var eps = Tolerance.Epsilon;
        return inner.Left >= outer.Left - eps
            && inner.Right <= outer.Right + eps
            && inner.Bottom >= outer.Bottom - eps
            && inner.Top <= outer.Top + eps;
    }

    // Frozen from DiscPacker.Union / SheetPacker.Union / NestedPartContainment.Union.
    private static Box FrozenUnion(Box a, Box b)
    {
        var l = System.Math.Min(a.Left, b.Left);
        var bo = System.Math.Min(a.Bottom, b.Bottom);
        var r = System.Math.Max(a.Right, b.Right);
        var t = System.Math.Max(a.Top, b.Top);
        return new Box(l, bo, r - l, t - bo);
    }

    // Frozen from DiscPacker.Rotate / CutoutCirclePacker.Rotate / DrawingAligner.Rotate.
    private static Vector FrozenRotate(Vector v, double angle)
    {
        var sine = System.Math.Sin(angle);
        var cosine = System.Math.Cos(angle);
        return new Vector(v.X * cosine - v.Y * sine, v.X * sine + v.Y * cosine);
    }
}
