using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PlateCenterPartsTests
{
    private static Part MakeSquare(Vector location)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Part(new Drawing("square", pgm), location);
    }

    [Fact]
    public void CentersPartsInWorkAreaNotPlate()
    {
        var plate = new Plate(100, 100)
        {
            // Wider clamp-side margin at the bottom.
            EdgeSpacing = new Spacing(left: 0.25, bottom: 0.75, right: 0.25, top: 0.25),
        };
        var part = MakeSquare(new Vector(5, 5));
        plate.Parts.Add(part);

        part.Offset(plate.OffsetToCenterPartsInWorkArea());

        var box = part.BoundingBox;
        // Work area is X 0.25..99.75, Y 0.75..99.75, so the centre sits 0.25 above the plate's.
        Assert.Equal(50, box.Center.X, 6);
        Assert.Equal(50.25, box.Center.Y, 6);
        Assert.Equal(box.Left - 0.25, 99.75 - box.Right, 6);
        Assert.Equal(box.Bottom - 0.75, 99.75 - box.Top, 6);
    }

    [Fact]
    public void EmptyPlateNeedsNoOffset()
    {
        var plate = new Plate(100, 100) { EdgeSpacing = new Spacing(0.25, 0.75, 0.25, 0.25) };

        var offset = plate.OffsetToCenterPartsInWorkArea();

        Assert.Equal(0, offset.X);
        Assert.Equal(0, offset.Y);
    }
}
