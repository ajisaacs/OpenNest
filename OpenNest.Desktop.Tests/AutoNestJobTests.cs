using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests;

public class AutoNestJobTests
{
    private static Drawing Square(string name, double side, int required)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(side, 0)));
        program.Codes.Add(new LinearMove(new Vector(side, side)));
        program.Codes.Add(new LinearMove(new Vector(0, side)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing(name, program);
        drawing.Quantity.Required = required;
        return drawing;
    }

    private static Nest NestWith(params Drawing[] drawings)
    {
        var nest = new Nest { Name = "job" };
        foreach (var drawing in drawings)
            nest.Drawings.Add(drawing);
        nest.Plates.Add(new Plate(new Size(48, 96)) { Quantity = 1 });
        return nest;
    }

    [Fact]
    public void ForRemaining_AsksForWhatIsNotPlacedYet_OnTheTemplateStock()
    {
        var square = Square("square", 10, 5);
        var nest = NestWith(square);
        nest.Plates[0].Parts.Add(new Part(square));

        var request = AutoNestJob.ForRemaining(nest, nest.Plates[0], "Default");

        var item = Assert.Single(request.Items);
        Assert.Same(square, item.Drawing);
        Assert.Equal(4, item.Quantity);
        Assert.Equal("Default", request.EngineName);
        Assert.Equal(nest.Plates[0].Size, Assert.Single(request.Stock).Size);
    }

    [Fact]
    public void ForRemaining_SkipsCutOffsAndPlacedDrawings_AndIsNullWhenNothingRemains()
    {
        var placed = Square("placed", 10, 1);
        var cutOff = Square("cut-off", 1, 3);
        cutOff.IsCutOff = true;
        var nest = NestWith(placed, cutOff);
        nest.Plates[0].Parts.Add(new Part(placed));

        Assert.Null(AutoNestJob.ForRemaining(nest, nest.Plates[0], "Default"));
    }

    [Fact]
    public void ValidResult_IsCommittedOntoTheEmptyPlate()
    {
        var square = Square("square", 10, 6);
        var nest = NestWith(square);
        var request = AutoNestJob.ForRemaining(nest, nest.Plates[0], "Default");

        var result = NestPipeline.Run(request);
        var kept = AutoNestJob.Keep(nest, result);

        Assert.True(kept.Nested, kept.Note);
        Assert.Null(AutoNestJob.RefusalReason(result));
        var plate = Assert.Single(nest.Plates);
        Assert.Equal(6, plate.Parts.Count);
        Assert.Equal(0, square.Quantity.Remaining);
    }

    [Fact]
    public void ResultPlacingNothing_LeavesTheNestUnchanged()
    {
        var tooBig = Square("too big", 200, 1);
        var nest = NestWith(tooBig);
        var request = AutoNestJob.ForRemaining(nest, nest.Plates[0], "Default");

        var result = NestPipeline.Run(request);
        var kept = AutoNestJob.Keep(nest, result);

        Assert.False(kept.Nested);
        var reason = AutoNestJob.RefusalReason(result);
        Assert.StartsWith("Auto Nest placed nothing; nothing was changed. Not placed: ", reason);
        Assert.Contains("too big", reason);
        Assert.Empty(Assert.Single(nest.Plates).Parts);
        Assert.Equal(1, tooBig.Quantity.Remaining);
    }
}
