namespace OpenNest.Tests.Strategies;

public class FillStrategyRegistryTests
{
    [Fact]
    public void Registry_DiscoversBuiltInStrategies()
    {
        var strategies = FillStrategyRegistry.Strategies;

        Assert.True(strategies.Count >= 4, $"Expected at least 4 built-in strategies, got {strategies.Count}");
        Assert.Contains(strategies, s => s.Name == "Pairs");
        Assert.Contains(strategies, s => s.Name == "RectBestFit");
        Assert.Contains(strategies, s => s.Name == "Extents");
        Assert.Contains(strategies, s => s.Name == "Linear");
    }

    [Fact]
    public void Registry_StrategiesAreOrderedByOrder()
    {
        var strategies = FillStrategyRegistry.Strategies;

        for (var i = 1; i < strategies.Count; i++)
            Assert.True(strategies[i].Order >= strategies[i - 1].Order,
                $"Strategy '{strategies[i].Name}' (Order={strategies[i].Order}) should not precede '{strategies[i - 1].Name}' (Order={strategies[i - 1].Order})");
    }

    [Fact]
    public void Registry_LinearIsLast()
    {
        var strategies = FillStrategyRegistry.Strategies;
        var last = strategies[strategies.Count - 1];

        Assert.Equal("Linear", last.Name);
    }
}
