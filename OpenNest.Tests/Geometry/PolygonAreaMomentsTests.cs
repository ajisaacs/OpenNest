using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class PolygonAreaMomentsTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1000000000, false, false)]
    [InlineData(1000000000, true, true)]
    [InlineData(-1000000000, false, true)]
    [InlineData(-1000000000, true, false)]
    public void TryCompute_RectangleUsesSignedMomentsWithLocalOrigin(double offset, bool reverse, bool closed)
    {
        var vertices = Rectangle(offset + 0.5, offset, 0.5, 1);
        if (reverse)
            Array.Reverse(vertices);
        if (closed)
            vertices = vertices.Append(vertices[0]).ToArray();

        Assert.True(PolygonAreaMoments.TryCompute(vertices, out var moments));

        Assert.Equal(0.5, moments.Area);
        Assert.Equal(offset + 0.75, moments.Centroid.X);
        Assert.Equal(offset + 0.5, moments.Centroid.Y);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryCompute_ConcavePolygonUsesAreaNotVertexAverage(bool reverse)
    {
        var vertices = new[]
        {
            new Vector(0, 0), new Vector(3, 0), new Vector(3, 1),
            new Vector(1, 1), new Vector(1, 3), new Vector(0, 3)
        };
        if (reverse)
            Array.Reverse(vertices);

        Assert.True(PolygonAreaMoments.TryCompute(vertices, out var moments));

        Assert.Equal(5, moments.Area);
        Assert.Equal(1.1, moments.Centroid.X, 12);
        Assert.Equal(1.1, moments.Centroid.Y, 12);
    }

    [Fact]
    public void TryCompute_CollinearVerticesDoNotBiasCentroid()
    {
        var vertices = new[]
        {
            new Vector(0, 0), new Vector(1, 0), new Vector(2, 0), new Vector(3, 0),
            new Vector(3, 2), new Vector(0, 2), new Vector(0, 0)
        };

        Assert.True(PolygonAreaMoments.TryCompute(vertices, out var moments));

        Assert.Equal(6, moments.Area);
        Assert.Equal(1.5, moments.Centroid.X);
        Assert.Equal(1, moments.Centroid.Y);
    }

    [Fact]
    public void TryCompute_NearButNotExactClosingVertexIsNotDiscarded()
    {
        var vertices = new[]
        {
            new Vector(0, 0), new Vector(1, 0), new Vector(1, 1),
            new Vector(0, 1), new Vector(0.000001, 0.000001)
        };
        // The final triangle removes area d/2 and has center (d/3, (1+d)/3).
        const double d = 0.000001;
        var expectedArea = 1 - d / 2;

        Assert.True(PolygonAreaMoments.TryCompute(vertices, out var moments));

        Assert.Equal(expectedArea, moments.Area, 14);
        Assert.Equal((0.5 - d * d / 6) / expectedArea, moments.Centroid.X, 14);
        Assert.Equal((0.5 - d * (1 + d) / 6) / expectedArea, moments.Centroid.Y, 14);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1000000000, false)]
    [InlineData(1000000000, true)]
    [InlineData(-1000000000, false)]
    public void TryCombine_UnequalDisconnectedFragmentsUsePositiveAreaWeights(double offset, bool swap)
    {
        Assert.True(PolygonAreaMoments.TryCompute(Rectangle(offset, offset, 1, 1), out var small));
        Assert.True(PolygonAreaMoments.TryCompute(
            Rectangle(offset + 4, offset, 1, 3).Reverse().ToArray(), out var large));

        Assert.True(PolygonAreaMoments.TryCombine(swap ? new[] { large, small } : new[] { small, large },
            out var combined));

        Assert.Equal(4, combined.Area);
        Assert.Equal(offset + 3.5, combined.Centroid.X);
        Assert.Equal(offset + 1.25, combined.Centroid.Y);
        // The centroid is in the gap, not on either material fragment.
        Assert.InRange(combined.Centroid.X - offset, 1.01, 3.99);
    }

    public static IEnumerable<object[]> InvalidPolygons()
    {
        yield return new object[] { Array.Empty<Vector>() };
        yield return new object[] { new[] { new Vector(1, 1), new Vector(2, 2) } };
        yield return new object[] { new[] { new Vector(1, 1), new Vector(2, 2), new Vector(3, 3) } };
        yield return new object[] { new[] { new Vector(1, 1), new Vector(1, 1), new Vector(1, 1) } };
        yield return new object[] { new[] { new Vector(0, 0), new Vector(double.NaN, 0), new Vector(0, 1) } };
        yield return new object[] { new[] { new Vector(0, 0), new Vector(1, double.PositiveInfinity), new Vector(0, 1) } };
        yield return new object[] { new[] { new Vector(double.NegativeInfinity, 0), new Vector(1, 0), new Vector(0, 1) } };
        yield return new object[] { Rectangle(0, 0, 1e200, 1e200) };
        yield return new object[] { Rectangle(0, 0, 1e103, 1e103) };
    }

    [Theory]
    [MemberData(nameof(InvalidPolygons))]
    public void TryCompute_DegenerateNonfiniteOrOverflowingMomentsAreRejected(Vector[] vertices)
    {
        Assert.False(PolygonAreaMoments.TryCompute(vertices, out _));
    }

    [Fact]
    public void TryCombine_EmptyOrInvalidFragmentRejectsWholeResult()
    {
        Assert.True(PolygonAreaMoments.TryCompute(Rectangle(1, 1, 1, 1), out var valid));

        Assert.False(PolygonAreaMoments.TryCombine(Array.Empty<PolygonAreaMoments>(), out _));
        Assert.False(PolygonAreaMoments.TryCombine(new[] { valid, default(PolygonAreaMoments) }, out _));
        Assert.False(PolygonAreaMoments.TryCombine(new[] { default(PolygonAreaMoments), valid }, out _));
    }

    private static Vector[] Rectangle(double x, double y, double width, double height) =>
        new[] { new Vector(x, y), new Vector(x + width, y),
            new Vector(x + width, y + height), new Vector(x, y + height) };
}
