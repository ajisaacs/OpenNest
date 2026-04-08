using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.Tests.Geometry;

public class SpatialQueryTests
{
    #region Helpers

    private static List<Entity> MakeSquare(double size)
    {
        return new List<Entity>
        {
            new Line(0, 0, size, 0),
            new Line(size, 0, size, size),
            new Line(size, size, 0, size),
            new Line(0, size, 0, 0),
        };
    }

    private static List<Entity> MakeRoundedRect(double length, double width, double r)
    {
        return new List<Entity>
        {
            new Line(r, 0, length - r, 0),
            new Arc(length - r, r, r, Angle.ToRadians(270), Angle.ToRadians(360)),
            new Line(length, r, length, width - r),
            new Arc(length - r, width - r, r, Angle.ToRadians(0), Angle.ToRadians(90)),
            new Line(length - r, width, r, width),
            new Arc(r, width - r, r, Angle.ToRadians(90), Angle.ToRadians(180)),
            new Line(0, width - r, 0, r),
            new Arc(r, r, r, Angle.ToRadians(180), Angle.ToRadians(270)),
        };
    }

    private static List<Entity> MakeCircle(double cx, double cy, double radius)
    {
        return new List<Entity> { new Circle(cx, cy, radius) };
    }

    private static List<Entity> Translate(List<Entity> entities, double dx, double dy)
    {
        var result = new List<Entity>();
        foreach (var e in entities)
        {
            if (e is Line line)
                result.Add(new Line(line.pt1.X + dx, line.pt1.Y + dy, line.pt2.X + dx, line.pt2.Y + dy));
            else if (e is Arc arc)
                result.Add(new Arc(arc.Center.X + dx, arc.Center.Y + dy, arc.Radius, arc.StartAngle, arc.EndAngle));
            else if (e is Circle circle)
                result.Add(new Circle(circle.Center.X + dx, circle.Center.Y + dy, circle.Radius));
        }
        return result;
    }

    #endregion

    #region Circle vs Circle

    [Fact]
    public void CircleToCircle_Right_ReturnsGap()
    {
        var a = MakeCircle(0, 0, 5);
        var b = MakeCircle(20, 0, 5);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.InRange(dist, 9.9, 10.1);
    }

    [Fact]
    public void CircleToCircle_Left_ReturnsGap()
    {
        var a = MakeCircle(20, 0, 5);
        var b = MakeCircle(0, 0, 5);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(-1, 0));

        Assert.InRange(dist, 9.9, 10.1);
    }

    [Fact]
    public void CircleToCircle_Up_ReturnsGap()
    {
        var a = MakeCircle(0, 0, 5);
        var b = MakeCircle(0, 20, 5);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(0, 1));

        Assert.InRange(dist, 9.9, 10.1);
    }

    [Fact]
    public void CircleToCircle_Touching_ReturnsZero()
    {
        var a = MakeCircle(0, 0, 5);
        var b = MakeCircle(10, 0, 5);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.InRange(dist, -0.01, 0.01);
    }

    [Fact]
    public void CircleToCircle_NoPath_ReturnsMaxValue()
    {
        var a = MakeCircle(0, 0, 3);
        var b = MakeCircle(0, 20, 3);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.Equal(double.MaxValue, dist);
    }

    [Fact]
    public void CircleToCircle_PushDirection_Right()
    {
        var a = MakeCircle(0, 0, 5);
        var b = MakeCircle(20, 0, 5);

        var dist = SpatialQuery.DirectionalDistance(a, b, PushDirection.Right);

        Assert.InRange(dist, 9.9, 10.1);
    }

    #endregion

    #region Square vs Square

    [Fact]
    public void SquareToSquare_Right_ReturnsGap()
    {
        var a = MakeSquare(10);
        var b = Translate(MakeSquare(10), 25, 0);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.InRange(dist, 14.9, 15.1);
    }

    [Fact]
    public void SquareToSquare_Left_ReturnsGap()
    {
        var a = Translate(MakeSquare(10), 25, 0);
        var b = MakeSquare(10);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(-1, 0));

        Assert.InRange(dist, 14.9, 15.1);
    }

    [Fact]
    public void SquareToSquare_Down_ReturnsGap()
    {
        var a = Translate(MakeSquare(10), 0, 25);
        var b = MakeSquare(10);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(0, -1));

        Assert.InRange(dist, 14.9, 15.1);
    }

    [Fact]
    public void SquareToSquare_Touching_ReturnsZero()
    {
        var a = MakeSquare(10);
        var b = Translate(MakeSquare(10), 10, 0);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.InRange(dist, -0.01, 0.01);
    }

    [Fact]
    public void SquareToSquare_NoOverlap_ReturnsMaxValue()
    {
        var a = MakeSquare(10);
        var b = Translate(MakeSquare(10), 0, 20);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.Equal(double.MaxValue, dist);
    }

    [Fact]
    public void SquareToSquare_PartialOverlap_Right()
    {
        var a = MakeSquare(10);
        var b = Translate(MakeSquare(10), 20, 5);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.InRange(dist, 9.9, 10.1);
    }

    #endregion

    #region Rounded Rectangle

    [Fact]
    public void RoundedRect_Right_ReturnsGap()
    {
        var a = MakeRoundedRect(20, 10, 2);
        var b = Translate(MakeRoundedRect(20, 10, 2), 30, 0);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.InRange(dist, 9.9, 10.1);
    }

    [Fact]
    public void RoundedRect_Up_ReturnsGap()
    {
        var a = MakeRoundedRect(20, 10, 2);
        var b = Translate(MakeRoundedRect(20, 10, 2), 0, 25);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(0, 1));

        Assert.InRange(dist, 14.9, 15.1);
    }

    [Fact]
    public void RoundedRect_Touching_ReturnsZero()
    {
        var a = MakeRoundedRect(20, 10, 2);
        var b = Translate(MakeRoundedRect(20, 10, 2), 20, 0);

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.InRange(dist, -0.01, 0.01);
    }

    [Fact]
    public void RoundedRect_Diagonal_ReturnsDistance()
    {
        var dir = new Vector(1 / System.Math.Sqrt(2), 1 / System.Math.Sqrt(2));
        var a = MakeRoundedRect(10, 10, 2);
        var b = Translate(MakeRoundedRect(10, 10, 2), 20, 20);

        var dist = SpatialQuery.DirectionalDistance(a, b, dir);

        Assert.True(dist > 0 && dist < double.MaxValue);
    }

    #endregion

    #region Circle vs Square

    [Fact]
    public void CircleToSquare_Right_ReturnsGap()
    {
        var circle = MakeCircle(0, 5, 5);
        var square = Translate(MakeSquare(10), 15, 0);

        var dist = SpatialQuery.DirectionalDistance(circle, square, new Vector(1, 0));

        Assert.InRange(dist, 9.9, 10.1);
    }

    [Fact]
    public void SquareToCircle_Right_ReturnsGap()
    {
        var square = MakeSquare(10);
        var circle = MakeCircle(25, 5, 5);

        var dist = SpatialQuery.DirectionalDistance(square, circle, new Vector(1, 0));

        Assert.InRange(dist, 9.9, 10.1);
    }

    [Fact]
    public void CircleToSquare_Touching_ReturnsZero()
    {
        var circle = MakeCircle(0, 5, 5);
        var square = Translate(MakeSquare(10), 5, 0);

        var dist = SpatialQuery.DirectionalDistance(circle, square, new Vector(1, 0));

        Assert.InRange(dist, -0.01, 0.01);
    }

    #endregion

    #region Circle vs Rounded Rectangle

    [Fact]
    public void CircleToRoundedRect_Right_ReturnsGap()
    {
        var circle = MakeCircle(0, 5, 5);
        var rect = Translate(MakeRoundedRect(20, 10, 2), 15, 0);

        var dist = SpatialQuery.DirectionalDistance(circle, rect, new Vector(1, 0));

        Assert.InRange(dist, 9.9, 10.1);
    }

    [Fact]
    public void RoundedRectToCircle_Left_ReturnsGap()
    {
        var rect = Translate(MakeRoundedRect(20, 10, 2), 15, 0);
        var circle = MakeCircle(0, 5, 5);

        var dist = SpatialQuery.DirectionalDistance(rect, circle, new Vector(-1, 0));

        Assert.InRange(dist, 9.9, 10.1);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void EmptyLists_ReturnsMaxValue()
    {
        var a = new List<Entity>();
        var b = new List<Entity>();

        var dist = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));

        Assert.Equal(double.MaxValue, dist);
    }

    [Fact]
    public void Symmetry_LeftRightReturnSameDistance()
    {
        var a = MakeSquare(10);
        var b = Translate(MakeSquare(10), 25, 0);

        var right = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));
        var left = SpatialQuery.DirectionalDistance(b, a, new Vector(-1, 0));

        Assert.InRange(System.Math.Abs(right - left), 0, 0.01);
    }

    [Fact]
    public void Symmetry_CirclesLeftRightSame()
    {
        var a = MakeCircle(0, 0, 5);
        var b = MakeCircle(20, 0, 5);

        var right = SpatialQuery.DirectionalDistance(a, b, new Vector(1, 0));
        var left = SpatialQuery.DirectionalDistance(b, a, new Vector(-1, 0));

        Assert.InRange(System.Math.Abs(right - left), 0, 0.01);
    }

    #endregion
}
