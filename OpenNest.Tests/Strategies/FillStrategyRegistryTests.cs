using System.Linq;
using OpenNest.Engine.Strategies;

namespace OpenNest.Tests.Strategies;

public class FillStrategyRegistryTests
{
    [Fact]
    public void Registry_DiscoversBuiltInStrategies()
    {
        var strategies = FillStrategyRegistry.Strategies;

        Assert.True(strategies.Count >= 6, $"Expected at least 6 built-in strategies, got {strategies.Count}");
        Assert.Contains(strategies, s => s.Name == "Pairs");
        Assert.Contains(strategies, s => s.Name == "RectBestFit");
        Assert.Contains(strategies, s => s.Name == "Extents");
        Assert.Contains(strategies, s => s.Name == "Linear");
        Assert.Contains(strategies, s => s.Name == "Row");
        Assert.Contains(strategies, s => s.Name == "Column");
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

    [Fact]
    public void Registry_RowAndColumnOrderedBetweenPairsAndRectBestFit()
    {
        var strategies = FillStrategyRegistry.Strategies;
        var pairsOrder = strategies.First(s => s.Name == "Pairs").Order;
        var rectOrder = strategies.First(s => s.Name == "RectBestFit").Order;
        var rowOrder = strategies.First(s => s.Name == "Row").Order;
        var colOrder = strategies.First(s => s.Name == "Column").Order;

        Assert.True(rowOrder > pairsOrder, "Row should run after Pairs");
        Assert.True(colOrder > pairsOrder, "Column should run after Pairs");
        Assert.True(rowOrder < rectOrder, "Row should run before RectBestFit");
        Assert.True(colOrder < rectOrder, "Column should run before RectBestFit");
    }
}
