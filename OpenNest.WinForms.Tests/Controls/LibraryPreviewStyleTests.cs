using System.Drawing;
using OpenNest.CNC;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

public class LibraryPreviewStyleTests
{
    private const int ViewSize = 200;

    [Fact]
    public void LibraryColorsRoundTripAndOlderSchemesKeepTheDefaults()
    {
        var scheme = ColorSchemeRegistry.Get("Dark");
        var restored = ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(scheme));

        Assert.Equal(scheme.LibraryBackgroundColor.ToArgb(), restored.LibraryBackgroundColor.ToArgb());
        Assert.Equal(scheme.LibraryFillColor.ToArgb(), restored.LibraryFillColor.ToArgb());
        Assert.Equal(scheme.LibraryOutlineColor.ToArgb(), restored.LibraryOutlineColor.ToArgb());
        Assert.Equal(scheme.LibraryEtchColor.ToArgb(), restored.LibraryEtchColor.ToArgb());
        Assert.Equal(scheme.LibraryBendColor.ToArgb(), restored.LibraryBendColor.ToArgb());

        var legacy = ColorSchemeSerializer.Deserialize("{\"name\":\"Old\",\"backgroundColor\":\"#101010\"}");
        var defaults = new ColorScheme();
        Assert.Equal(defaults.LibraryBackgroundColor.ToArgb(), legacy.LibraryBackgroundColor.ToArgb());
        Assert.Equal(defaults.LibraryFillColor.ToArgb(), legacy.LibraryFillColor.ToArgb());
        Assert.Equal(defaults.LibraryBendColor.ToArgb(), legacy.LibraryBendColor.ToArgb());
    }

    [Fact]
    public void ShapePreviewFillsWithTheLibraryStyleNotTheDrawingColor() => ArchUnitsTests.RunSta(() =>
    {
        using var preview = new ShapePreviewControl { Size = new Size(ViewSize, ViewSize) };
        preview.ShowDrawing(RedSquare());

        AssertLibraryStyle(preview);
    });

    [Fact]
    public void DrawingSetPreviewFillsWithTheLibraryStyleNotTheDrawingColor() => ArchUnitsTests.RunSta(() =>
    {
        using var preview = new DrawingSetPreviewControl { Size = new Size(ViewSize, ViewSize) };
        preview.ShowDrawings(new[] { (RedSquare(), "") });

        AssertLibraryStyle(preview);
    });

    private static void AssertLibraryStyle(PlateView preview)
    {
        using var bitmap = new Bitmap(ViewSize, ViewSize);
        preview.DrawToBitmap(bitmap, new Rectangle(0, 0, ViewSize, ViewSize));
        var scheme = ColorScheme.Default;

        Assert.Equal(scheme.LibraryBackgroundColor.ToArgb(), preview.BackColor.ToArgb());
        Assert.Equal(scheme.LibraryFillColor.ToArgb(), bitmap.GetPixel(ViewSize / 2, ViewSize / 2 + 10).ToArgb());
    }

    private static Drawing RedSquare()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(10, 0));
        program.Codes.Add(new LinearMove(10, 10));
        program.Codes.Add(new LinearMove(0, 10));
        program.Codes.Add(new LinearMove(0, 0));
        return new Drawing("square", program) { Color = Color.Red };
    }
}
