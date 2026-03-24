using OpenNest.Geometry;

namespace OpenNest.Tests.Splitting;

public class SplitLineTests
{
    [Fact]
    public void SplitLine_Vertical_StoresPositionAsX()
    {
        var line = new SplitLine(50.0, CutOffAxis.Vertical);
        Assert.Equal(50.0, line.Position);
        Assert.Equal(CutOffAxis.Vertical, line.Axis);
    }

    [Fact]
    public void SplitLine_Horizontal_StoresPositionAsY()
    {
        var line = new SplitLine(30.0, CutOffAxis.Horizontal);
        Assert.Equal(30.0, line.Position);
        Assert.Equal(CutOffAxis.Horizontal, line.Axis);
    }

    [Fact]
    public void SplitParameters_Defaults()
    {
        var p = new SplitParameters();
        Assert.Equal(SplitType.Straight, p.Type);
        Assert.Equal(3, p.TabCount);
        Assert.Equal(1.0, p.TabWidth);
        Assert.Equal(0.125, p.TabHeight);
        Assert.Equal(2, p.SpikePairCount);
    }
}

public class AutoSplitCalculatorTests
{
    [Fact]
    public void FitToPlate_SingleAxis_CalculatesCorrectSplits()
    {
        var partBounds = new Box(0, 0, 100, 50);
        var lines = AutoSplitCalculator.FitToPlate(partBounds, 60, 60, 1.0, 0);

        Assert.Single(lines);
        Assert.Equal(CutOffAxis.Vertical, lines[0].Axis);
        Assert.Equal(50.0, lines[0].Position, 1);
    }

    [Fact]
    public void FitToPlate_BothAxes_GeneratesGrid()
    {
        var partBounds = new Box(0, 0, 200, 200);
        var lines = AutoSplitCalculator.FitToPlate(partBounds, 60, 60, 0, 0);

        var verticals = lines.Where(l => l.Axis == CutOffAxis.Vertical).ToList();
        var horizontals = lines.Where(l => l.Axis == CutOffAxis.Horizontal).ToList();
        Assert.Equal(3, verticals.Count);
        Assert.Equal(3, horizontals.Count);
    }

    [Fact]
    public void FitToPlate_AlreadyFits_ReturnsEmpty()
    {
        var partBounds = new Box(0, 0, 50, 50);
        var lines = AutoSplitCalculator.FitToPlate(partBounds, 60, 60, 1.0, 0);
        Assert.Empty(lines);
    }

    [Fact]
    public void SplitByCount_SingleAxis_EvenlySpaced()
    {
        var partBounds = new Box(0, 0, 100, 50);
        var lines = AutoSplitCalculator.SplitByCount(partBounds, horizontalPieces: 1, verticalPieces: 3);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(CutOffAxis.Vertical, l.Axis));
        Assert.Equal(33.333, lines[0].Position, 2);
        Assert.Equal(66.667, lines[1].Position, 2);
    }

    [Fact]
    public void FitToPlate_AccountsForFeatureOverhang()
    {
        var partBounds = new Box(0, 0, 100, 50);
        var lines = AutoSplitCalculator.FitToPlate(partBounds, 60, 60, 1.0, 0.5);
        Assert.Single(lines);
    }
}
