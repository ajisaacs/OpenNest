using System.Linq;
using System.Threading;
using OpenNest.Engine.NestingEngines.FlatBar;

namespace OpenNest.Engine.Tests.NestingEngines;

public class BarPackerTests
{
    [Fact]
    public void SwapOptimizationStopsWhenOnlyIdenticalCopiesRemain()
    {
        // One bar holds [6, 1.5, 1.5] with 0.99 free; two 1.5 copies are left over. Trading a
        // placed copy for an unplaced one changes nothing, but a trial charges the copy's
        // self-nesting gain again and looked 0.01 fuller, so the swap used to repeat forever.
        var items = new[]
        {
            new BarItem(0, 6, 0, 0),
            new BarItem(1, 1.5, 0, 0.01),
            new BarItem(1, 1.5, 0, 0.01),
            new BarItem(1, 1.5, 0, 0.01),
            new BarItem(1, 1.5, 0, 0.01),
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var plan = BarPacker.Pack(items, [10.0], 1, timeout.Token);

        var bar = Assert.Single(plan.Bars);
        Assert.Equal(new[] { 0, 1, 1 }, bar.Items.Select(i => i.Type).OrderBy(t => t));
        Assert.Equal(2, plan.Unplaced.Count);
    }
}
