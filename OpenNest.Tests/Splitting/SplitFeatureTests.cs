using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Tests.Splitting;

public class SplitFeatureTests
{
    [Fact]
    public void WeldGapTabSplit_Vertical_TabsOnNegativeSide()
    {
        var feature = new WeldGapTabSplit();
        var line = new SplitLine(50.0, CutOffAxis.Vertical);
        var parameters = new SplitParameters
        {
            Type = SplitType.WeldGapTabs,
            TabWidth = 2.0,
            TabHeight = 0.25,
            TabCount = 2
        };

        var result = feature.GenerateFeatures(line, 0.0, 100.0, parameters);

        // Positive side (right): single straight line (no tabs)
        Assert.Single(result.PositiveSideEdge);
        Assert.IsType<Line>(result.PositiveSideEdge[0]);

        // Negative side (left): has tab protrusions — more than 1 entity
        Assert.True(result.NegativeSideEdge.Count > 1);

        // All entities should be lines
        Assert.All(result.NegativeSideEdge, e => Assert.IsType<Line>(e));

        // First entity starts at extent start, last ends at extent end
        var first = (Line)result.NegativeSideEdge[0];
        var last = (Line)result.NegativeSideEdge[^1];
        Assert.Equal(0.0, first.StartPoint.Y, 6);
        Assert.Equal(100.0, last.EndPoint.Y, 6);

        // Tabs protrude in the negative-X direction (left of split line)
        var tabEntities = result.NegativeSideEdge.Cast<Line>().ToList();
        var minX = tabEntities.Min(l => System.Math.Min(l.StartPoint.X, l.EndPoint.X));
        Assert.Equal(50.0 - 0.25, minX, 6); // tabHeight = 0.25
    }

    [Fact]
    public void WeldGapTabSplit_Name()
    {
        Assert.Equal("Weld-Gap Tabs", new WeldGapTabSplit().Name);
    }

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
