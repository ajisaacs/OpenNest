using OpenNest.Geometry;

namespace OpenNest.Tests.Splitting;

public class SplitFeatureTests
{
    [Fact]
    public void StraightSplit_Vertical_ProducesSingleLineEachSide()
    {
        var feature = new StraightSplit();
        var line = new SplitLine(50.0, CutOffAxis.Vertical);
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var result = feature.GenerateFeatures(line, 10.0, 90.0, parameters);

        Assert.Single(result.NegativeSideEdge);
        var negLine = Assert.IsType<Line>(result.NegativeSideEdge[0]);
        Assert.Equal(50.0, negLine.StartPoint.X, 6);
        Assert.Equal(10.0, negLine.StartPoint.Y, 6);
        Assert.Equal(50.0, negLine.EndPoint.X, 6);
        Assert.Equal(90.0, negLine.EndPoint.Y, 6);

        Assert.Single(result.PositiveSideEdge);
        var posLine = Assert.IsType<Line>(result.PositiveSideEdge[0]);
        Assert.Equal(50.0, posLine.StartPoint.X, 6);
        Assert.Equal(90.0, posLine.StartPoint.Y, 6);
        Assert.Equal(50.0, posLine.EndPoint.X, 6);
        Assert.Equal(10.0, posLine.EndPoint.Y, 6);
    }

    [Fact]
    public void StraightSplit_Horizontal_ProducesSingleLineEachSide()
    {
        var feature = new StraightSplit();
        var line = new SplitLine(40.0, CutOffAxis.Horizontal);
        var parameters = new SplitParameters { Type = SplitType.Straight };

        var result = feature.GenerateFeatures(line, 5.0, 95.0, parameters);

        var negLine = Assert.IsType<Line>(result.NegativeSideEdge[0]);
        Assert.Equal(5.0, negLine.StartPoint.X, 6);
        Assert.Equal(40.0, negLine.StartPoint.Y, 6);
        Assert.Equal(95.0, negLine.EndPoint.X, 6);
        Assert.Equal(40.0, negLine.EndPoint.Y, 6);
    }

    [Fact]
    public void StraightSplit_Name()
    {
        Assert.Equal("Straight", new StraightSplit().Name);
    }
}
