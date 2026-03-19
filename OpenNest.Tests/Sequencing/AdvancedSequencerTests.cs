using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.Sequencing;

namespace OpenNest.Tests.Sequencing;

public class AdvancedSequencerTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y);

    [Fact]
    public void GroupsIntoRows_NoAlternate()
    {
        var plate = new Plate(100, 100);
        var row1a = MakePartAt(10, 10);
        var row1b = MakePartAt(30, 10);
        var row2a = MakePartAt(10, 50);
        var row2b = MakePartAt(30, 50);
        plate.Parts.Add(row1a);
        plate.Parts.Add(row1b);
        plate.Parts.Add(row2a);
        plate.Parts.Add(row2b);

        var parameters = new SequenceParameters
        {
            Method = SequenceMethod.Advanced,
            MinDistanceBetweenRowsColumns = 5.0,
            AlternateRowsColumns = false
        };
        var sequencer = new AdvancedSequencer(parameters);
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(row1a, result[0].Part);
        Assert.Same(row1b, result[1].Part);
        Assert.Same(row2a, result[2].Part);
        Assert.Same(row2b, result[3].Part);
    }

    [Fact]
    public void SerpentineAlternatesDirection()
    {
        var plate = new Plate(100, 100);
        var r1Left = MakePartAt(10, 10);
        var r1Right = MakePartAt(30, 10);
        var r2Left = MakePartAt(10, 50);
        var r2Right = MakePartAt(30, 50);
        plate.Parts.Add(r1Left);
        plate.Parts.Add(r1Right);
        plate.Parts.Add(r2Left);
        plate.Parts.Add(r2Right);

        var parameters = new SequenceParameters
        {
            Method = SequenceMethod.Advanced,
            MinDistanceBetweenRowsColumns = 5.0,
            AlternateRowsColumns = true
        };
        var sequencer = new AdvancedSequencer(parameters);
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(r1Left, result[0].Part);
        Assert.Same(r1Right, result[1].Part);
        Assert.Same(r2Right, result[2].Part);
        Assert.Same(r2Left, result[3].Part);
    }
}
