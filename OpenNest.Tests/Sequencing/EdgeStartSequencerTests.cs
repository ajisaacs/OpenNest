using OpenNest.Engine.Sequencing;

namespace OpenNest.Tests.Sequencing;

public class EdgeStartSequencerTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y);

    [Fact]
    public void SortsByDistanceFromNearestEdge()
    {
        var plate = new Plate(60, 120);
        var edgePart = MakePartAt(1, 1);
        var centerPart = MakePartAt(25, 25);
        var midPart = MakePartAt(10, 10);
        plate.Parts.Add(edgePart);
        plate.Parts.Add(centerPart);
        plate.Parts.Add(midPart);

        var sequencer = new EdgeStartSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(edgePart, result[0].Part);
        Assert.Same(midPart, result[1].Part);
        Assert.Same(centerPart, result[2].Part);
    }
}
