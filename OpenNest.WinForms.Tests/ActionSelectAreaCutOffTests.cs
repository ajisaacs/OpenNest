using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests;

public class ActionSelectAreaCutOffTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var axis in new[] { CutOffAxis.Vertical, CutOffAxis.Horizontal })
            foreach (var quadrant in new[] { 1, 3 })
                foreach (var farSide in new[] { false, true })
                    foreach (var alternate in new[] { false, true })
                        yield return new object[] { axis, quadrant, farSide, alternate };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SelectedAreaAndFillStayOnSelectedSideOfCutOff(
        CutOffAxis axis, int quadrant, bool farSide, bool alternate) => RunSta(() =>
    {
        var plate = new Plate(new OpenNest.Geometry.Size(20, 20))
        {
            Quadrant = quadrant,
            PartSpacing = 0.5,
        };
        var bounds = plate.WorkArea();
        var position = new Vector(bounds.Left + 10, bounds.Bottom + 10);
        // Definitions alone must constrain selection, before any cutoff parts exist.
        plate.CutOffs.Add(new CutOff(position, axis));
        using var view = new SelectionView { Plate = plate };
        var action = new ActionSelectArea(view);
        try
        {
            var offset = farSide ? 5 : -5;
            view.SetPoint(axis == CutOffAxis.Vertical
                ? new Vector(position.X + offset, position.Y)
                : new Vector(position.X, position.Y + offset));
            if (alternate)
                view.ToggleSelectionDirection();
            // Invoke the same update used by mouse movement without pixel rounding.
            typeof(ActionSelectArea).GetMethod("UpdateSelectedArea", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(action, null);
            var area = action.SelectedArea;
            Assert.NotEqual(Box.Empty, area);
            var limit = (axis == CutOffAxis.Vertical ? position.X : position.Y)
                + (farSide ? plate.PartSpacing : -plate.PartSpacing);
            Assert.Equal(limit, axis == CutOffAxis.Vertical
                ? (farSide ? area.Left : area.Right)
                : (farSide ? area.Bottom : area.Top), 8);
            Assert.Equal(axis == CutOffAxis.Vertical ? bounds.Width : bounds.Length,
                axis == CutOffAxis.Vertical ? area.Width : area.Length, 8);

            var program = new OpenNest.CNC.Program();
            program.MoveTo(0, 0);
            program.LineTo(0, 3);
            program.LineTo(3, 3);
            program.LineTo(3, 0);
            program.LineTo(0, 0);
            // ActionFillArea passes SelectedArea directly to this service.
            var parts = PlateFillService.FillItem("Default", plate,
                new NestItem { Drawing = new Drawing("square", program) }, area, null, CancellationToken.None);
            Assert.NotEmpty(parts);
            Assert.All(parts, part =>
            {
                var box = part.BoundingBox;
                Assert.True(box.Left >= area.Left - 1e-6 && box.Right <= area.Right + 1e-6);
                Assert.True(box.Bottom >= area.Bottom - 1e-6 && box.Top <= area.Top + 1e-6);
            });
        }
        finally
        {
            action.DisconnectEvents();
        }
    });

    private sealed class SelectionView : PlateView
    {
        public void SetPoint(Vector point) => CurrentPoint = point;
        public void ToggleSelectionDirection() => OnKeyUp(new KeyEventArgs(Keys.Space));
    }

    private static void RunSta(System.Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The selection/fill test did not complete.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
