using OpenNest.Engine.Sequencing;

namespace OpenNest.Tests.Sequencing;

public class DirectionalSequencerTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y);
    private static Plate MakePlate(params Part[] parts) => TestHelpers.MakePlate(60, 120, parts);

    [Fact]
    public void RightSide_SortsXDescending()
    {
        var a = MakePartAt(10, 5);
        var b = MakePartAt(30, 5);
        var c = MakePartAt(20, 5);
        var plate = MakePlate(a, b, c);

        var sequencer = new RightSideSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(b, result[0].Part);
        Assert.Same(c, result[1].Part);
        Assert.Same(a, result[2].Part);
    }

    [Fact]
    public void LeftSide_SortsXAscending()
    {
        var a = MakePartAt(10, 5);
        var b = MakePartAt(30, 5);
        var c = MakePartAt(20, 5);
        var plate = MakePlate(a, b, c);

        var sequencer = new LeftSideSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(a, result[0].Part);
        Assert.Same(c, result[1].Part);
        Assert.Same(b, result[2].Part);
    }

    [Fact]
    public void BottomSide_SortsYAscending()
    {
        var a = MakePartAt(5, 20);
        var b = MakePartAt(5, 5);
        var c = MakePartAt(5, 10);
        var plate = MakePlate(a, b, c);

        var sequencer = new BottomSideSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(b, result[0].Part);
        Assert.Same(c, result[1].Part);
        Assert.Same(a, result[2].Part);
    }

    [Fact]
    public void RightSide_TiesBrokenByPerpendicularAxis()
    {
        var a = MakePartAt(10, 20);
        var b = MakePartAt(10, 5);
        var plate = MakePlate(a, b);

        var sequencer = new RightSideSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(b, result[0].Part);
        Assert.Same(a, result[1].Part);
    }
}
