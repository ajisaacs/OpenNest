using OpenNest.CNC;

namespace OpenNest.Tests.CNC;

public class VariableDefinitionTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var v = new VariableDefinition("diameter", "0.3", 0.3);
        Assert.Equal("diameter", v.Name);
        Assert.Equal("0.3", v.Expression);
        Assert.Equal(0.3, v.Value);
        Assert.False(v.Inline);
        Assert.False(v.Global);
    }

    [Fact]
    public void Constructor_WithFlags_SetsFlags()
    {
        var v = new VariableDefinition("width", "48.0", 48.0, inline: true, global: true);
        Assert.True(v.Inline);
        Assert.True(v.Global);
    }

    [Fact]
    public void DefaultFlags_AreFalse()
    {
        var v = new VariableDefinition("x", "1", 1.0);
        Assert.False(v.Inline);
        Assert.False(v.Global);
    }
}
