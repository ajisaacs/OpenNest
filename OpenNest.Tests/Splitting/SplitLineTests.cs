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
