using OpenNest.CNC;

namespace OpenNest.Tests;

public class CutOffTests
{
    [Fact]
    public void Drawing_IsCutOff_DefaultsFalse()
    {
        var drawing = new Drawing("test", new Program());
        Assert.False(drawing.IsCutOff);
    }

    [Fact]
    public void Plate_CutOffPart_DoesNotIncrementQuantity()
    {
        var drawing = new Drawing("cutoff", new Program()) { IsCutOff = true };
        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(drawing));
        Assert.Equal(0, drawing.Quantity.Nested);
    }

    [Fact]
    public void Plate_Utilization_ExcludesCutOffParts()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Geometry.Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(0, 0)));
        var realDrawing = new Drawing("real", pgm);
        var cutoffDrawing = new Drawing("cutoff", new Program()) { IsCutOff = true };

        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(realDrawing));
        plate.Parts.Add(new Part(cutoffDrawing));

        var utilization = plate.Utilization();
        var expected = realDrawing.Area / plate.Area();
        Assert.Equal(expected, utilization, 5);
    }

    [Fact]
    public void Plate_HasOverlappingParts_SkipsCutOffParts()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Geometry.Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(0, 0)));

        var realDrawing = new Drawing("real", pgm);
        var cutoffDrawing = new Drawing("cutoff", pgm) { IsCutOff = true };

        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(realDrawing));
        plate.Parts.Add(new Part(cutoffDrawing));

        var hasOverlap = plate.HasOverlappingParts(out var pts);
        Assert.False(hasOverlap);
    }
}
