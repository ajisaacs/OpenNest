using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class LeadInAssignerTests
{
    private static Part MakeSquarePartAt(double x, double y)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        return new Part(drawing, new Vector(x, y));
    }

    [Fact]
    public void Assign_SetsHasManualLeadInsOnAllParts()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(MakeSquarePartAt(10, 10));
        plate.Parts.Add(MakeSquarePartAt(30, 30));
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        Assert.All(plate.Parts, p => Assert.True(p.HasManualLeadIns));
    }

    [Fact]
    public void Assign_SkipsLockedParts()
    {
        var plate = new Plate(60, 120);
        var lockedPart = MakeSquarePartAt(10, 10);
        lockedPart.LeadInsLocked = true;
        lockedPart.HasManualLeadIns = true;
        var originalProgram = lockedPart.Program;

        plate.Parts.Add(lockedPart);
        plate.Parts.Add(MakeSquarePartAt(30, 30));
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        Assert.Same(originalProgram, lockedPart.Program);
    }

    [Fact]
    public void Assign_RemovesExistingLeadInsBeforeReapply()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(MakeSquarePartAt(10, 10));
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);
        var countAfterFirst = plate.Parts[0].Program.Codes.Count;

        assigner.Assign(plate);
        var countAfterSecond = plate.Parts[0].Program.Codes.Count;

        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    [Fact]
    public void Assign_PartsContainLeadinLayerCodes()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(MakeSquarePartAt(10, 10));
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        var hasLeadin = plate.Parts[0].Program.Codes.OfType<LinearMove>().Any(m => m.Layer == LayerType.Leadin);
        Assert.True(hasLeadin);
    }
}
