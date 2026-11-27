using System.Drawing;
using OpenNest.Controls;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>The Plates tab list draws each plate's picture once and redraws it only when it changes.</summary>
public class PlateListBoxTests
{
    [Fact]
    public void ThePictureIsReusedUntilAPartMoves() => RunSta(() =>
    {
        var (plate, part) = CreatePlate();
        using var list = new PlateListBox();
        list.SetPlates([plate]);

        var first = list.GetPreview(plate);
        Assert.Same(first, list.GetPreview(plate));
        var before = Snapshot(first);

        part.Offset(new Vector(30, 0));
        var moved = list.GetPreview(plate);

        Assert.NotSame(first, moved);
        Assert.NotEqual(before, Snapshot(moved));
    });

    [Fact]
    public void PartsShowInTheirDrawingsColorOnTheSheet() => RunSta(() =>
    {
        var (plate, part) = CreatePlate();
        part.BaseDrawing.Color = Color.FromArgb(255, 200, 30, 30);
        using var list = new PlateListBox();
        list.SetPlates([plate]);

        using var image = (Bitmap)list.GetPreview(plate).Clone();
        var found = false;
        for (var x = 0; x < image.Width && !found; x++)
            for (var y = 0; y < image.Height && !found; y++)
                found = image.GetPixel(x, y).ToArgb() == part.BaseDrawing.Color.ToArgb();

        Assert.True(found);
    });

    [Fact]
    public void RowsBelowTheLastPlateAreNoRow() => RunSta(() =>
    {
        var (plate, _) = CreatePlate();
        using var list = new PlateListBox { Height = 400 };
        list.CreateControl();
        list.SetPlates([plate]);

        Assert.Equal(0, list.RowAt(new Point(5, 5)));
        Assert.Equal(-1, list.RowAt(new Point(5, list.ItemHeight + 10)));
    });

    private static (Plate Plate, Part Part) CreatePlate()
    {
        var nest = new Nest("preview");
        var drawing = new RectangleShape { Name = "SYN", Length = 20, Width = 10 }.GetDrawing();
        nest.Drawings.Add(drawing);
        var plate = nest.CreatePlate();
        plate.Size = new OpenNest.Geometry.Size(60, 120);
        var part = new Part(drawing);
        part.Offset(new Vector(5, 5));
        plate.Parts.Add(part);
        return (plate, part);
    }

    private static int[] Snapshot(Image image)
    {
        using var bitmap = new Bitmap(image);
        var pixels = new int[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                pixels[y * bitmap.Width + x] = bitmap.GetPixel(x, y).ToArgb();
        return pixels;
    }

    private static void RunSta(System.Action testBody) =>
        StaTestThread.Run(testBody, TimeSpan.FromSeconds(60), "The plate list STA test did not complete.");
}
