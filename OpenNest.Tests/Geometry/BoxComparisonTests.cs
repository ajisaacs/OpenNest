using System;
using System.Collections.Generic;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.Geometry;

public class BoxComparisonTests
{
    [Fact]
    public void GreaterThan_TallerBox_ReturnsTrue()
    {
        var tall = new Box(0, 0, 10, 20);
        var short_ = new Box(0, 0, 10, 10);

        Assert.True(tall > short_);
        Assert.False(short_ > tall);
    }

    [Fact]
    public void GreaterThan_SameWidthLongerBox_ReturnsTrue()
    {
        var longer = new Box(0, 0, 20, 10);
        var shorter = new Box(0, 0, 10, 10);

        Assert.True(longer > shorter);
        Assert.False(shorter > longer);
    }

    [Fact]
    public void LessThan_ShorterBox_ReturnsTrue()
    {
        var tall = new Box(0, 0, 10, 20);
        var short_ = new Box(0, 0, 10, 10);

        Assert.True(short_ < tall);
        Assert.False(tall < short_);
    }

    [Fact]
    public void GreaterThanOrEqual_EqualBoxes_ReturnsTrue()
    {
        var a = new Box(0, 0, 10, 20);
        var b = new Box(0, 0, 10, 20);

        Assert.True(a >= b);
        Assert.True(b >= a);
    }

    [Fact]
    public void LessThanOrEqual_EqualBoxes_ReturnsTrue()
    {
        var a = new Box(0, 0, 10, 20);
        var b = new Box(0, 0, 10, 20);

        Assert.True(a <= b);
        Assert.True(b <= a);
    }

    [Fact]
    public void CompareTo_TallerBox_ReturnsPositive()
    {
        var tall = new Box(0, 0, 10, 20);
        var short_ = new Box(0, 0, 10, 10);

        Assert.True(tall.CompareTo(short_) > 0);
        Assert.True(short_.CompareTo(tall) < 0);
    }

    [Fact]
    public void CompareTo_EqualBoxes_ReturnsZero()
    {
        var a = new Box(0, 0, 10, 20);
        var b = new Box(0, 0, 10, 20);

        Assert.Equal(0, a.CompareTo(b));
    }

    [Fact]
    public void Sort_OrdersByWidthThenLength()
    {
        var boxes = new List<Box>
        {
            new Box(0, 0, 20, 10),
            new Box(0, 0, 5, 30),
            new Box(0, 0, 10, 10),
        };

        boxes.Sort();

        Assert.Equal(10, boxes[0].Width);
        Assert.Equal(10, boxes[0].Length);
        Assert.Equal(10, boxes[1].Width);
        Assert.Equal(20, boxes[1].Length);
        Assert.Equal(30, boxes[2].Width);
    }
}
