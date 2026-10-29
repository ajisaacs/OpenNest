using System.Reflection;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// Placing or cloning parts: the previews follow the cursor as the selection and must keep
/// their selected look until placed, whatever keys the user presses meanwhile.
/// </summary>
public class ActionCloneTests
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

    private static void Raise(PlateView view, string method, EventArgs args) =>
        typeof(PlateView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { args });

    [Fact]
    public void DeleteWhilePlacingADrawingKeepsThePreviewSelected() => StaTestThread.Run(() =>
    {
        var plate = new Plate(48, 96);
        var placed = new Part(Square("placed"));
        plate.Parts.Add(placed);
        using var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(600, 600) };
        view.ZoomToFit();
        view.SetAction(typeof(ActionClone), Square("new"));

        Raise(view, "OnKeyDown", new KeyEventArgs(Keys.Delete));

        var preview = Assert.Single(view.SelectedParts);
        Assert.True(preview.IsSelected);
        Assert.IsType<ActionClone>(view.Actions.CurrentAction);
        Assert.Same(placed, Assert.Single(plate.Parts));
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void DeleteWhileCloningPlacedPartsRemovesTheOriginals() => StaTestThread.Run(() =>
    {
        var plate = new Plate(48, 96);
        var original = new Part(Square("original"));
        plate.Parts.Add(original);
        using var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(600, 600) };
        view.ZoomToFit();
        view.SetAction(typeof(ActionClone), new List<Part> { original });

        Raise(view, "OnKeyDown", new KeyEventArgs(Keys.Delete));

        Assert.Empty(plate.Parts);
        Assert.True(Assert.Single(view.SelectedParts).IsSelected);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void F1SetsThePartInPlaceInsteadOfOpeningHelp() => StaTestThread.Run(() =>
    {
        var plate = new Plate(48, 96);
        using var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(600, 600) };
        view.ZoomToFit();
        view.SetAction(typeof(ActionClone), Square("new"));

        Assert.True(view.ActionHandlesF1);
        Raise(view, "OnKeyDown", new KeyEventArgs(Keys.F1));

        Assert.Single(plate.Parts);

        view.SetAction(typeof(ActionSelect));
        Assert.False(view.ActionHandlesF1);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
