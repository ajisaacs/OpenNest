using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests;

public class PlateSnapToStandardSizeTests
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
    public void SnapToStandardSize_SmallParts_SnapsToIncrement()
    {
        var plate = new Plate(200, 200); // oversized starting size
        plate.Parts.Add(MakeRectPart(0, 0, 10, 20));

        var result = plate.SnapToStandardSize();

        // 10x20 is well below 48x48 MinSheet -> snap to integer increment.
        Assert.Null(result.MatchedLabel);
        Assert.Equal(10, plate.Size.Length); // X axis
        Assert.Equal(20, plate.Size.Width);  // Y axis
    }

    [Fact]
    public void SnapToStandardSize_SmallPartsWithFractionalIncrement_UsesIncrement()
    {
        var plate = new Plate(200, 200);
        plate.Parts.Add(MakeRectPart(0, 0, 10.3, 20.7));

        var result = plate.SnapToStandardSize(new PlateSizeOptions { SnapIncrement = 0.25 });

        Assert.Null(result.MatchedLabel);
        Assert.Equal(10.5, plate.Size.Length, 4);
        Assert.Equal(20.75, plate.Size.Width, 4);
    }

    [Fact]
    public void SnapToStandardSize_40x90Part_SnapsToStandard48x96_XLong()
    {
        // Part is 90 long (X) x 40 wide (Y) -> X is the long axis.
        var plate = new Plate(200, 200);
        plate.Parts.Add(MakeRectPart(0, 0, 90, 40));

        var result = plate.SnapToStandardSize();

        Assert.Equal("48x96", result.MatchedLabel);
        Assert.Equal(96, plate.Size.Length); // X axis = long
        Assert.Equal(48, plate.Size.Width);  // Y axis = short
    }

    [Fact]
    public void SnapToStandardSize_90TallPart_SnapsToStandard48x96_YLong()
    {
        // Part is 40 long (X) x 90 wide (Y) -> Y is the long axis.
        var plate = new Plate(200, 200);
        plate.Parts.Add(MakeRectPart(0, 0, 40, 90));

        var result = plate.SnapToStandardSize();

        Assert.Equal("48x96", result.MatchedLabel);
        Assert.Equal(48, plate.Size.Length); // X axis = short
        Assert.Equal(96, plate.Size.Width);  // Y axis = long
    }

    [Fact]
    public void SnapToStandardSize_JustOver48_PicksNextStandardSize()
    {
        var plate = new Plate(200, 200);
        plate.Parts.Add(MakeRectPart(0, 0, 100, 50));

        var result = plate.SnapToStandardSize();

        Assert.Equal("60x120", result.MatchedLabel);
        Assert.Equal(120, plate.Size.Length); // X long
        Assert.Equal(60, plate.Size.Width);
    }

    [Fact]
    public void SnapToStandardSize_EmptyPlate_DoesNotModifySize()
    {
        var plate = new Plate(60, 120);

        var result = plate.SnapToStandardSize();

        Assert.Null(result.MatchedLabel);
        Assert.Equal(60, plate.Size.Width);
        Assert.Equal(120, plate.Size.Length);
    }

    [Fact]
    public void SnapToStandardSize_MultipleParts_UsesCombinedEnvelope()
    {
        var plate = new Plate(200, 200);
        plate.Parts.Add(MakeRectPart(0, 0, 30, 40));
        plate.Parts.Add(MakeRectPart(30, 0, 30, 40));  // combined X-extent = 60
        plate.Parts.Add(MakeRectPart(0, 40, 60, 60));  // combined extent = 60 x 100

        var result = plate.SnapToStandardSize();

        // 60 x 100 fits 60x120 standard sheet, Y is the long axis.
        Assert.Equal("60x120", result.MatchedLabel);
        Assert.Equal(60, plate.Size.Length); // X
        Assert.Equal(120, plate.Size.Width); // Y long
    }
}
