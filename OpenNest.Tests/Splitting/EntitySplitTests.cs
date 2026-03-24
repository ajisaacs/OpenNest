using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Splitting;

public class EntitySplitTests
{
    // --- SplitLine.ToLine ---

    [Fact]
    public void ToLine_Vertical_ReturnsVerticalLine()
    {
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);
        var line = sl.ToLine(0, 100);

        Assert.Equal(50.0, line.StartPoint.X, 5);
        Assert.Equal(0.0, line.StartPoint.Y, 5);
        Assert.Equal(50.0, line.EndPoint.X, 5);
        Assert.Equal(100.0, line.EndPoint.Y, 5);
    }

    [Fact]
    public void ToLine_Horizontal_ReturnsHorizontalLine()
    {
        var sl = new SplitLine(30.0, CutOffAxis.Horizontal);
        var line = sl.ToLine(10, 90);

        Assert.Equal(10.0, line.StartPoint.X, 5);
        Assert.Equal(30.0, line.StartPoint.Y, 5);
        Assert.Equal(90.0, line.EndPoint.X, 5);
        Assert.Equal(30.0, line.EndPoint.Y, 5);
    }

    // --- FindIntersection: Line crossing vertical split ---

    [Fact]
    public void FindIntersection_LineCrossesVerticalSplit_ReturnsPoint()
    {
        // Diagonal line from (0,0) to (100,100), vertical split at x=50
        var line = new Line(0, 0, 100, 100);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        var result = SplitLineIntersect.FindIntersection(line, sl);

        Assert.NotNull(result);
        Assert.Equal(50.0, result.Value.X, 5);
        Assert.Equal(50.0, result.Value.Y, 5);
    }

    // --- FindIntersection: Line NOT crossing ---

    [Fact]
    public void FindIntersection_LineDoesNotCross_ReturnsNull()
    {
        // Line entirely to the left of split at x=50
        var line = new Line(0, 0, 40, 40);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        var result = SplitLineIntersect.FindIntersection(line, sl);

        Assert.Null(result);
    }

    // --- FindIntersection: Line parallel to split ---

    [Fact]
    public void FindIntersection_LineParallelToSplit_ReturnsNull()
    {
        // Vertical line at x=50 — parallel to vertical split at x=50
        var line = new Line(50, 0, 50, 100);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        var result = SplitLineIntersect.FindIntersection(line, sl);

        Assert.Null(result);
    }

    // --- FindIntersection: Arc crossing vertical split ---

    [Fact]
    public void FindIntersection_ArcCrossesVerticalSplit_ReturnsPoint()
    {
        // Arc centered at (60,50), radius 20, from PI to 0 (CCW).
        // CCW from PI wraps through 3PI/2 (bottom) then 0 (right).
        // At x=50: (50-60)^2 + (y-50)^2 = 400 => (y-50)^2 = 300
        // y = 50 - sqrt(300) ≈ 32.68 (bottom intersection, on the arc)
        // y = 50 + sqrt(300) ≈ 67.32 (top intersection, also on the arc)
        var arc = new Arc(60, 50, 20, System.Math.PI, 0, false);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        var result = SplitLineIntersect.FindIntersection(arc, sl);

        Assert.NotNull(result);
        Assert.Equal(50.0, result.Value.X, 1);
        // The first intersection found by the circle-line algorithm; either ~32.68 or ~67.32
        var y = result.Value.Y;
        var expectedLow = 50.0 - System.Math.Sqrt(300);
        var expectedHigh = 50.0 + System.Math.Sqrt(300);
        Assert.True(
            System.Math.Abs(y - expectedLow) < 0.1 || System.Math.Abs(y - expectedHigh) < 0.1,
            $"Expected Y near {expectedLow:F2} or {expectedHigh:F2}, got {y:F2}");
    }

    // --- CrossesSplitLine ---

    [Fact]
    public void CrossesSplitLine_LineStraddles_ReturnsTrue()
    {
        var line = new Line(40, 0, 60, 100);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        Assert.True(SplitLineIntersect.CrossesSplitLine(line, sl));
    }

    [Fact]
    public void CrossesSplitLine_LineEntirelyOnOneSide_ReturnsFalse()
    {
        var line = new Line(10, 0, 40, 100);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        Assert.False(SplitLineIntersect.CrossesSplitLine(line, sl));
    }

    [Fact]
    public void CrossesSplitLine_HorizontalSplit_Works()
    {
        var line = new Line(0, 10, 100, 60);
        var sl = new SplitLine(30.0, CutOffAxis.Horizontal);

        Assert.True(SplitLineIntersect.CrossesSplitLine(line, sl));
    }

    [Fact]
    public void CrossesSplitLine_LineTouchingButNotStraddling_ReturnsFalse()
    {
        // Line endpoint exactly at the split line — bbox right == 50, left < 50
        // But right must be > pos (strictly), so touching exactly returns false
        var line = new Line(10, 0, 50, 100);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        Assert.False(SplitLineIntersect.CrossesSplitLine(line, sl));
    }

    // --- SideOf ---

    [Fact]
    public void SideOf_PointLeftOfVerticalSplit_ReturnsNegative()
    {
        var pt = new Vector(30, 50);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        Assert.Equal(-1, SplitLineIntersect.SideOf(pt, sl));
    }

    [Fact]
    public void SideOf_PointRightOfVerticalSplit_ReturnsPositive()
    {
        var pt = new Vector(70, 50);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        Assert.Equal(1, SplitLineIntersect.SideOf(pt, sl));
    }

    [Fact]
    public void SideOf_PointOnVerticalSplit_ReturnsZero()
    {
        var pt = new Vector(50, 50);
        var sl = new SplitLine(50.0, CutOffAxis.Vertical);

        Assert.Equal(0, SplitLineIntersect.SideOf(pt, sl));
    }

    [Fact]
    public void SideOf_PointBelowHorizontalSplit_ReturnsNegative()
    {
        var pt = new Vector(50, 10);
        var sl = new SplitLine(30.0, CutOffAxis.Horizontal);

        Assert.Equal(-1, SplitLineIntersect.SideOf(pt, sl));
    }

    [Fact]
    public void SideOf_PointAboveHorizontalSplit_ReturnsPositive()
    {
        var pt = new Vector(50, 60);
        var sl = new SplitLine(30.0, CutOffAxis.Horizontal);

        Assert.Equal(1, SplitLineIntersect.SideOf(pt, sl));
    }

    // --- FindIntersection: horizontal split ---

    [Fact]
    public void FindIntersection_LineCrossesHorizontalSplit_ReturnsPoint()
    {
        // Diagonal line from (0,0) to (100,100), horizontal split at y=50
        var line = new Line(0, 0, 100, 100);
        var sl = new SplitLine(50.0, CutOffAxis.Horizontal);

        var result = SplitLineIntersect.FindIntersection(line, sl);

        Assert.NotNull(result);
        Assert.Equal(50.0, result.Value.X, 5);
        Assert.Equal(50.0, result.Value.Y, 5);
    }
}
