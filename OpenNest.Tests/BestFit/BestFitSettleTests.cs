using OpenNest.CNC;
using OpenNest.Engine.BestFit;

namespace OpenNest.Tests.BestFit;

/// <summary>
/// The pair search slides part 2 in from offsets on a step grid, so a pair that locks in a
/// corner needs a second, sideways slide to close the gap the grid leaves.
/// </summary>
public class BestFitSettleTests
{
    private const double Spacing = 0.25;
    private const double LegWidth = 1.3;

    /// <summary>10 x 3 channel: a 10 x 1 bar with a 1.3-wide, 2-tall leg at each end.</summary>
    private static Drawing Channel()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 3);
        program.LineTo(10 - LegWidth, 3);
        program.LineTo(10 - LegWidth, 1);
        program.LineTo(LegWidth, 1);
        program.LineTo(LegWidth, 3);
        program.LineTo(0, 3);
        program.LineTo(0, 0);
        return new Drawing("channel", program);
    }

    [Fact]
    public void InterlockedChannels_CloseTheGapTheStepGridLeaves()
    {
        // Two channels facing each other, one shifted by a leg width plus the spacing, so each
        // has a leg inside the other. That shift is not a multiple of the step.
        var results = new BestFitFinder(60, 120).FindBestFits(Channel(), Spacing, stepSize: 0.25);

        var best = results.First(r => r.Keep);

        Assert.Equal(10 + LegWidth + Spacing, best.BoundingWidth, 3);
        Assert.Equal(3 + 1 + Spacing, best.BoundingHeight, 3);
    }
}
