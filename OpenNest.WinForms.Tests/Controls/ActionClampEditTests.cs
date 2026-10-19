using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// Plate &gt; Edit Clamps: dragging, adding, typing and deleting clamps on the plate view
/// changes the nest's rail, so every sheet follows it.
/// </summary>
public class ActionClampEditTests
{
    private static Nest RailNest()
    {
        var nest = new Nest("clamps") { Units = Units.Inches };
        nest.PlateDefaults.Size = new OpenNest.Geometry.Size(48, 120);
        nest.SetClampRail(new ClampRail(2, 0.5, new double[] { 20, 60 }));
        nest.CreatePlate();
        nest.CreatePlate();
        return nest;
    }

    private static PlateView EditView(Nest nest)
    {
        var view = new PlateView { Plate = nest.Plates[0], Size = new System.Drawing.Size(900, 450) };
        view.ClampNest = () => nest;
        view.ZoomToFit();
        view.SetAction(typeof(ActionClampEdit));
        Paint(view); // dimension labels are laid out while painting
        return view;
    }

    private static void Raise(PlateView view, string method, EventArgs args) =>
        typeof(PlateView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { args });

    private static void Paint(PlateView view)
    {
        using var bitmap = new Bitmap(view.Width, view.Height);
        using var g = Graphics.FromImage(bitmap);
        Raise(view, "OnPaint", new PaintEventArgs(g, view.ClientRectangle));
    }

    private static Point At(PlateView view, double x, double y) => view.PointWorldToControl(new Vector(x, y));

    private static void Move(PlateView view, Point p, MouseButtons button = MouseButtons.None) =>
        Raise(view, "OnMouseMove", new MouseEventArgs(button, 0, p.X, p.Y, 0));

    private static void Click(PlateView view, Point p)
    {
        Move(view, p);
        Raise(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        Raise(view, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
    }

    private static void Drag(PlateView view, Point from, Point to)
    {
        Move(view, from);
        Raise(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0));
        Move(view, new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), MouseButtons.Left);
        Move(view, to, MouseButtons.Left);
        Raise(view, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
    }

    [Fact]
    public void DraggingAClampAlongItsEdgeMovesItOnEverySheet() => StaTestThread.Run(() =>
    {
        var nest = RailNest();
        using var view = EditView(nest);

        Drag(view, At(view, 20, 0.25), At(view, 40, 0.25));

        var moved = nest.PlateDefaults.ClampRail!.Clamps;
        Assert.Equal(ClampEdge.OriginSide, moved[0].Edge);
        Assert.InRange(moved[0].Center, 38, 42); // pixel rounding, then a 1/8" snap
        Assert.Equal(0, moved[0].Center % 0.125, 9);
        Assert.Equal(60, moved[1].Center);
        Assert.All(nest.Plates, plate => Assert.Same(nest.PlateDefaults.ClampRail, plate.ClampRail));
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void DraggingAClampOntoAnotherEdgeMovesItThere() => StaTestThread.Run(() =>
    {
        var nest = RailNest();
        using var view = EditView(nest);

        Drag(view, At(view, 60, 0.25), At(view, 119.9, 30));

        var clamp = nest.PlateDefaults.ClampRail!.Clamps.Single(c => c.Edge != ClampEdge.OriginSide);
        Assert.Equal(ClampEdge.FarEnd, clamp.Edge);
        Assert.InRange(clamp.Center, 28, 32);
        Assert.Contains(nest.Plates[1].ClampZones, z => z.Bounds.Left > 119);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void ClickingNearAnEdgeAddsAClamp() => StaTestThread.Run(() =>
    {
        var nest = RailNest();
        using var view = EditView(nest);

        Click(view, At(view, 90, 47.8)); // just inside the far side

        var added = nest.PlateDefaults.ClampRail!.Clamps.Single(c => c.Edge == ClampEdge.FarSide);
        Assert.InRange(added.Center, 88, 92);
        Assert.Equal(3, nest.Plates[1].ClampZones.Count);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void ClickingInsideTheSheetAddsNothing() => StaTestThread.Run(() =>
    {
        var nest = RailNest();
        using var view = EditView(nest);

        Click(view, At(view, 60, 24));

        Assert.Equal(2, nest.PlateDefaults.ClampRail!.Clamps.Count);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void TypingADimensionMovesTheClamp() => StaTestThread.Run(() =>
    {
        var nest = RailNest();
        using var view = EditView(nest);
        // The near-edge dimension of the clamp centered at 20 (2 wide) reads 19, below the sheet.
        var editTo = Properties.Settings.Default.ClampDimensionToCenter ? 30.0 : 29.0;
        var label = view.PointWorldToControl(new Vector(Properties.Settings.Default.ClampDimensionToCenter ? 20 : 19, 0));
        label.Offset(0, 30);

        Click(view, label);
        var editor = Assert.Single(view.Controls.OfType<TextBox>());
        editor.Text = editTo.ToString(System.Globalization.CultureInfo.CurrentCulture);
        typeof(TextBox).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, new object[] { new KeyEventArgs(Keys.Enter) });

        Assert.Empty(view.Controls.OfType<TextBox>());
        Assert.Equal(new double[] { 30, 60 }, nest.PlateDefaults.ClampRail!.Centers);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void DeleteRemovesTheSelectedClampAndNeverParts() => StaTestThread.Run(() =>
    {
        var nest = RailNest();
        using var view = EditView(nest);

        Click(view, At(view, 60, 0.25));
        Raise(view, "OnKeyDown", new KeyEventArgs(Keys.Delete));

        Assert.Equal(new double[] { 20 }, nest.PlateDefaults.ClampRail!.Centers);

        Click(view, At(view, 20, 0.25));
        Raise(view, "OnKeyDown", new KeyEventArgs(Keys.Delete));
        Assert.Null(nest.PlateDefaults.ClampRail);
        Assert.Empty(nest.Plates[1].ClampZones);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
