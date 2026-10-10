using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class RapidContactPointTests
{
    [Theory]
    [InlineData(-2, 2, 6, 2, 2)]
    [InlineData(-2, -2, 6, 6, 2)] // Shared corners are deduplicated.
    [InlineData(-2, 4, 6, 4, 2)] // Collinear contact span.
    [InlineData(-2, 2, 0, 2, 1)] // Endpoint arrival.
    [InlineData(0, 0, -2, -2, 0)] // Start-only departure.
    [InlineData(1, 1, 2, 2, 0)] // Entirely inside.
    public void RectangleContacts(double x1, double y1, double x2, double y2, int count)
    {
        var vertices = new[] { new Vector(0, 0), new Vector(4, 0), new Vector(4, 4), new Vector(0, 4) };
        var curves = Enumerable.Range(0, 4).Select(i =>
            PostVerificationGeometry.Curve.Create(vertices[i], vertices[(i + 1) % 4], null, false)).ToArray();
        var start = new Vector(x1, y1);
        var end = new Vector(x2, y2);
        var points = PostVerificationGeometry.ContactPoints(start, end, curves, default);
        Assert.Equal(count, points.Count);
        Assert.True(((ICollection<Vector>)points).IsReadOnly);
        Assert.All(points, point =>
        {
            Assert.Contains(curves, curve => curve.Contains(point));
            Assert.True(new Line(start, end).ClosestPointTo(point).DistanceTo(point) < 1e-8);
        });
        Assert.Equal(points.OrderBy(point => point.DistanceTo(start)), points);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)] // Tangency has a single marker.
    public void CircleContactsUseNativeCurve(double y, int count)
    {
        var circle = PostVerificationGeometry.Curve.Create(new Vector(1, 0), new Vector(1, 0), Vector.Zero, false);
        var points = PostVerificationGeometry.ContactPoints(new Vector(-2, y), new Vector(2, y), [circle], default);
        Assert.Equal(count, points.Count);
        Assert.All(points, point => Assert.Equal(1, point.DistanceTo(Vector.Zero), 8));
        Assert.Equal(new Vector(count == 1 ? 0 : -1, y), points[0]);
    }

    [Fact]
    public void ArcContactsExcludeUnusedCircleHalf()
    {
        var arc = PostVerificationGeometry.Curve.Create(new Vector(0, -1), new Vector(0, 1), Vector.Zero, false);
        var points = PostVerificationGeometry.ContactPoints(new Vector(-2, 0), new Vector(2, 0), [arc], default);
        Assert.Equal(new Vector(1, 0), Assert.Single(points));
    }
}
