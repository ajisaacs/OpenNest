using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Help;

namespace OpenNest.WinForms.Tests.Actions;

/// <summary>
/// The manual cut-off tool says how to turn its line and where its help is, stops a new line at
/// the first crossing and draws it full length after Tab.
/// </summary>
public class ActionCutOffHintTests
{
    private static void PressKey(PlateView view, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { new KeyEventArgs(key) });

    private static void Mouse(PlateView view, string handler, MouseButtons button, Point at) =>
        typeof(PlateView).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic,
                new[] { typeof(MouseEventArgs) })!
            .Invoke(view, new object[] { new MouseEventArgs(button, 1, at.X, at.Y, 0) });

    private static CutOff ClickVerticalLine(PlateView view, double x, double y)
    {
        var at = view.PointWorldToControl(new OpenNest.Geometry.Vector(x, y));
        var point = new Point((int)at.X, (int)at.Y);
        Mouse(view, "OnMouseMove", MouseButtons.None, point);
        Mouse(view, "OnMouseDown", MouseButtons.Left, point);
        Mouse(view, "OnMouseUp", MouseButtons.Left, point);
        return view.Plate.CutOffs[^1];
    }

    private static PlateView ViewWithHorizontalAt(double y)
    {
        var plate = new Plate(48, 96);
        plate.CutOffs.Add(new CutOff(new OpenNest.Geometry.Vector(0, y), CutOffAxis.Horizontal));
        plate.RegenerateCutOffs(new CutOffSettings());
        var view = new PlateView { Plate = plate, Size = new Size(800, 600) };
        view.ZoomToFit();
        return view;
    }

    [Fact]
    public void ANewLineStopsAtTheFirstCrossingUntilTabMakesItFullLength() => StaTestThread.Run(() =>
    {
        using var view = ViewWithHorizontalAt(20);
        view.SetAction(typeof(ActionCutOff));

        var stopped = ClickVerticalLine(view, 40, 30);
        Assert.Null(stopped.StartLimit);
        Assert.Equal(20, stopped.EndLimit);

        PressKey(view, Keys.Tab);
        Assert.StartsWith("Sheet Cut-Off: click to add a full-length vertical line", view.Status);
        var full = ClickVerticalLine(view, 60, 30);
        Assert.Null(full.StartLimit);
        Assert.Null(full.EndLimit);

        // The choice outlasts the tool.
        view.SetAction(typeof(ActionSelect));
        view.SetAction(typeof(ActionCutOff));
        Assert.StartsWith("Sheet Cut-Off: click to add a full-length vertical line", view.Status);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void StatusNamesTheAxisAndSpaceTurnsIt() => StaTestThread.Run(() =>
    {
        using var view = new PlateView { Plate = new Plate(48, 120) };

        view.SetAction(typeof(ActionCutOff));
        Assert.Equal(
            "Sheet Cut-Off: click to add a vertical line stopped at the first crossing, Space turns it horizontal, Tab makes it full length, Esc finishes, F1 for help",
            view.Status);
        Assert.Equal(HelpTopics.SheetCutOff, view.ActionHelpTopic);

        PressKey(view, Keys.Space);
        Assert.StartsWith("Sheet Cut-Off: click to add a horizontal line stopped at the first crossing, Space turns it vertical", view.Status);

        view.SetAction(typeof(ActionSelect));
        Assert.Null(view.ActionHelpTopic);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
