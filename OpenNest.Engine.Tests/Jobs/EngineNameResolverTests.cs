using OpenNest.Engine.Jobs;
using Xunit;

namespace OpenNest.Engine.Tests.Jobs;

public class EngineNameResolverTests
{
    [Theory]
    [InlineData("Fill", "Fill")]
    [InlineData(" strip ", "Strip")]
    [InlineData("Default", "Fill")]
    [InlineData(null, "Fill")]
    [InlineData("  ", "Fill")]
    public void FillStrategiesWinAndAreCanonicalized(string name, string expected)
    {
        var result = EngineNameResolver.Classify(name);

        Assert.Equal(EngineNameKind.FillStrategy, result.Kind);
        Assert.Equal(expected, result.FillStrategy);
    }

    [Theory]
    [InlineData("StockLadder")]
    [InlineData(" irregular ")]
    [InlineData("Opus55NestingEngine")]
    public void WholeJobEnginesAreRecognized(string name)
    {
        var result = EngineNameResolver.Classify(name);

        Assert.Equal(EngineNameKind.JobEngine, result.Kind);
        Assert.Null(result.FillStrategy);
        Assert.Equal(name.Trim(), result.Name);
    }

    [Fact]
    public void UnknownNamesAreUnknown()
    {
        var result = EngineNameResolver.Classify("Mystery Engine");

        Assert.Equal(EngineNameKind.Unknown, result.Kind);
        Assert.Null(result.FillStrategy);
    }
}
