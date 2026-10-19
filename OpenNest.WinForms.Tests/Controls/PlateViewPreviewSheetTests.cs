using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

public class PlateViewPreviewSheetTests
{
    [Fact]
    public void PreviewSheetReplacesThePlateUntilThePreviewEnds() => StaTestThread.Run(() =>
    {
        var plate = new Plate(48, 96);
        var trial = new Plate(60, 120);
        using var view = new PlateView { Plate = plate };
        var part = new Part(new Drawing("square", Square()), new Vector(1, 1));

        view.SetActiveParts(new List<Part> { part }, trial);
        Assert.Same(trial, view.SheetPlate);
        Assert.Same(plate, view.DisplayPlate);

        // A preview without a sheet (interactive fill) is drawn on the plate.
        view.SetActiveParts(new List<Part> { part });
        Assert.Same(plate, view.SheetPlate);

        view.SetActiveParts(new List<Part> { part }, trial);
        view.ClearPreviewParts();
        Assert.Same(plate, view.SheetPlate);

        view.SetActiveParts(new List<Part> { part }, trial);
        view.Plate = new Plate(48, 96);
        Assert.Same(view.Plate, view.SheetPlate);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    private static CNC.Program Square()
    {
        var program = new CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(0, 10);
        program.LineTo(10, 10);
        program.LineTo(10, 0);
        program.LineTo(0, 0);
        return program;
    }
}
