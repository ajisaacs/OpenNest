using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Engine;

public class CutoutPipelinePersistenceTests
{
    [Fact]
    public void ExpandedMultiSheetProposalCommitsAndReloadsAsSeparateDrawings()
    {
        var frame = new OpenNest.Shapes.RingShape { OuterDiameter = 20, InnerDiameter = 10 }
            .GetDrawing();
        frame.Name = "neutral-frame";
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(6, 0);
        program.LineTo(6, 6);
        program.LineTo(0, 6);
        program.LineTo(0, 0);
        var insert = new Drawing("neutral-insert", program);
        var request = new NestPipelineRequest("Irregular", new[] {
            new NestItem { Drawing = frame, Quantity = 2 },
            new NestItem { Drawing = insert, Quantity = 2 } },
            new[] { new NestPlateStock("sheet", new Size(22, 22), 2, 0.25) },
            new NestJobOptions(maxPlates: 2));
        var ordinary = NestPipeline.Run(request);
        Assert.Contains(ordinary.Raw.Fulfillment, f => f.Unplaced > 0);
        var result = NestPipeline.RunCutoutPreview(request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(2, result.Raw.Plates.Count);
        Assert.All(result.Raw.Plates, p => Assert.Equal(new[] { "part-0", "part-1" },
            p.Placements.Select(x => x.PartId)));

        var nest = new Nest();
        nest.Drawings.Add(frame);
        nest.Drawings.Add(insert);
        using (var manager = new PlateManager(nest))
        {
            var committed = NestPipelineCommit.ApplyToEmptyPlates(result, manager);
            Assert.Equal(2, committed.Count);
            Assert.All(committed, plate => Assert.Equal(new[] { frame, insert },
                plate.Parts.Select(p => p.BaseDrawing)));
        }
        Assert.Equal(2, frame.Quantity.Nested);
        Assert.Equal(2, insert.Quantity.Nested);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        var loaded = new NestReader(new MemoryStream(stream.ToArray())).Read();
        Assert.Equal(2, loaded.Plates.Count);
        for (var i = 0; i < loaded.Plates.Count; i++)
        {
            var expected = nest.Plates[i];
            var actual = loaded.Plates[i];
            Assert.Equal(expected.Size, actual.Size);
            Assert.Equal(expected.PartSpacing, actual.PartSpacing);
            Assert.Equal(expected.Quadrant, actual.Quadrant);
            Assert.Equal(expected.EdgeSpacing, actual.EdgeSpacing);
            Assert.Equal(new[] { "neutral-frame", "neutral-insert" },
                actual.Parts.Select(p => p.BaseDrawing.Name));
            for (var j = 0; j < actual.Parts.Count; j++)
            {
                Assert.Equal(expected.Parts[j].Location.X, actual.Parts[j].Location.X, 6);
                Assert.Equal(expected.Parts[j].Location.Y, actual.Parts[j].Location.Y, 6);
                Assert.Equal(expected.Parts[j].Rotation, actual.Parts[j].Rotation, 6);
            }
        }
        Assert.Equal(2, loaded.Drawings.Single(d => d.Name == "neutral-frame").Quantity.Nested);
        Assert.Equal(2, loaded.Drawings.Single(d => d.Name == "neutral-insert").Quantity.Nested);
    }
}
