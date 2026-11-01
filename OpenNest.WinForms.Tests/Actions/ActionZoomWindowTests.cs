using System.Drawing;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Geometry;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Actions;

/// <summary>Zoom To Area is a one-shot tool: a single zoom hands the view back to Select.</summary>
public class ActionZoomWindowTests
{
    private static PlateView ZoomView()
    {
        var view = new PlateView { Plate = new Plate(48, 120), Size = new System.Drawing.Size(900, 450) };
        view.ZoomToFit();
        view.SetAction(typeof(ActionZoomWindow));
        return view;
    }

    private static Point At(PlateView view, double x, double y) => view.PointWorldToControl(new Vector(x, y));

    private static void Press(PlateView view, Point p) =>
        Raise(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));

    private static void Release(PlateView view, Point p) =>
        Raise(view, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));

    private static void Click(PlateView view, Point p)
    {
        Press(view, p);
        Release(view, p);
    }

    [Fact]
    public void DraggingAWindowZoomsAndReturnsToSelect() => StaTestThread.Run(() =>
    {
        using var view = ZoomView();
        var scale = view.ViewScale;

        Press(view, At(view, 5, 5));
        Release(view, At(view, 20, 20));

        Assert.True(view.ViewScale > scale);
        Assert.IsType<ActionSelect>(view.Actions.CurrentAction);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void ClickingTwoCornersZoomsAndReturnsToSelect() => StaTestThread.Run(() =>
    {
        using var view = ZoomView();
        var scale = view.ViewScale;

        Click(view, At(view, 5, 5));
        Assert.IsType<ActionZoomWindow>(view.Actions.CurrentAction);

        Click(view, At(view, 20, 20));

        Assert.True(view.ViewScale > scale);
        Assert.IsType<ActionSelect>(view.Actions.CurrentAction);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void EscapeFromSelectDoesNotBringTheToolBack() => StaTestThread.Run(() =>
    {
        using var view = ZoomView();

        Press(view, At(view, 5, 5));
        Release(view, At(view, 20, 20));
        view.Actions.ProcessEscapeKey();

        Assert.IsType<ActionSelect>(view.Actions.CurrentAction);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void CancellingKeepsTheToolArmed() => StaTestThread.Run(() =>
    {
        using var view = ZoomView();
        var scale = view.ViewScale;

        Click(view, At(view, 5, 5));
        view.Actions.ProcessEscapeKey();

        Assert.Equal(scale, view.ViewScale);
        Assert.IsType<ActionZoomWindow>(view.Actions.CurrentAction);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
