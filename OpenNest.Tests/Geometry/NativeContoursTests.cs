using System.Collections.Generic;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.Geometry;

public class NativeContoursTests
{
    private static ShapeProfile Rectangle(double width, double height) => new(new List<Entity>
    {
        new Line(new Vector(0, 0), new Vector(width, 0)),
        new Line(new Vector(width, 0), new Vector(width, height)),
        new Line(new Vector(width, height), new Vector(0, height)),
        new Line(new Vector(0, height), new Vector(0, 0)),
    });

    [Fact]
    public void Transformed_TurnsAboutTheOriginThenMoves()
    {
        var local = NativeContours.Of(Rectangle(4, 2), Vector.Zero);

        var moved = local.Transformed(System.Math.PI / 2, new Vector(10, 5));

        // A quarter turn puts the 4 x 2 rectangle at x -2..0, y 0..4 before the move.
        Assert.Equal(8, moved.Bounds.Left, 9);
        Assert.Equal(10, moved.Bounds.Right, 9);
        Assert.Equal(5, moved.Bounds.Bottom, 9);
        Assert.Equal(9, moved.Bounds.Top, 9);
    }

    [Fact]
    public void Translated_MatchesContoursBuiltAtTheLocation()
    {
        var profile = Rectangle(4, 2);
        var other = NativeContours.Of(Rectangle(1, 1), new Vector(8, 0));

        var moved = NativeContours.Of(profile, Vector.Zero).Translated(new Vector(3, 0));
        var built = NativeContours.Of(profile, new Vector(3, 0));

        Assert.Equal(built.GapTo(other, 5, out _), moved.GapTo(other, 5, out var onMoved, out var onOther), 9);
        Assert.Equal(1, moved.GapTo(other, 5, out _), 9);
        Assert.Equal(7, onMoved.X, 9);
        Assert.Equal(8, onOther.X, 9);
    }

    [Fact]
    public void Nested_FindsAPartInsideAnotherWithoutTouching()
    {
        var outer = NativeContours.Of(Rectangle(10, 10), Vector.Zero);
        var inner = NativeContours.Of(Rectangle(1, 1), new Vector(4, 4));
        var apart = NativeContours.Of(Rectangle(1, 1), new Vector(20, 0));

        Assert.True(outer.Nested(inner));
        Assert.True(inner.Nested(outer));
        Assert.False(outer.Nested(apart));
    }
}
