using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingStrategy;

public class LeadInLayerTagTests
{
    private static readonly Vector Point = new(5, 5);
    private const double Normal = 0.0;

    [Fact]
    public void LineLeadIn_SetsLeadinLayer()
    {
        var leadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 };
        var codes = leadIn.Generate(Point, Normal);
        var linear = codes.OfType<LinearMove>().Single();
        Assert.Equal(LayerType.Leadin, linear.Layer);
    }

    [Fact]
    public void ArcLeadIn_SetsLeadinLayer()
    {
        var leadIn = new ArcLeadIn { Radius = 0.25 };
        var codes = leadIn.Generate(Point, Normal);
        var arc = codes.OfType<ArcMove>().Single();
        Assert.Equal(LayerType.Leadin, arc.Layer);
    }

    [Fact]
    public void LineArcLeadIn_SetsLeadinLayerOnAllMoves()
    {
        var leadIn = new LineArcLeadIn { LineLength = 0.5, ArcRadius = 0.25, ApproachAngle = 135 };
        var codes = leadIn.Generate(Point, Normal);
        Assert.All(codes.OfType<LinearMove>(), m => Assert.Equal(LayerType.Leadin, m.Layer));
        Assert.All(codes.OfType<ArcMove>(), m => Assert.Equal(LayerType.Leadin, m.Layer));
    }

    [Fact]
    public void CleanHoleLeadIn_SetsLeadinLayerOnAllMoves()
    {
        var leadIn = new CleanHoleLeadIn { LineLength = 0.5, ArcRadius = 0.25, Kerf = 0.05 };
        var codes = leadIn.Generate(Point, Normal);
        Assert.All(codes.OfType<LinearMove>(), m => Assert.Equal(LayerType.Leadin, m.Layer));
        Assert.All(codes.OfType<ArcMove>(), m => Assert.Equal(LayerType.Leadin, m.Layer));
    }

    [Fact]
    public void LineLineLeadIn_SetsLeadinLayerOnAllMoves()
    {
        var leadIn = new LineLineLeadIn { Length1 = 0.5, Length2 = 0.3, ApproachAngle1 = 90, ApproachAngle2 = 90 };
        var codes = leadIn.Generate(Point, Normal);
        Assert.All(codes.OfType<LinearMove>(), m => Assert.Equal(LayerType.Leadin, m.Layer));
    }

    [Fact]
    public void NoLeadIn_NoLinearOrArcMoves()
    {
        var leadIn = new NoLeadIn();
        var codes = leadIn.Generate(Point, Normal);
        Assert.Empty(codes.OfType<LinearMove>());
        Assert.Empty(codes.OfType<ArcMove>());
    }

    [Fact]
    public void LineLeadOut_SetsLeadoutLayer()
    {
        var leadOut = new LineLeadOut { Length = 0.5, ApproachAngle = 90 };
        var codes = leadOut.Generate(Point, Normal);
        var linear = codes.OfType<LinearMove>().Single();
        Assert.Equal(LayerType.Leadout, linear.Layer);
    }

    [Fact]
    public void ArcLeadOut_SetsLeadoutLayer()
    {
        var leadOut = new ArcLeadOut { Radius = 0.25 };
        var codes = leadOut.Generate(Point, Normal);
        var arc = codes.OfType<ArcMove>().Single();
        Assert.Equal(LayerType.Leadout, arc.Layer);
    }

    [Fact]
    public void NoLeadOut_ReturnsEmptyList()
    {
        var leadOut = new NoLeadOut();
        var codes = leadOut.Generate(Point, Normal);
        Assert.Empty(codes);
    }
}
