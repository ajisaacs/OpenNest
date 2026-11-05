using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PlateAutoSizeTests
{
    private static Part MakeRectPart(double x, double y, double length, double width)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(length, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(length, width)));
        pgm.Codes.Add(new LinearMove(new Vector(0, width)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        var part = new Part(drawing);
        part.Offset(x, y);
        return part;
    }

    [Fact]
    public void AutoSize_SmallParts_RoundsUpToFactor()
    {
        var plate = new Plate(200, 200);
        plate.Parts.Add(MakeRectPart(0, 0, 10.3, 20.7));

        plate.AutoSize(0.25);

        Assert.Equal(10.5, plate.Size.Length, 4); // X axis
        Assert.Equal(20.75, plate.Size.Width, 4); // Y axis
    }

    [Fact]
    public void AutoSize_ZeroFactor_FitsExactly()
    {
        var plate = new Plate(200, 200);
        plate.Parts.Add(MakeRectPart(0, 0, 10.3, 20.7));

        plate.AutoSize(0);

        Assert.Equal(10.3, plate.Size.Length, 4);
        Assert.Equal(20.7, plate.Size.Width, 4);
    }

    [Fact]
    public void AutoSize_LargerThanFullSheet_RoundsUpInsteadOfPickingAStandardSheet()
    {
        var plate = new Plate(200, 200);
        plate.Parts.Add(MakeRectPart(0, 0, 100, 50));

        plate.AutoSize();

        Assert.Equal(100, plate.Size.Length);
        Assert.Equal(50, plate.Size.Width);
    }

    [Fact]
    public void AutoSize_FullSheet_ShrinksToParts()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(MakeRectPart(0, 0, 100.2, 55.6));

        plate.AutoSize();

        Assert.Equal(101, plate.Size.Length);
        Assert.Equal(56, plate.Size.Width);
    }

    [Fact]
    public void AutoSize_IncludesEdgeSpacing()
    {
        var plate = new Plate(200, 200) { EdgeSpacing = new Spacing(0, 0, 0.5, 0.5) };
        plate.Parts.Add(MakeRectPart(0, 0, 10, 20));

        plate.AutoSize(0.25);

        Assert.Equal(10.5, plate.Size.Length, 4);
        Assert.Equal(20.5, plate.Size.Width, 4);
    }

    [Fact]
    public void AutoSize_EmptyPlate_DoesNotModifySize()
    {
        var plate = new Plate(60, 120);

        plate.AutoSize();

        Assert.Equal(60, plate.Size.Width);
        Assert.Equal(120, plate.Size.Length);
    }

    [Fact]
    public void AutoSize_MultipleParts_UsesCombinedEnvelope()
    {
        var plate = new Plate(200, 200);
        plate.Parts.Add(MakeRectPart(0, 0, 30, 40));
        plate.Parts.Add(MakeRectPart(30, 0, 30, 40));
        plate.Parts.Add(MakeRectPart(0, 40, 60, 60)); // combined extent = 60 x 100

        plate.AutoSize();

        Assert.Equal(60, plate.Size.Length); // X
        Assert.Equal(100, plate.Size.Width); // Y
    }
}
