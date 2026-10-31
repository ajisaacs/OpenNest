using System.Reflection;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Geometry;
using OpenNest.Help;

namespace OpenNest.WinForms.Tests.Actions;

/// <summary>
/// Trim/Extend Cut-Off says which mode it is in and switches with Tab; right-click on a junction
/// offers Swap Through-Cut.
/// </summary>
public class ActionTrimExtendCutOffTests
{
    // Default view scale is about one pixel per inch: allow for rounding to whole pixels.
    private const double Tolerance = 1.0;
    private const double SeparatorY = 31.510593;
    private const double LastVerticalX = 70.221406;

    private static void PressKey(PlateView view, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { new KeyEventArgs(key) });

    private static bool IsInputKey(PlateView view, Keys key) =>
        (bool)typeof(PlateView).GetMethod("IsInputKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { key })!;

    // N26-264 plate 1: the vertical at X 70.22 stops at the full-length horizontal separator.
    private static (Plate Plate, CutOff Vertical, CutOff Horizontal) Junction()
    {
        var plate = new Plate(48, 96);
        var vertical = new CutOff(new Vector(LastVerticalX, 0), CutOffAxis.Vertical) { EndLimit = SeparatorY };
        var horizontal = new CutOff(new Vector(0, SeparatorY), CutOffAxis.Horizontal);
        plate.CutOffs.Add(vertical);
        plate.CutOffs.Add(horizontal);
        plate.RegenerateCutOffs(new CutOffSettings());
        return (plate, vertical, horizontal);
    }

    [Fact]
    public void StatusNamesTheModeAndTabSwitchesIt() => StaTestThread.Run(() =>
    {
        using var view = new PlateView { Plate = new Plate(48, 96) };

        Assert.False(IsInputKey(view, Keys.Tab));
        view.SetAction(typeof(ActionTrimExtendCutOff));
        Assert.Equal(
            "Trim/Extend Cut-Off: click the piece of a cut-off to remove, Tab switches to Extend, Esc finishes, F1 for help",
            view.Status);
        Assert.Equal(HelpTopics.TrimExtendCutOff, view.ActionHelpTopic);
        Assert.True(IsInputKey(view, Keys.Tab));

        PressKey(view, Keys.Tab);
        Assert.StartsWith("Trim/Extend Cut-Off: click near the end of a cut-off to extend it or close a gap, Tab switches to Trim",
            view.Status);

        PressKey(view, Keys.Escape);
        Assert.IsType<ActionSelect>(view.Actions.CurrentAction);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void DraggingACutOffTakesTheLinesThatMeetIt() => StaTestThread.Run(() =>
    {
        var plate = new Plate(60, 120);
        var vertical = new CutOff(new Vector(30, 0), CutOffAxis.Vertical);
        var horizontal = new CutOff(new Vector(0, 20), CutOffAxis.Horizontal) { EndLimit = 30 };
        plate.CutOffs.Add(vertical);
        plate.CutOffs.Add(horizontal);
        plate.RegenerateCutOffs(new CutOffSettings());
        using var view = new PlateView { Plate = plate };

        var dragged = view.CutOffs.TryStartDrag(new Vector(30, 40), Tolerance);
        Assert.Same(vertical, dragged);
        view.CutOffs.UpdateDrag(new Vector(34, 40), dragged);
        Assert.Equal(34, horizontal.EndLimit);
        Assert.Equal(34, horizontal.Drawing.Program.BoundingBox().Right, 6);
        view.CutOffs.UpdateDrag(new Vector(26, 40), dragged);
        view.CutOffs.EndDrag();

        Assert.Equal(26, horizontal.EndLimit);
        Assert.Equal(26, horizontal.Drawing.Program.BoundingBox().Right, 6);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void ApplyRegeneratesAndRefusalsChangeNothing() => StaTestThread.Run(() =>
    {
        var (plate, vertical, horizontal) = Junction();
        using var view = new PlateView { Plate = plate };

        var refused = CutOffTrim.Trim(plate, vertical, new Vector(LastVerticalX, 10), view.CutOffSettings);
        Assert.False(view.CutOffs.Apply(refused, "Trim Cut-Off"));
        Assert.Equal(SeparatorY, vertical.EndLimit);

        Assert.True(view.CutOffs.Apply(
            CutOffTrim.Trim(plate, horizontal, new Vector(90, SeparatorY), view.CutOffSettings), "Trim Cut-Off"));
        Assert.Equal(LastVerticalX, horizontal.EndLimit);
        var right = horizontal.Drawing.Program.Codes.OfType<OpenNest.CNC.LinearMove>().Max(move => move.EndPoint.X);
        Assert.Equal(LastVerticalX, right, 6);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void RightClickOnAJunctionSwapsTheThroughCut() => StaTestThread.Run(() =>
    {
        var (plate, vertical, horizontal) = Junction();
        using var view = new PlateView { Plate = plate };

        var menu = view.CutOffMenu.BuildMenu(view.PointWorldToControl(new Vector(LastVerticalX, SeparatorY)), Tolerance);
        Assert.NotNull(menu);
        var swap = Assert.Single(menu.Items.Cast<ToolStripItem>());
        Assert.Equal("Swap Through-Cut", swap.Text);

        swap.PerformClick();
        Assert.Null(vertical.EndLimit);
        Assert.Equal(LastVerticalX, horizontal.EndLimit);
        // The vertical now splits the sheet, so it is cut last.
        Assert.Same(vertical.Drawing, plate.Parts[^1].BaseDrawing);
        Assert.Equal("Swapped the through-cut", view.Status);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void OffersNoSwapAwayFromAJunctionOrOutsideSelectMode() => StaTestThread.Run(() =>
    {
        var (plate, _, _) = Junction();
        using var view = new PlateView { Plate = plate };
        var onJunction = view.PointWorldToControl(new Vector(LastVerticalX, SeparatorY));

        Assert.Null(view.CutOffMenu.BuildMenu(view.PointWorldToControl(new Vector(20, 10)), Tolerance));

        view.InspectionOnly = true;
        Assert.Null(view.CutOffMenu.BuildMenu(onJunction, Tolerance));
        view.InspectionOnly = false;

        view.Actions.SetAction(typeof(ActionZoomWindow));
        Assert.Null(view.CutOffMenu.BuildMenu(onJunction, Tolerance));
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
