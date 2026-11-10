using System.Security.Cryptography;
using OpenNest.Math;

namespace OpenNest.Tests.Math;

public class PortableMathTests
{
    private static long Ulps(double a, double b)
    {
        if (a == b)
            return 0;
        if (System.Math.Sign(a) != System.Math.Sign(b))
            return long.MaxValue;
        return System.Math.Abs(
            BitConverter.DoubleToInt64Bits(a) - BitConverter.DoubleToInt64Bits(b)
        );
    }

    private static IEnumerable<double> Inputs(double range, int count, int seed)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
            yield return (2 * random.NextDouble() - 1) * range;
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(7.0)]
    [InlineData(1000.0)]
    [InlineData(1e5)]
    public void SinAndCos_AgreeWithTheRuntimeWithinOneUlp(double range)
    {
        foreach (var x in Inputs(range, 20000, 17))
        {
            Assert.True(Ulps(PortableMath.Sin(x), System.Math.Sin(x)) <= 1, $"sin {x:R}");
            Assert.True(Ulps(PortableMath.Cos(x), System.Math.Cos(x)) <= 1, $"cos {x:R}");
        }
    }

    [Fact]
    public void AtanAndAtan2_AgreeWithTheRuntimeWithinOneUlp()
    {
        var ys = Inputs(50, 20000, 3).ToArray();
        var xs = Inputs(50, 20000, 4).ToArray();
        for (var i = 0; i < ys.Length; i++)
        {
            Assert.True(
                Ulps(PortableMath.Atan(ys[i]), System.Math.Atan(ys[i])) <= 1,
                $"atan {ys[i]:R}"
            );
            Assert.True(
                Ulps(PortableMath.Atan2(ys[i], xs[i]), System.Math.Atan2(ys[i], xs[i])) <= 1,
                $"atan2 {ys[i]:R} {xs[i]:R}"
            );
        }
    }

    [Fact]
    public void TanAsinAcos_AgreeWithTheRuntimeWithinAFewUlps()
    {
        foreach (var x in Inputs(1.5, 20000, 5))
            Assert.True(Ulps(PortableMath.Tan(x), System.Math.Tan(x)) <= 4, $"tan {x:R}");
        foreach (var x in Inputs(1, 20000, 6))
        {
            Assert.True(Ulps(PortableMath.Asin(x), System.Math.Asin(x)) <= 4, $"asin {x:R}");
            Assert.True(Ulps(PortableMath.Acos(x), System.Math.Acos(x)) <= 4, $"acos {x:R}");
        }
    }

    [Fact]
    public void ExactValues()
    {
        Assert.Equal(0.0, PortableMath.Sin(0));
        Assert.Equal(1.0, PortableMath.Cos(0));
        Assert.Equal(1.0, PortableMath.Sin(System.Math.PI / 2));
        Assert.Equal(-1.0, PortableMath.Cos(System.Math.PI));
        Assert.Equal(System.Math.PI, PortableMath.Atan2(0, -1));
        Assert.Equal(-System.Math.PI, PortableMath.Atan2(-0.0, -1));
        Assert.Equal(System.Math.PI / 2, PortableMath.Atan2(1, 0));
        Assert.Equal(-System.Math.PI / 2, PortableMath.Atan2(-1, 0));
        Assert.Equal(System.Math.PI / 4, PortableMath.Atan2(1, 1));
        Assert.Equal(0.0, PortableMath.Acos(1));
        Assert.Equal(System.Math.PI, PortableMath.Acos(-1));
        Assert.Equal(System.Math.PI / 2, PortableMath.Asin(1));
        Assert.True(double.IsNaN(PortableMath.Sin(double.PositiveInfinity)));
        Assert.True(double.IsNaN(PortableMath.Acos(1.5)));
        Assert.True(double.IsNaN(PortableMath.Atan2(double.NaN, 1)));
    }

    /// <summary>
    /// The point of PortableMath: the same bits on every platform. The digest was captured on
    /// Linux and must also pass on Windows (Project Memory 518f3b58).
    /// </summary>
    [Fact]
    public void ResultsAreTheSameBitsOnEveryPlatform()
    {
        var bytes = new List<byte>();
        void Add(double v) => bytes.AddRange(BitConverter.GetBytes(v));
        foreach (var x in Inputs(10, 5000, 11).Concat(Inputs(1e6, 1000, 12)))
        {
            Add(PortableMath.Sin(x));
            Add(PortableMath.Cos(x));
            Add(PortableMath.Atan(x));
            Add(PortableMath.Atan2(x, x * 0.37 - 1));
        }

        foreach (var x in Inputs(1, 2000, 13))
        {
            Add(PortableMath.Asin(x));
            Add(PortableMath.Acos(x));
            Add(PortableMath.Tan(x));
        }

        for (var degrees = 0; degrees < 360; degrees++)
        {
            Add(PortableMath.Sin(Angle.ToRadians(degrees)));
            Add(PortableMath.Cos(Angle.ToRadians(degrees)));
        }

        var digest = Convert.ToHexString(SHA256.HashData(bytes.ToArray()))[..16];
        Assert.Equal("D20C2D300368105F", digest);
    }
}
