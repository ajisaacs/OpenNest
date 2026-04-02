using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.IO;
using System.Linq;

namespace OpenNest.Tests.IO;

public class NestBendSerializationTests
{
    [Fact]
    public void Bends_SurviveNestRoundtrip()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        drawing.Bends.Add(new Bend
        {
            StartPoint = new Vector(0, 5),
            EndPoint = new Vector(10, 5),
            Direction = BendDirection.Up,
            Angle = 90,
            Radius = 0.06,
            NoteText = "UP 90° R0.06"
        });
        drawing.Bends.Add(new Bend
        {
            StartPoint = new Vector(0, 3),
            EndPoint = new Vector(10, 3),
            Direction = BendDirection.Down,
            Angle = 45.5,
            Radius = 0.125,
            NoteText = "DOWN 45.5° R0.125"
        });

        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var plate = new Plate(60, 120);
        plate.GrainAngle = 0.5236;
        plate.Parts.Add(new Part(drawing, new Vector(0, 0)));
        nest.Plates.Add(plate);

        using var ms = new MemoryStream();
        var writer = new NestWriter(nest);
        writer.Write(ms);

        ms.Position = 0;
        var reader = new NestReader(ms);
        var loaded = reader.Read();

        var loadedDrawing = loaded.Drawings.First();
        Assert.Equal(2, loadedDrawing.Bends.Count);

        var bend1 = loadedDrawing.Bends[0];
        Assert.Equal(0, bend1.StartPoint.X, 0.001);
        Assert.Equal(5, bend1.StartPoint.Y, 0.001);
        Assert.Equal(10, bend1.EndPoint.X, 0.001);
        Assert.Equal(5, bend1.EndPoint.Y, 0.001);
        Assert.Equal(BendDirection.Up, bend1.Direction);
        Assert.Equal(90, bend1.Angle);
        Assert.Equal(0.06, bend1.Radius);
        Assert.Equal("UP 90° R0.06", bend1.NoteText);

        var bend2 = loadedDrawing.Bends[1];
        Assert.Equal(BendDirection.Down, bend2.Direction);
        Assert.Equal(45.5, bend2.Angle);

        var loadedPlate = loaded.Plates[0];
        Assert.Equal(0.5236, loadedPlate.GrainAngle, 0.0001);
    }

    [Fact]
    public void NoBends_SurviveNestRoundtrip()
    {
        var drawing = TestHelpers.MakeSquareDrawing();

        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var plate = new Plate(60, 120);
        plate.Parts.Add(new Part(drawing, new Vector(0, 0)));
        nest.Plates.Add(plate);

        using var ms = new MemoryStream();
        var writer = new NestWriter(nest);
        writer.Write(ms);

        ms.Position = 0;
        var reader = new NestReader(ms);
        var loaded = reader.Read();

        Assert.Empty(loaded.Drawings.First().Bends);
        Assert.Equal(0, loaded.Plates[0].GrainAngle);
    }
}
