using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

/// <summary>
/// Characterizes a single-drawing, zero-plate .nest archive through the unchanged
/// writer and reader, which drawing packages rely on.
/// </summary>
public class ZeroPlateNestTests
{
    [Fact]
    public void SingleDrawingWithoutPlates_RoundTripsTheDrawing()
    {
        var drawing = DrawingPackageFixture.Build();
        var nest = new Nest(drawing.Name) { Units = Units.Millimeters };
        nest.Drawings.Add(drawing);
        using var stream = new MemoryStream();

        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        var reader = new NestReader(stream);
        var restored = reader.Read();

        Assert.Empty(reader.Warnings);
        Assert.Empty(restored.Plates);
        Assert.Equal(Units.Millimeters, restored.Units);
        var copy = Assert.Single(restored.Drawings);
        Assert.Equal(NestWriter.GetProgramText(drawing.Program), NestWriter.GetProgramText(copy.Program));
        Assert.Equal(NestWriter.GetSubProgramsText(drawing.Program), NestWriter.GetSubProgramsText(copy.Program));
        Assert.Equal(drawing.Name, copy.Name);
        Assert.Equal(drawing.Customer, copy.Customer);
        Assert.Equal((drawing.Material.Name, drawing.Material.Grade, drawing.Material.Density),
            (copy.Material.Name, copy.Material.Grade, copy.Material.Density));
        Assert.Equal(drawing.Priority, copy.Priority);
        Assert.Equal(drawing.Quantity.Required, copy.Quantity.Required);
        Assert.Equal(drawing.Source.Path, copy.Source.Path);
        Assert.Equal(drawing.Source.Offset, copy.Source.Offset);
        Assert.Equal(drawing.SourceEntities.Select(e => (e.Id, e.Type, e.Layer?.Name)),
            copy.SourceEntities.Select(e => (e.Id, e.Type, e.Layer?.Name)));
        Assert.True(drawing.SuppressedEntityIds.SetEquals(copy.SuppressedEntityIds));
        var bend = Assert.Single(copy.Bends);
        Assert.Equal((new Vector(0, 3), new Vector(10, 3), (double?)90, (double?)0.125, "UP 90"),
            (bend.StartPoint, bend.EndPoint, bend.Angle, bend.Radius, bend.NoteText));
    }
}
