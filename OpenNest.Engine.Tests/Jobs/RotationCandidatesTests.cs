using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class RotationCandidatesTests
{
    [Fact]
    public void RectangleOnlyNeedsRightAngles()
    {
        Assert.Equal(RotationPolicy.Automatic.EnumerateAngles(),
            RotationCandidates.ForShape(RotationPolicy.Automatic, Rectangle()));
    }

    [Fact]
    public void RotatedRectangleAddsExistingCalipersAnglesWithoutMutatingShape()
    {
        var shape = Rectangle();
        shape.Rotate(0.3);
        var before = shape.ToPolygonWithTolerance(0.1).Vertices.ToArray();
        var angles = RotationCandidates.ForShape(RotationPolicy.Automatic, shape);
        Assert.Equal(8, angles.Count);
        var expected = -shape.ToPolygonWithTolerance(0.1).FindBestRotation().Angle;
        for (var turn = 0; turn < 4; turn++)
        {
            var normalized = (expected + turn * System.Math.PI / 2 + 2 * System.Math.PI)
                % (2 * System.Math.PI);
            Assert.Equal(normalized, angles[4 + turn], 10);
        }
        Assert.All(angles, angle => Assert.True(RotationPolicy.Automatic.Allows(angle)));
        Assert.Equal(before, shape.ToPolygonWithTolerance(0.1).Vertices);
        Assert.Equal(angles, RotationCandidates.ForShape(RotationPolicy.Automatic, shape));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LimitPreservesRightAnglesFirst(int limit)
    {
        var shape = Rectangle();
        shape.Rotate(0.3);
        var all = RotationCandidates.ForShape(RotationPolicy.Automatic, shape);
        Assert.Equal(all.Take(limit), RotationCandidates.ForShape(RotationPolicy.Automatic, shape, limit));
        Assert.Equal(RotationPolicy.Automatic.EnumerateAngles().Take(System.Math.Min(limit, 4)),
            all.Take(System.Math.Min(limit, 4)));
    }

    [Fact]
    public void RestrictedPoliciesDoNotAddShapeAngles()
    {
        var shape = Rectangle();
        shape.Rotate(0.3);
        foreach (var policy in new[] { RotationPolicy.Fixed(0.1, true),
            RotationPolicy.BoundedSweep(5, 7, 0.1, true) })
        {
            var angles = RotationCandidates.ForShape(policy, shape);
            Assert.Equal(policy.EnumerateAngles(), angles);
            Assert.All(angles, angle => Assert.True(policy.Allows(angle)));
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("open")]
    [InlineData("zero-area")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("invalid-circle")]
    public void InvalidPerimeterFallsBackToPolicyAngles(string kind)
    {
        var shape = new Shape();
        if (kind == "open")
            shape.Entities.Add(new Line(new Vector(0, 0), new Vector(1, 1)));
        if (kind == "zero-area")
        {
            shape.Entities.Add(new Line(new Vector(0, 0), new Vector(1, 1)));
            shape.Entities.Add(new Line(new Vector(1, 1), new Vector(0, 0)));
        }
        if (kind is "nan" or "infinity")
        {
            shape = Rectangle();
            ((Line)shape.Entities[0]).StartPoint = new Vector(
                kind == "nan" ? double.NaN : double.PositiveInfinity, 0);
        }
        if (kind == "invalid-circle")
            shape.Entities.Add(new Circle(0, 0, -1));
        Assert.Equal(RotationPolicy.Automatic.EnumerateAngles(),
            RotationCandidates.ForShape(RotationPolicy.Automatic, shape));
    }

    [Fact]
    public void DiscCollapsesToFirstOrientationIncludingArbitraryAngles()
    {
        var shape = new Shape();
        shape.Entities.Add(new Circle(3, 7, 2));
        Assert.Equal(new[] { 0.3 }, RotationCandidates.DistinctOutlines(shape,
            new[] { 0.3, 0, 0.7, System.Math.PI / 2, System.Math.PI }));
    }

    [Fact]
    public void RectangleHasTwoOutlinesAndPreservesFirstOccurrence()
    {
        var shape = Rectangle();
        shape.Offset(12, -7);
        var before = shape.ToPolygon().Vertices.ToArray();
        Assert.Equal(new[] { 0.0, System.Math.PI / 2 }, RotationCandidates.DistinctOutlines(
            shape, RotationPolicy.Automatic.EnumerateAngles()));
        Assert.Equal(new[] { System.Math.PI, 3 * System.Math.PI / 2 },
            RotationCandidates.DistinctOutlines(shape,
                new[] { System.Math.PI, -System.Math.PI / 2, 0, System.Math.PI / 2 }));
        Assert.Equal(before, shape.ToPolygon().Vertices);
    }

    [Fact]
    public void AsymmetricOutlineDoesNotCollapseEvenWithSquareBounds()
    {
        var shape = Polygon(new Vector(0, 0), new Vector(4, 0), new Vector(1, 4));
        Assert.Equal(4, RotationCandidates.DistinctOutlines(shape,
            RotationPolicy.Automatic.EnumerateAngles()).Count);
    }

    [Fact]
    public void OutlineToleranceControlsNearSymmetry()
    {
        var shape = Polygon(new Vector(0, 0), new Vector(4, 0),
            new Vector(4, 4.000001), new Vector(0, 4.000001));
        Assert.Single(RotationCandidates.DistinctOutlines(shape,
            RotationPolicy.Automatic.EnumerateAngles(), 1e-5));
        Assert.Equal(2, RotationCandidates.DistinctOutlines(shape,
            RotationPolicy.Automatic.EnumerateAngles(), 1e-8).Count);
    }

    [Fact]
    public void NonFiniteAndDuplicateAnglesAreOmitted()
    {
        Assert.Equal(new[] { 0.0 }, RotationCandidates.DistinctOutlines(Rectangle(),
            new[] { double.NaN, 0, 1e-8, 2 * System.Math.PI, double.PositiveInfinity }));
    }

    [Fact]
    public void InvalidArgumentsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RotationCandidates.ForShape(RotationPolicy.Automatic, Rectangle(), -1));
        foreach (var tolerance in new[] { 0, -1, double.NaN, double.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RotationCandidates.DistinctOutlines(Rectangle(), new[] { 0.0 }, tolerance));
        Assert.Throws<ArgumentNullException>(() => RotationCandidates.ForShape(null!, Rectangle()));
        Assert.Throws<ArgumentNullException>(() => RotationCandidates.ForShape(RotationPolicy.Automatic, null!));
        Assert.Throws<ArgumentException>(() => RotationCandidates.DistinctOutlines(new Shape(), new[] { 0.0 }));
    }

    private static Shape Rectangle() => Polygon(new Vector(0, 0), new Vector(4, 0),
        new Vector(4, 2), new Vector(0, 2));

    private static Shape Polygon(params Vector[] points)
    {
        var shape = new Shape();
        for (var index = 0; index < points.Length; index++)
            shape.Entities.Add(new Line(points[index], points[(index + 1) % points.Length]));
        return shape;
    }
}
