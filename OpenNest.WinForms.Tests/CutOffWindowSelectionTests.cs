using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests;

public class CutOffWindowSelectionTests
{
    [Theory]
    [InlineData(CutOffAxis.Vertical, 1)]
    [InlineData(CutOffAxis.Horizontal, 2)]
    [InlineData(CutOffAxis.Vertical, 3)]
    [InlineData(CutOffAxis.Horizontal, 4)]
    public void WindowSelectsVisibleCutOffSegmentsInBothDirections(CutOffAxis axis, int quadrant) => RunSta(() =>
    {
        var plate = new Plate(20, 20) { Quadrant = quadrant };
        var bounds = plate.BoundingBox(includeParts: false);
        var cutoff = new CutOff(bounds.Center, axis);
        plate.CutOffs.Add(cutoff);
        cutoff.Regenerate(plate, new CutOffSettings());
        using var view = new PlateView { Plate = plate };
        var full = GraphRect(view, new Vector(bounds.Left - 1, bounds.Bottom - 1),
            new Vector(bounds.Right + 1, bounds.Top + 1));
        var crossing = GraphRect(view, new Vector(bounds.Center.X - 1, bounds.Center.Y - 1),
            new Vector(bounds.Center.X + 1, bounds.Center.Y + 1));
        var missed = GraphRect(view, new Vector(bounds.Left + 1, bounds.Bottom + 1),
            new Vector(bounds.Left + 2, bounds.Bottom + 2));

        Assert.Contains(cutoff, view.Selection.GetCutOffsFromWindow(full, SelectionType.Contains));
        Assert.Empty(view.Selection.GetCutOffsFromWindow(crossing, SelectionType.Contains));
        Assert.Contains(cutoff, view.Selection.GetCutOffsFromWindow(crossing, SelectionType.Intersect));
        Assert.Empty(view.Selection.GetCutOffsFromWindow(missed, SelectionType.Intersect));
    });

    [Fact]
    public void WindowUsesActualSegmentsNotTheNominalLineOrGap() => RunSta(() =>
    {
        using var view = new PlateView();
        var cutoff = new CutOff(new Vector(10, 10), CutOffAxis.Vertical);
        view.Plate.CutOffs.Add(cutoff);
        cutoff.Drawing.Program = new CNC.Program();
        cutoff.Drawing.Program.Codes.Add(new RapidMove(new Vector(10, 0)));
        cutoff.Drawing.Program.Codes.Add(new LinearMove(new Vector(10, 4)));
        cutoff.Drawing.Program.Codes.Add(new RapidMove(new Vector(10, 16)));
        cutoff.Drawing.Program.Codes.Add(new LinearMove(new Vector(10, 20)));

        var gap = GraphRect(view, new Vector(9, 8), new Vector(11, 12));
        var oneSegment = GraphRect(view, new Vector(9, -1), new Vector(11, 5));
        var full = GraphRect(view, new Vector(9, -1), new Vector(11, 21));
        Assert.Empty(view.Selection.GetCutOffsFromWindow(gap, SelectionType.Intersect));
        Assert.Empty(view.Selection.GetCutOffsFromWindow(oneSegment, SelectionType.Contains));
        Assert.Contains(cutoff, view.Selection.GetCutOffsFromWindow(oneSegment, SelectionType.Intersect));
        Assert.Contains(cutoff, view.Selection.GetCutOffsFromWindow(full, SelectionType.Contains));

        cutoff.Drawing.Program = new CNC.Program();
        Assert.Empty(view.Selection.GetCutOffsFromWindow(full, SelectionType.Intersect));
        Assert.Empty(view.Selection.GetCutOffsFromWindow(full, SelectionType.Contains));
    });

    [Fact]
    public void ActionSelectsPartsAndCutOffsTogetherAndDeleteRemovesBoth() => RunSta(() =>
    {
        var plate = new Plate(20, 20);
        var program = new CNC.Program();
        program.MoveTo(2, 2);
        program.LineTo(4, 2);
        program.LineTo(4, 4);
        program.LineTo(2, 4);
        program.LineTo(2, 2);
        plate.Parts.Add(new Part(new Drawing("square", program)));
        var cutoff = new CutOff(new Vector(10, 10), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutoff);
        cutoff.Regenerate(plate, new CutOffSettings());
        using var view = new PlateView { Plate = plate };
        var action = new ActionSelect(view);
        try
        {
            action.Point1 = new Vector(0, 0);
            action.Point2 = new Vector(20, 20); // left-to-right: contain
            SelectWindow(action);
            SelectWindow(action); // repeated selection must not duplicate a definition
            Assert.Single(view.SelectedParts);
            Assert.Same(cutoff, Assert.Single(view.Selection.SelectedCutOffs));
            Assert.True(action.IsBusy());

            view.Selection.DeleteSelected();
            Assert.Empty(plate.CutOffs);
            Assert.Empty(plate.Parts);
            Assert.Empty(view.SelectedParts);
            Assert.Empty(view.Selection.SelectedCutOffs);

            plate.CutOffs.Add(cutoff);
            cutoff.Regenerate(plate, view.CutOffSettings);
            action.Point1 = new Vector(12, 12);
            action.Point2 = new Vector(8, 8); // right-to-left: intersect
            SelectWindow(action);
            Assert.Same(cutoff, Assert.Single(view.Selection.SelectedCutOffs));
            Assert.True(action.IsBusy());
            action.CancelAction();
            Assert.Empty(view.Selection.SelectedCutOffs);
            Assert.False(action.IsBusy());
        }
        finally
        {
            action.DisconnectEvents();
        }
    });

    [Fact]
    public void MixedDeleteRegeneratesSurvivingCutOffAfterRemovingPart() => RunSta(() =>
    {
        var program = new CNC.Program();
        program.MoveTo(9, 5);
        program.LineTo(11, 5);
        program.LineTo(11, 10);
        program.LineTo(9, 10);
        program.LineTo(9, 5);
        var part = new Part(new Drawing("square", program));
        var plate = new Plate(20, 20);
        plate.Parts.Add(part);
        var removed = new CutOff(new Vector(2, 0), CutOffAxis.Vertical)
        {
            StartLimit = 4,
            EndLimit = 11,
        };
        var kept = new CutOff(new Vector(10, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(removed);
        plate.CutOffs.Add(kept);
        plate.RegenerateCutOffs(new CutOffSettings());
        using var view = new PlateView { Plate = plate };
        var action = (ActionSelect)view.Actions.CurrentAction;
        action.Point1 = new Vector(0, 3);
        action.Point2 = new Vector(12, 12);
        SelectWindow(action);
        Assert.Contains(view.SelectedParts, selected => selected.BasePart == part);
        Assert.Same(removed, Assert.Single(view.Selection.SelectedCutOffs));
        Assert.True(kept.Drawing.Program.Codes.Count > 2);

        view.Selection.DeleteSelected();
        Assert.Same(kept, Assert.Single(plate.CutOffs));
        Assert.Empty(view.SelectedParts);
        Assert.Equal(2, kept.Drawing.Program.Codes.Count);
        Assert.Equal(2, Assert.Single(plate.Parts).Program.Codes.Count);
    });

    [Fact]
    public void EscapeStopsCutOffDragBeforeAnotherWindowSelection() => RunSta(() =>
    {
        var plate = new Plate(20, 20);
        var cutoff = new CutOff(new Vector(10, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutoff);
        plate.RegenerateCutOffs(new CutOffSettings());
        using var view = new EventView { Plate = plate, Size = new System.Drawing.Size(500, 500) };
        view.ZoomToArea(-2, -2, 24, 24);
        view.Down(new Vector(10, 2));
        Assert.True(view.CutOffs.IsDragging);
        view.ProcessEscapeKey();
        Assert.Empty(view.Selection.SelectedCutOffs);
        Assert.False(view.CutOffs.IsDragging);
        view.Up(new Vector(10, 2));
        view.Down(new Vector(12, 12));
        view.MoveTo(new Vector(8, -1), MouseButtons.Left);
        view.Up(new Vector(8, -1));
        Assert.Same(cutoff, Assert.Single(view.Selection.SelectedCutOffs));
        var before = cutoff.Position;
        view.MoveTo(new Vector(13, 8), MouseButtons.None);
        Assert.Equal(before.X, cutoff.Position.X);
    });

    [Fact]
    public void ActiveCutOffDragNeedsLeftMotionAndEndsAfterSelectionClears() => RunSta(() =>
    {
        var plate = new Plate(20, 20);
        var cutoff = new CutOff(new Vector(10, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutoff);
        plate.RegenerateCutOffs(new CutOffSettings());
        using var view = new EventView { Plate = plate, Size = new System.Drawing.Size(500, 500) };
        view.ZoomToArea(-2, -2, 24, 24);
        view.Down(new Vector(10, 2));
        Assert.True(view.CutOffs.IsDragging);

        view.MoveTo(new Vector(13, 2), MouseButtons.None);
        Assert.Equal(10, cutoff.Position.X);
        view.UpRight(new Vector(13, 2));
        Assert.True(view.CutOffs.IsDragging);

        view.MoveTo(new Vector(11, 2), MouseButtons.Left);
        Assert.InRange(cutoff.Position.X, 10.5, 11.5);
        view.Selection.DeselectCutOffs();
        view.Up(new Vector(11, 2));
        Assert.False(view.CutOffs.IsDragging);
        var releasedX = cutoff.Position.X;
        view.MoveTo(new Vector(13, 2), MouseButtons.None);
        Assert.Equal(releasedX, cutoff.Position.X);
    });

    private sealed class EventView : PlateView
    {
        public void Down(Vector world)
        {
            MoveTo(world, MouseButtons.None);
            var point = PointWorldToControl(world);
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
        }

        public void Up(Vector world)
        {
            var point = PointWorldToControl(world);
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
        }

        public void UpRight(Vector world)
        {
            var point = PointWorldToControl(world);
            OnMouseUp(new MouseEventArgs(MouseButtons.Right, 1, point.X, point.Y, 0));
        }

        public void MoveTo(Vector world, MouseButtons buttons)
        {
            var point = PointWorldToControl(world);
            OnMouseMove(new MouseEventArgs(buttons, 0, point.X, point.Y, 0));
        }
    }

    private static void SelectWindow(ActionSelect action) =>
        typeof(ActionSelect).GetMethod("SelectPartsInWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(action, null);

    private static RectangleF GraphRect(PlateView view, Vector first, Vector second)
    {
        var a = view.PointWorldToGraph(first);
        var b = view.PointWorldToGraph(second);
        return RectangleF.FromLTRB(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y),
            System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y));
    }

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "Cut-off window selection test timed out.");
}
