using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.GravographIS;

public class GravographISPostProcessorTests
{
    private static LayeredPolyline Poly(LayerType layer, params Vector[] pts)
        => new LayeredPolyline(new List<Vector>(pts), layer);

    [Fact]
    public void BuildPasses_EngraveAndCut_OrdersEngraveFirstThenCutWithPause()
    {
        var post = new GravographISPostProcessor();

        var passes = post.BuildPasses(new[]
        {
            Poly(LayerType.Cut, new Vector(0, 0), new Vector(1, 0)),
            Poly(LayerType.Scribe, new Vector(0, 0), new Vector(0, 1)),
        });

        Assert.Equal(2, passes.Count);

        Assert.Equal(post.Config.Engrave.FeedMmPerSec, passes[0].FeedMmPerSec);
        Assert.False(passes[0].PauseBefore);

        Assert.Equal(post.Config.Cut.FeedMmPerSec, passes[1].FeedMmPerSec);
        Assert.True(passes[1].PauseBefore);
        Assert.Equal("Change tool", passes[1].PauseMessage);
    }

    [Fact]
    public void BuildPasses_CutOnly_IsSinglePass()
    {
        var post = new GravographISPostProcessor();

        var passes = post.BuildPasses(new[]
        {
            Poly(LayerType.Cut, new Vector(0, 0), new Vector(1, 0)),
        });

        Assert.Single(passes);
        Assert.Equal(post.Config.Cut.FeedMmPerSec, passes[0].FeedMmPerSec);
    }

    [Fact]
    public void BuildPasses_SkipsDisplayGeometry()
    {
        var post = new GravographISPostProcessor();

        var passes = post.BuildPasses(new[]
        {
            Poly(LayerType.Display, new Vector(0, 0), new Vector(1, 0)),
            Poly(LayerType.Cut, new Vector(0, 0), new Vector(0, 1)),
        });

        Assert.Single(passes);
        Assert.Equal(post.Config.Cut.FeedMmPerSec, passes[0].FeedMmPerSec);
    }

    [Fact]
    public void Config_IsExposedThroughConfigurableInterface()
    {
        var post = new GravographISPostProcessor(new GravographISPostConfig());
        OpenNest.IConfigurablePostProcessor configurable = post;
        Assert.Same(post.Config, configurable.Config);
    }
}
