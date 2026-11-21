using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

public class EntityChainsTests
{
    [Fact]
    public void ClosedLoop_ComesBackWhole()
    {
        var bottom = new Line(0, 0, 10, 0);
        var right = new Line(10, 0, 10, 5);
        var corner = new Arc(8, 5, 2, 0, System.Math.PI / 2); // (10,5) to (8,7)
        var top = new Line(0, 7, 8, 7);
        var left = new Line(0, 0, 0, 7);
        var stray = new Line(20, 20, 30, 20);

        var chain = EntityChains.Connected(
            new Entity[] { bottom, right, corner, top, left, stray }, new[] { top });

        Assert.Equal(new Entity[] { bottom, right, corner, top, left }, chain);
    }

    [Fact]
    public void OpenChain_FollowsBranchesAndIgnoresReversedEnds()
    {
        var a = new Line(0, 0, 5, 0);
        var b = new Line(10, 0, 5, 0); // joined at its end, not its start
        var branch = new Line(5, 0, 5, 5);
        var separate = new Line(5.01, 5.01, 9, 9);

        var chain = EntityChains.Connected(new Entity[] { a, b, branch, separate }, new[] { a });

        Assert.Equal(new Entity[] { a, b, branch }, chain);
    }

    [Fact]
    public void EndsWithinToleranceJoin()
    {
        var a = new Line(0, 0, 5, 0);
        var b = new Line(5 + Tolerance.ChainTolerance / 2, 0, 5, 5);

        Assert.Equal(2, EntityChains.Connected(new Entity[] { a, b }, new[] { a }).Count);
    }

    [Fact]
    public void Circle_JoinsNothing()
    {
        var circle = new Circle(0, 0, 5);
        var line = new Line(5, 0, 10, 0);

        Assert.Equal(new Entity[] { line }, EntityChains.Connected(new Entity[] { circle, line }, new[] { line }));
        Assert.Equal(new Entity[] { circle }, EntityChains.Connected(new Entity[] { line }, new[] { circle }));
    }
}
