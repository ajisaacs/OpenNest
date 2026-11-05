using System.Reflection;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Geometry;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// The arrow keys move the selection by the fixed offset increment, or, with the calculated
/// distance on, by the selection's own size plus the part spacing, so no push back is needed.
/// </summary>
public class PlateViewStepTests
{
    private static Drawing Square(string name)
    {
        var program = new CNC.Program();
        program.Codes.Add(new CNC.RapidMove(new Vector(0, 0)));
        program.Codes.Add(new CNC.LinearMove(new Vector(4, 0)));
        program.Codes.Add(new CNC.LinearMove(new Vector(4, 4)));
        program.Codes.Add(new CNC.LinearMove(new Vector(0, 4)));
        program.Codes.Add(new CNC.LinearMove(new Vector(0, 0)));
        return new Drawing(name, program);
    }

    private static Plate MakePlate() => new(48, 96) { PartSpacing = 1 };

    private static void Arrow(PlateView view, Keys key) =>
        typeof(PlateView).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { key });

    [Fact]
    public void ArrowKeysKeepTheOffsetIncrementByDefault() => StaTestThread.Run(() =>
    {
        var plate = MakePlate();
        var part = new Part(Square("part"), new Vector(10, 10));
        plate.Parts.Add(part);
        using var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(600, 600) };
        view.SelectAll();

        Arrow(view, Keys.Right);

        Assert.Equal(10 + view.OffsetIncrementDistance, part.Location.X, 6);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Theory]
    [InlineData(Keys.Right, 15, 10)]
    [InlineData(Keys.Left, 5, 10)]
    [InlineData(Keys.Up, 10, 15)]
    [InlineData(Keys.Down, 10, 5)]
    public void CalculatedDistanceStepsTheSizePlusTheSpacing(Keys key, double x, double y) => StaTestThread.Run(() =>
    {
        var plate = MakePlate();
        var part = new Part(Square("part"), new Vector(10, 10));
        plate.Parts.Add(part);
        using var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(600, 600) };
        view.CalculatedOffsetDistance = true;
        view.SelectAll();

        Arrow(view, key);

        Assert.Equal(x, part.Location.X, 3);
        Assert.Equal(y, part.Location.Y, 3);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void CalculatedDistanceStepsAClonePreviewOffTheCopyJustPlaced() => StaTestThread.Run(() =>
    {
        var plate = MakePlate();
        using var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(600, 600) };
        view.ZoomToFit();
        view.CalculatedOffsetDistance = true;
        view.SetAction(typeof(ActionClone), Square("new"));
        Raise(view, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 300, 300, 0));

        Raise(view, "OnKeyDown", new KeyEventArgs(Keys.F1));
        Arrow(view, Keys.Right);
        Raise(view, "OnKeyDown", new KeyEventArgs(Keys.F1));

        Assert.Equal(2, plate.Parts.Count);
        Assert.Equal(5, plate.Parts[1].Location.X - plate.Parts[0].Location.X, 3);
        Assert.Equal(plate.Parts[0].Location.Y, plate.Parts[1].Location.Y, 6);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
