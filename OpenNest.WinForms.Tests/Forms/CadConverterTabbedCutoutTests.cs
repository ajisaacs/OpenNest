using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Bending;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.TabbedCutouts;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// Drives the CAD Converter's tabbed cutout tool through its real mouse handling: a 10 x 10 part
/// with a 4 x 4 square hole from (3, 3) to (7, 7).
/// </summary>
public class CadConverterTabbedCutoutTests
{
    [Fact]
    public void PickingAHole_AddsATabbedCutout_DraggingMovesATab_EditReSpacesTheTabs() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new TabbedCutoutTestForm();
        Show(form);
        form.LoadDrawings([Part()]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;

        Invoke(form, "OnAddTabbedCutoutClicked", null, EventArgs.Empty);
        Assert.True(view.IsPickingTabbedCutout);
        Mouse(view, "OnMouseClick", view.PointWorldToControl(new Vector(7, 5)));

        Assert.Empty(form.Messages);
        Assert.False(view.IsPickingTabbedCutout);
        var cutout = Assert.Single(item.TabbedCutouts);
        Assert.Equal(4, cutout.EdgeIds.Count);
        Assert.Equal(new[] { 2.0, 10.0 }, cutout.TabCenters);
        var layout = Assert.Single(view.TabbedCutoutLayouts);
        Assert.True(layout.IsValid, layout.Error);

        // Drag the first tab (on the bottom edge) along the hole to the right edge.
        Mouse(view, "OnMouseDown", view.PointWorldToControl(layout.PointAt(cutout.TabCenters[0])));
        Mouse(view, "OnMouseMove", view.PointWorldToControl(new Vector(7, 4)));
        Mouse(view, "OnMouseUp", view.PointWorldToControl(new Vector(7, 4)));
        Assert.InRange(cutout.TabCenters[0], 4.9, 5.1);

        // Same count and width keep the dragged tab; a new count spaces the tabs evenly again.
        form.Settings = dialog => { };
        Invoke(form, "OnTabbedCutoutEdited", null, 0);
        Assert.InRange(item.TabbedCutouts[0].TabCenters[0], 4.9, 5.1);
        form.Settings = dialog => dialog.TabCount = 4;
        Invoke(form, "OnTabbedCutoutEdited", null, 0);
        Assert.Equal(new[] { 2.0, 6.0, 10.0, 14.0 }, item.TabbedCutouts[0].TabCenters);

        var drawing = Assert.Single(form.GetDrawings());
        Assert.Equal(item.TabbedCutouts[0].TabCenters, Assert.Single(drawing.TabbedCutouts).TabCenters);
        var prepared = PreparedContours.Capture(drawing.Program, new CuttingParameters());
        Assert.Equal(1, prepared.Count);
        Assert.Equal(4, prepared.TabbedPieceCount);
        Assert.Equal(100 - 16, drawing.Area, 6);
        form.Close();
    });

    [Fact]
    public void PickingTheOutline_ExplainsAndAddsNothing() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new TabbedCutoutTestForm();
        Show(form);
        form.LoadDrawings([Part()]);
        var view = Field<EntityView>(form, "entityView1");

        Invoke(form, "OnAddTabbedCutoutClicked", null, EventArgs.Empty);
        Mouse(view, "OnMouseClick", view.PointWorldToControl(new Vector(5, 0)));

        Assert.StartsWith("Could not make a tabbed cutout from that hole", Assert.Single(form.Messages));
        Assert.Empty(Field<FileListControl>(form, "fileList").SelectedItem!.TabbedCutouts);
        form.Close();
    });

    [Fact]
    public void ReopenedDrawing_ShowsItsTabbedCutout() => ArchUnitsTests.RunSta(() =>
    {
        var source = Part();
        var hole = source.SourceEntities!.Where(e => e is Line l && l.StartPoint.X is > 2 and < 8
            && l.StartPoint.Y is > 2 and < 8).ToList();
        var result = new CadImportResult { Entities = source.SourceEntities!, Name = "reopened" };
        var tabbed = CadImporter.BuildDrawing(result, source.SourceEntities!, new List<Bend>(), 1, null, null, null,
            [new TabbedCutout { EdgeIds = hole.Select(e => e.Id).ToList() }]);

        using var form = new TabbedCutoutTestForm();
        Show(form);
        form.LoadDrawings([tabbed]);

        var layout = Assert.Single(Field<EntityView>(form, "entityView1").TabbedCutoutLayouts);
        Assert.True(layout.IsValid, layout.Error);
        form.Close();
    });

    private static Drawing Part()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 10), new Line(10, 10, 0, 10), new Line(0, 10, 0, 0),
            new Line(3, 3, 7, 3), new Line(7, 3, 7, 7), new Line(7, 7, 3, 7), new Line(3, 7, 3, 3),
        };
        foreach (var e in entities)
            e.Layer = new Layer("0") { IsVisible = true };
        return CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "tabbed" }, entities,
            new List<Bend>(), 1, null, null);
    }

    private static void Show(Form form)
    {
        form.WindowState = FormWindowState.Normal;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.Size = new System.Drawing.Size(1200, 800);
        form.Show();
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().BaseType!.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Invoke(CadConverterForm form, string name, params object?[] args) =>
        typeof(CadConverterForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);

    private static void Mouse(Control control, string handler, Point at) =>
        typeof(Control).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic, [typeof(MouseEventArgs)])!
            .Invoke(control, [new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0)]);

    private sealed class TabbedCutoutTestForm : CadConverterForm
    {
        public List<string> Messages { get; } = new();

        public Action<TabbedCutoutDialog> Settings { get; set; } = _ => { };

        internal override bool AskTabbedCutoutSettings(TabbedCutoutDialog dialog)
        {
            Settings(dialog);
            return true;
        }

        internal override void ShowTabbedCutoutMessage(string message) => Messages.Add(message);
    }
}
