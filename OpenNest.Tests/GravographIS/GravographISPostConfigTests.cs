using OpenNest.CNC;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.GravographIS;

public class GravographISPostConfigTests
{
    [Fact]
    public void Defaults_EngraveBlock_NoPause_FasterFeed()
    {
        var config = new GravographISPostConfig();

        Assert.Equal(10, config.Engrave.FeedMmPerSec);
        Assert.False(config.Engrave.PauseBefore);
    }

    [Fact]
    public void Defaults_CutBlock_PausesToChangeTool()
    {
        var config = new GravographISPostConfig();

        Assert.Equal(3, config.Cut.FeedMmPerSec);
        Assert.True(config.Cut.PauseBefore);
        Assert.Equal("Change tool", config.Cut.PauseMessage);
    }

    [Theory]
    [InlineData(LayerType.Scribe)]
    public void ConfigFor_Scribe_ReturnsEngraveBlock(LayerType layer)
    {
        var config = new GravographISPostConfig();
        Assert.Same(config.Engrave, config.ConfigFor(layer));
    }

    [Theory]
    [InlineData(LayerType.Cut)]
    [InlineData(LayerType.Leadin)]
    [InlineData(LayerType.Leadout)]
    public void ConfigFor_CutLayers_ReturnCutBlock(LayerType layer)
    {
        var config = new GravographISPostConfig();
        Assert.Same(config.Cut, config.ConfigFor(layer));
    }

    [Fact]
    public void ConfigFor_Display_ReturnsNull_SoItIsSkipped()
    {
        var config = new GravographISPostConfig();
        Assert.Null(config.ConfigFor(LayerType.Display));
    }
}
