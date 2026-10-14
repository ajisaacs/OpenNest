using System.Drawing;
using System.Reflection;
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
