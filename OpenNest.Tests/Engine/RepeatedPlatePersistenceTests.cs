using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Engine;

public class RepeatedPlatePersistenceTests
{
    [Theory]
    [InlineData("Rectangles", 9)]
    [InlineData("Irregular", 9)]
    [InlineData("Default", 9)]
    [InlineData("Rectangles", 5)]
    [InlineData("Irregular", 5)]
    [InlineData("Default", 5)]
    public void RealEngineRepeatedAndPartialLayoutsCommitAndReloadWeightedQuantities(string engine, int quantity)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(2, 0);
        program.LineTo(2, 2);
        program.LineTo(0, 2);
        program.LineTo(0, 0);
        var drawing = new Drawing("neutral-square", program);
        var before = PartGeometrySnapshot.FromProgram(drawing.Program).Motions;
        var item = new NestItem
        {
            Drawing = drawing,
            Quantity = quantity,
            StepAngle = OpenNest.Math.Angle.TwoPI,
            RotationStart = 0,
            RotationEnd = 0
        };
        var result = NestPipeline.Run(new NestPipelineRequest(engine, [item],
            [new NestPlateStock("sheet", new Size(4.5, 4.5), 3, 0.25)]));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(quantity / 4 + 1, result.Raw.Plates.Count);
        Assert.Equal(quantity, result.Raw.Plates.Sum(p => p.Placements.Count));
        Assert.Equal(Enumerable.Range(0, quantity), result.Raw.Plates.SelectMany(p => p.Placements).Select(p => p.InstanceIndex));
        Assert.Equal(Enumerable.Range(0, result.Raw.Plates.Count), result.Raw.Plates.Select(p => p.PlateIndex));
        Assert.Equal(result.Raw.Plates.Count, Assert.Single(result.Raw.StockUsage).Used);
        Assert.Equal(quantity, Assert.Single(result.Raw.Fulfillment).Placed);
        Assert.Equal(0, drawing.Quantity.Nested);
        Assert.Equal(before, PartGeometrySnapshot.FromProgram(drawing.Program).Motions);
        Assert.Equal(quantity, item.Quantity);

        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var occupied = nest.CreatePlate();
        occupied.Quantity = 2;
        var existing = new Part(drawing);
        occupied.Parts.Add(existing);
        var empty = nest.CreatePlate();
        empty.Quantity = 7;
        using (var manager = new PlateManager(nest))
        {
            var applied = NestPipelineCommit.ApplyToEmptyPlates(result, manager);
            Assert.Equal(new[] { quantity / 4, 1 }, applied.Select(p => p.Quantity));
            Assert.Equal(new[] { 4, 1 }, applied.Select(p => p.Parts.Count));
            Assert.Same(empty, applied[0]);
            Assert.Same(existing, Assert.Single(occupied.Parts));
            Assert.Equal(2, occupied.Quantity);
        }
        Assert.Equal(quantity + 2, drawing.Quantity.Nested);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        var loaded = new NestReader(new MemoryStream(stream.ToArray())).Read();
        Assert.Equal(new[] { 2, quantity / 4, 1 }, loaded.Plates.Select(p => p.Quantity));
        Assert.Equal(quantity + 2, Assert.Single(loaded.Drawings).Quantity.Nested);
        Assert.Equal(result.Raw.Plates.Count + 2, loaded.Plates.Sum(p => p.Quantity));
        for (var i = 0; i < loaded.Plates.Count; i++)
        {
            Assert.Equal(nest.Plates[i].Size, loaded.Plates[i].Size);
            Assert.Equal(nest.Plates[i].EdgeSpacing, loaded.Plates[i].EdgeSpacing);
            Assert.Equal(nest.Plates[i].PartSpacing, loaded.Plates[i].PartSpacing);
            Assert.Equal(nest.Plates[i].Quadrant, loaded.Plates[i].Quadrant);
            Assert.Equal(nest.Plates[i].Parts.Select(p => (p.Location, p.Rotation)),
                loaded.Plates[i].Parts.Select(p => (p.Location, p.Rotation)));
        }
    }
}
