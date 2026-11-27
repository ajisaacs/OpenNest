using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// A context menu swallows the mouse moves made while it is open, so the click that dismisses
/// it must not drag the part that was right-clicked.
/// </summary>
public class PartContextMenuDismissTests
{
    [Fact]
    public void ClickingOffThePartAfterARightClick_LeavesThePartWhereItWas()
    {
        using var view = NewView(out var part);
        var onPart = ToControl(view, new Vector(12, 12));
        var offPart = ToControl(view, new Vector(40, 80));

        Mouse(view, "OnMouseMove", MouseButtons.None, onPart);
        Mouse(view, "OnMouseDown", MouseButtons.Right, onPart);
        Mouse(view, "OnMouseUp", MouseButtons.Right, onPart);
        Assert.Contains(view.SelectedParts, p => p.BasePart == part);

        // The menu was open while the cursor traveled: no move reaches the view before the press.
        Mouse(view, "OnMouseDown", MouseButtons.Left, offPart);
        Mouse(view, "OnMouseMove", MouseButtons.Left, new Point(offPart.X + 1, offPart.Y));
        Mouse(view, "OnMouseUp", MouseButtons.Left, new Point(offPart.X + 1, offPart.Y));

        Assert.Equal(new Vector(10, 10), part.Location);
        Assert.Empty(view.SelectedParts);
    }

    private static PlateView NewView(out Part part)
    {
        var program = new CNC.Program();
        program.Codes.Add(new CNC.RapidMove(new Vector(0, 0)));
        program.Codes.Add(new CNC.LinearMove(new Vector(4, 0)));
        program.Codes.Add(new CNC.LinearMove(new Vector(4, 4)));
        program.Codes.Add(new CNC.LinearMove(new Vector(0, 4)));
        program.Codes.Add(new CNC.LinearMove(new Vector(0, 0)));

        var plate = new Plate(new OpenNest.Geometry.Size(60, 120)) { Quadrant = 1 };
        part = new Part(new Drawing("square", program), new Vector(10, 10));
        plate.Parts.Add(part);
        var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(800, 600) };
        view.ZoomToFit();
        return view;
    }

    private static Point ToControl(PlateView view, Vector world)
    {
        var at = view.PointWorldToControl(world);
        return new Point((int)at.X, (int)at.Y);
    }

    private static void Mouse(PlateView view, string handler, MouseButtons button, Point at) =>
        typeof(PlateView).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(MouseEventArgs)])!
            .Invoke(view, [new MouseEventArgs(button, 1, at.X, at.Y, 0)]);
}
