using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Bending;
using OpenNest.Breakoffs;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// Drives the CAD Converter's breakoff tool through its real mouse handling: a 10 x 6 part with a
/// triangular breakoff (10,1) -> (12,3) -> (10,5) joined along x = 10.
/// </summary>
public class CadConverterBreakoffTests
{
    [Fact]
    public void PickingTheJoiningLine_AddsABreakoff_FlipSwapsTheScrap_DraggingMovesATab() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new BreakoffTestForm();
        Show(form);
        form.LoadDrawings([Part()]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;

        Invoke(form, "OnAddBreakoffClicked", null, EventArgs.Empty);
        Assert.True(view.IsPickingBreakoff);
        Mouse(view, "OnMouseClick", view.PointWorldToControl(new Vector(10, 3)));

        Assert.Empty(form.Messages);
        Assert.False(view.IsPickingBreakoff);
        var breakoff = Assert.Single(item.Breakoffs);
        Assert.Single(breakoff.EdgeIds);
        Assert.Equal(4, breakoff.TabCenters.Count);
        var layout = Assert.Single(view.BreakoffLayouts);
        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(4, new Shape { Entities = layout.Scrap.ToList() }.Area(), 6);

        Invoke(form, "OnBreakoffFlipped", null, 0);
        Assert.True(item.Breakoffs[0].Flip);
        Assert.Equal(60, new Shape { Entities = view.BreakoffLayouts[0].Scrap.ToList() }.Area(), 6);
        Invoke(form, "OnBreakoffFlipped", null, 0);
        breakoff = item.Breakoffs[0];
        layout = view.BreakoffLayouts[0];

        var before = breakoff.TabCenters[1];
        Mouse(view, "OnMouseDown", view.PointWorldToControl(layout.PointAt(before)));
        Mouse(view, "OnMouseMove", view.PointWorldToControl(new Vector(10, 1 + 1.75)));
        Mouse(view, "OnMouseUp", view.PointWorldToControl(new Vector(10, 1 + 1.75)));
        Assert.InRange(breakoff.TabCenters[1], 1.6, 1.9);
        Assert.NotEqual(before, breakoff.TabCenters[1]);

        var drawing = Assert.Single(form.GetDrawings());
        Assert.Equal(breakoff.TabCenters, Assert.Single(drawing.Breakoffs).TabCenters);
        var prepared = PreparedContours.Capture(drawing.Program, new CuttingParameters());
        Assert.Equal(1, prepared.Count);
        Assert.Equal(3, prepared.BreakCutCount);
        form.Close();
    });

    [Fact]
    public void PickingTheOutline_ExplainsAndAddsNothing() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new BreakoffTestForm();
        Show(form);
        form.LoadDrawings([Part()]);
        var view = Field<EntityView>(form, "entityView1");

        Invoke(form, "OnAddBreakoffClicked", null, EventArgs.Empty);
        Mouse(view, "OnMouseClick", view.PointWorldToControl(new Vector(5, 0)));

        Assert.StartsWith("Could not make a breakoff from that line", Assert.Single(form.Messages));
        Assert.Empty(Field<FileListControl>(form, "fileList").SelectedItem!.Breakoffs);
        form.Close();
    });

    [Fact]
    public void ReopenedDrawing_ShowsItsBreakoff() => ArchUnitsTests.RunSta(() =>
    {
        var source = Part();
        var edges = source.SourceEntities!.Where(e => e is Line { StartPoint.X: 10, EndPoint.X: 10 } l
            && l.Length == 4).ToList();
        var result = new CadImportResult { Entities = source.SourceEntities!, Name = "reopened" };
        var withBreakoff = CadImporter.BuildDrawing(result, source.SourceEntities!, new List<Bend>(), 1, null, null,
            [new Breakoff { EdgeIds = edges.Select(e => e.Id).ToList() }]);

        using var form = new BreakoffTestForm();
        Show(form);
        form.LoadDrawings([withBreakoff]);

        var layout = Assert.Single(Field<EntityView>(form, "entityView1").BreakoffLayouts);
        Assert.True(layout.IsValid, layout.Error);
        form.Close();
    });

    private static Drawing Part()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 1), new Line(10, 1, 10, 5), new Line(10, 5, 10, 6),
            new Line(10, 6, 0, 6), new Line(0, 6, 0, 0), new Line(10, 1, 12, 3), new Line(12, 3, 10, 5),
        };
        foreach (var e in entities)
            e.Layer = new Layer("0") { IsVisible = true };
        return CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "breakoff" }, entities,
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

    private sealed class BreakoffTestForm : CadConverterForm
    {
        public List<string> Messages { get; } = new();

        internal override bool AskBreakoffSettings(BreakoffDialog dialog) => true;

        internal override void ShowBreakoffMessage(string message) => Messages.Add(message);
    }
}
