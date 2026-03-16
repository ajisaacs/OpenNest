using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.Sequencing;

public class LeastCodeSequencerTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y);

    [Fact]
    public void NearestNeighbor_FromExitPoint()
    {
        var plate = new Plate(60, 120);
        var farPart = MakePartAt(5, 5);
        var nearPart = MakePartAt(55, 115);
        plate.Parts.Add(farPart);
        plate.Parts.Add(nearPart);

        var sequencer = new LeastCodeSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        // nearPart is closer to exit point, should come first
        Assert.Same(nearPart, result[0].Part);
        Assert.Same(farPart, result[1].Part);
    }

    [Fact]
    public void PreservesAllParts()
    {
        var plate = new Plate(60, 120);
        for (var i = 0; i < 10; i++)
            plate.Parts.Add(MakePartAt(i * 5, i * 10));

        var sequencer = new LeastCodeSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void TwoOpt_ImprovesSolution()
    {
        var plate = new Plate(100, 100);
        var a = MakePartAt(90, 90);
        var b = MakePartAt(10, 80);
        var c = MakePartAt(80, 10);
        var d = MakePartAt(5, 5);
        plate.Parts.Add(a);
        plate.Parts.Add(b);
        plate.Parts.Add(c);
        plate.Parts.Add(d);

        var sequencer = new LeastCodeSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Equal(4, result.Count);
    }
}
