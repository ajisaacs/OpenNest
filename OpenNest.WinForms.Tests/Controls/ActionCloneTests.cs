using System.Reflection;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Geometry;
using static OpenNest.WinForms.Tests.FormTesting;

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
    public void DeleteAfterSelectAllRemovesPlacedPartsAndKeepsPlacing() => StaTestThread.Run(() =>
    {
        var plate = new Plate(48, 96);
        var drawing = Square("filled");
        plate.Parts.Add(new Part(drawing));
        plate.Parts.Add(new Part(drawing));
        using var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(600, 600) };
        view.ZoomToFit();
        view.SetAction(typeof(ActionClone), drawing);
        var preview = Assert.Single(view.SelectedParts);
        view.SelectAll();

        Raise(view, "OnKeyDown", new KeyEventArgs(Keys.Delete));

        Assert.Empty(plate.Parts);
        Assert.Same(preview, Assert.Single(view.SelectedParts));
        Assert.True(preview.IsSelected);
        Assert.IsType<ActionClone>(view.Actions.CurrentAction);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void F1SetsThePartInPlace() => StaTestThread.Run(() =>
    {
        var plate = new Plate(48, 96);
        using var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(600, 600) };
        view.ZoomToFit();
        view.SetAction(typeof(ActionClone), Square("new"));

        Raise(view, "OnKeyDown", new KeyEventArgs(Keys.F1));

        Assert.Single(plate.Parts);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void ADialogsFakeMouseMoveLeavesNudgedPreviewsInPlace() => StaTestThread.Run(() =>
    {
        using var view = new PlateView { Plate = new Plate(48, 96), Size = new System.Drawing.Size(600, 600) };
        view.ZoomToFit();
        view.SetAction(typeof(ActionClone), Square("new"));
        var preview = Assert.Single(view.SelectedParts);
        MoveMouse(view, new System.Drawing.Point(300, 300));
        var followed = preview.BasePart.BoundingBox.Location;

        Nudge(view);
        var nudged = preview.BasePart.BoundingBox.Location;
        Assert.Equal(followed.X + view.OffsetIncrementDistance, nudged.X, 6);

        // A repost of the last position, with no focus change.
        MoveMouse(view, new System.Drawing.Point(300, 300));
        Assert.Equal(nudged, preview.BasePart.BoundingBox.Location);

        // Opening the dialog, then closing it: each post arrives where the cursor rests.
        var rest = view.PointToClient(Cursor.Position);
        Raise(view, "OnLostFocus", EventArgs.Empty);
        MoveMouse(view, rest);
        Assert.Equal(nudged, preview.BasePart.BoundingBox.Location);

        Raise(view, "OnGotFocus", EventArgs.Empty);
        MoveMouse(view, rest);
        Assert.Equal(nudged, preview.BasePart.BoundingBox.Location);

        // The next real move takes the previews back to the cursor.
        MoveMouse(view, new System.Drawing.Point(rest.X + 20, rest.Y + 20));
        Assert.NotEqual(nudged, preview.BasePart.BoundingBox.Location);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    private static void MoveMouse(PlateView view, System.Drawing.Point point) =>
        Raise(view, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0));

    private static void Nudge(PlateView view) =>
        typeof(PlateView).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { Keys.Right });
}
