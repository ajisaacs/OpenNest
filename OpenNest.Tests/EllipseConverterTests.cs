using OpenNest.Geometry;
using OpenNest.Math;
using Xunit;

namespace OpenNest.Tests;

public class EllipseConverterTests
{
    private const double Tol = 1e-10;

    [Fact]
    public void EvaluatePoint_AtZero_ReturnsMajorAxisEnd()
    {
        var p = EllipseConverter.EvaluatePoint(10, 5, 0, new Vector(0, 0), 0);
        Assert.InRange(p.X, 10 - Tol, 10 + Tol);
        Assert.InRange(p.Y, -Tol, Tol);
    }

    [Fact]
    public void EvaluatePoint_AtHalfPi_ReturnsMinorAxisEnd()
    {
        var p = EllipseConverter.EvaluatePoint(10, 5, 0, new Vector(0, 0), System.Math.PI / 2);
        Assert.InRange(p.X, -Tol, Tol);
        Assert.InRange(p.Y, 5 - Tol, 5 + Tol);
    }

    [Fact]
    public void EvaluatePoint_WithRotation_RotatesCorrectly()
    {
        var p = EllipseConverter.EvaluatePoint(10, 5, System.Math.PI / 2, new Vector(0, 0), 0);
        Assert.InRange(p.X, -Tol, Tol);
        Assert.InRange(p.Y, 10 - Tol, 10 + Tol);
    }

    [Fact]
    public void EvaluatePoint_WithCenter_TranslatesCorrectly()
    {
        var p = EllipseConverter.EvaluatePoint(10, 5, 0, new Vector(100, 200), 0);
        Assert.InRange(p.X, 110 - Tol, 110 + Tol);
        Assert.InRange(p.Y, 200 - Tol, 200 + Tol);
    }

    [Fact]
    public void EvaluateTangent_AtZero_PointsUp()
    {
        var t = EllipseConverter.EvaluateTangent(10, 5, 0, 0);
        Assert.InRange(t.X, -Tol, Tol);
        Assert.True(t.Y > 0);
    }

    [Fact]
    public void EvaluateNormal_AtZero_PointsInward()
    {
        var n = EllipseConverter.EvaluateNormal(10, 5, 0, 0);
        Assert.True(n.X < 0);
        Assert.InRange(n.Y, -Tol, Tol);
    }

    [Fact]
    public void IntersectNormals_PerpendicularNormals_FindsCenter()
    {
        var p1 = new Vector(5, 0);
        var n1 = new Vector(-1, 0);
        var p2 = new Vector(0, 5);
        var n2 = new Vector(0, -1);
        var center = EllipseConverter.IntersectNormals(p1, n1, p2, n2);
        Assert.InRange(center.X, -Tol, Tol);
        Assert.InRange(center.Y, -Tol, Tol);
    }

    [Fact]
    public void IntersectNormals_ParallelNormals_ReturnsInvalid()
    {
        var p1 = new Vector(0, 0);
        var n1 = new Vector(1, 0);
        var p2 = new Vector(0, 5);
        var n2 = new Vector(1, 0);
        var center = EllipseConverter.IntersectNormals(p1, n1, p2, n2);
        Assert.False(center.IsValid());
    }
}
