using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine;
using OpenNest.Engine.RapidPlanning;
using OpenNest.Engine.Sequencing;

namespace OpenNest.Tests.Engine;

public class PlateProcessorTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y, size: 2);

    [Fact]
    public void Process_ReturnsAllParts()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(MakePartAt(10, 10));
        plate.Parts.Add(MakePartAt(30, 30));
        plate.Parts.Add(MakePartAt(50, 50));

        var processor = new PlateProcessor
        {
            Sequencer = new RightSideSequencer(),
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        Assert.Equal(3, result.Parts.Count);
    }

    [Fact]
    public void Process_PreservesSequenceOrder()
    {
        var plate = new Plate(60, 120);
        var left = MakePartAt(5, 10);
        var right = MakePartAt(50, 10);
        plate.Parts.Add(left);
        plate.Parts.Add(right);

        var processor = new PlateProcessor
        {
            Sequencer = new RightSideSequencer(),
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        Assert.Same(right, result.Parts[0].Part);
        Assert.Same(left, result.Parts[1].Part);
    }

    [Fact]
    public void Process_SkipsCuttingStrategy_WhenManualLeadIns()
    {
        var plate = new Plate(60, 120);
        var part = MakePartAt(10, 10);
        part.HasManualLeadIns = true;
        plate.Parts.Add(part);

        var processor = new PlateProcessor
        {
            Sequencer = new LeftSideSequencer(),
            CuttingStrategy = new ContourCuttingStrategy
            {
                Parameters = new CuttingParameters()
            },
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        Assert.Same(part.Program, result.Parts[0].ProcessedProgram);
    }

    [Fact]
    public void Process_DoesNotMutatePart()
    {
        var plate = new Plate(60, 120);
        var part = MakePartAt(10, 10);
        var originalProgram = part.Program;
        plate.Parts.Add(part);

        var processor = new PlateProcessor
        {
            Sequencer = new LeftSideSequencer(),
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        Assert.Same(originalProgram, part.Program);
    }

    [Fact]
    public void Process_NoCuttingStrategy_PassesProgramThrough()
    {
        var plate = new Plate(60, 120);
        var part = MakePartAt(10, 10);
        plate.Parts.Add(part);

        var processor = new PlateProcessor
        {
            Sequencer = new LeftSideSequencer(),
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        Assert.Same(part.Program, result.Parts[0].ProcessedProgram);
    }

    [Fact]
    public void Process_EmptyPlate_ReturnsEmptyResult()
    {
        var plate = new Plate(60, 120);

        var processor = new PlateProcessor
        {
            Sequencer = new LeftSideSequencer(),
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        Assert.Empty(result.Parts);
    }
}
