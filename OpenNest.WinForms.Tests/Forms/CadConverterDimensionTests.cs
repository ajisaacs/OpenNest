using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Bending;
using OpenNest.Controls;
using OpenNest.Dimensions;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>
/// Drives the CAD Converter's overall dimensions through its real mouse handling: a 10 x 6 part
/// from (0, 0) to (10, 6).
/// </summary>
public class CadConverterDimensionTests
{
    [Fact]
    public void AddingOverallDimensions_DraggingMovesThem_RemoveDropsThem() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new DimensionTestForm();
        Show(form);
        form.LoadDrawings([Part()]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;

        Invoke(form, "OnAddDimensionsClicked", null, EventArgs.Empty);

        Assert.Equal(2, item.Dimensions.Count);
        Assert.Equal(new[] { 10.0, 6.0 }, view.DimensionLayouts.Select(l => l.Value));

        // Adding again changes nothing: each overall exists once.
        Invoke(form, "OnAddDimensionsClicked", null, EventArgs.Empty);
        Assert.Equal(2, item.Dimensions.Count);

        // Drag the horizontal dimension from below the part to above it.
        var press = view.PointWorldToControl(view.DimensionLayouts[0].MidPoint);
        var release = view.PointWorldToControl(new Vector(5, 8));
        Mouse(view, "OnMouseDown", press);
        Mouse(view, "OnMouseMove", release);
        Mouse(view, "OnMouseUp", release);
        Assert.InRange(item.Dimensions[0].Offset, 1.8, 2.2);
        Assert.InRange(view.DimensionLayouts[0].LineStart.Y, 7.8, 8.2);

        var drawing = Assert.Single(form.GetDrawings());
        Assert.Equal(item.Dimensions.Select(d => d.Offset), drawing.Dimensions.Select(d => d.Offset));

        Invoke(form, "OnDimensionRemoved", null, 0);
        Assert.Equal(DimensionKind.OverallVertical, Assert.Single(item.Dimensions).Kind);
        Assert.Single(view.DimensionLayouts);
        form.Close();
    });

    [Fact]
    public void ExcludingTheWidestEntity_RemeasuresTheDimension() => ArchUnitsTests.RunSta(() =>
    {
        var part = Part();
        var spike = new Line(10, 3, 14, 3) { Layer = part.SourceEntities![0].Layer };
        part.SourceEntities.Add(spike);

        using var form = new DimensionTestForm();
        Show(form);
        form.LoadDrawings([part]);
        var view = Field<EntityView>(form, "entityView1");

        Invoke(form, "OnAddDimensionsClicked", null, EventArgs.Empty);
        Assert.Equal(14, view.DimensionLayouts[0].Value, 9);

        Invoke(form, "OnEntityActionRequested", null,
            new EntityActionEventArgs(EntityAction.Exclude, [spike]));

        Assert.Equal(10, view.DimensionLayouts[0].Value, 9);
        form.Close();
    });

    [Fact]
    public void NothingToMeasure_ExplainsAndAddsNothing() => ArchUnitsTests.RunSta(() =>
    {
        var part = Part();
        foreach (var entity in part.SourceEntities!)
            part.SuppressedEntityIds.Add(entity.Id);

        using var form = new DimensionTestForm();
        Show(form);
        form.LoadDrawings([part]);

        Invoke(form, "OnAddDimensionsClicked", null, EventArgs.Empty);

        Assert.Single(form.Messages);
        Assert.Empty(Field<FileListControl>(form, "fileList").SelectedItem!.Dimensions);
        form.Close();
    });

    [Fact]
    public void ReopenedDrawing_ShowsItsDimensions_AndZoomFitsThem() => ArchUnitsTests.RunSta(() =>
    {
        var part = Part();
        part.Dimensions.Add(new DrawingDimension { Kind = DimensionKind.OverallHorizontal, Offset = -3 });

        using var form = new DimensionTestForm();
        Show(form);
        form.LoadDrawings([part]);

        var view = Field<EntityView>(form, "entityView1");
        var layout = Assert.Single(view.DimensionLayouts);
        Assert.True(layout.IsValid, layout.Error);
        Assert.Equal(10, layout.Value, 9);
        Assert.True(view.ClientRectangle.Contains(view.PointWorldToControl(layout.MidPoint)));

        var item = Field<FileListControl>(form, "fileList").SelectedItem!;
        item.Dimensions[0].Offset = -5;
        Assert.Equal(-3, part.Dimensions[0].Offset);
        form.Close();
    });

    [Fact]
    public void RadiusAndDiameter_FromTheEntityMenu_DragAroundTheHole_AndSurviveReopening() => ArchUnitsTests.RunSta(() =>
    {
        var part = Part();
        var hole = new Circle(5, 3, 1) { Layer = part.SourceEntities![0].Layer };
        part.SourceEntities.Add(hole);

        using var form = new DimensionTestForm();
        Show(form);
        form.LoadDrawings([part]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;

        Invoke(form, "OnEntityActionRequested", null, new EntityActionEventArgs(EntityAction.AddRadiusDimension, [hole]));
        Invoke(form, "OnEntityActionRequested", null, new EntityActionEventArgs(EntityAction.AddDiameterDimension, [hole]));
        // Each kind exists once per curve.
        Invoke(form, "OnEntityActionRequested", null, new EntityActionEventArgs(EntityAction.AddRadiusDimension, [hole]));

        Assert.Equal(new[] { DimensionKind.Radius, DimensionKind.Diameter }, item.Dimensions.Select(d => d.Kind));
        Assert.Equal(new[] { 1.0, 2.0 }, view.DimensionLayouts.Select(l => l.Value));

        // Drag the radius label to straight left of the hole, 2 past its edge.
        var press = view.PointWorldToControl(view.DimensionLayouts[0].LineEnd);
        var release = view.PointWorldToControl(new Vector(2, 3));
        Mouse(view, "OnMouseDown", press);
        Mouse(view, "OnMouseMove", release);
        Mouse(view, "OnMouseUp", release);
        Assert.InRange(item.Dimensions[0].Offset, 1.9, 2.1);
        Assert.InRange(view.DimensionLayouts[0].ArrowPoint.X, 3.99, 4.01);

        var saved = Assert.Single(form.GetDrawings());
        Assert.All(saved.Dimensions, d => Assert.Equal(hole.Id, d.EntityId));
        form.Close();

        // Reopened, both still measure the hole; excluded, they are listed as not shown.
        using var reopened = new DimensionTestForm();
        Show(reopened);
        reopened.LoadDrawings([saved]);
        var reopenedView = Field<EntityView>(reopened, "entityView1");
        Assert.Equal(new[] { 1.0, 2.0 }, reopenedView.DimensionLayouts.Select(l => l.Value));

        var reopenedHole = Field<FileListControl>(reopened, "fileList").SelectedItem!.Entities.Single(e => e.Id == hole.Id);
        Invoke(reopened, "OnEntityActionRequested", null, new EntityActionEventArgs(EntityAction.Exclude, [reopenedHole]));
        Assert.All(reopenedView.DimensionLayouts, l => Assert.False(l.IsValid));
        reopened.Close();
    });

    [Fact]
    public void DisplayOnlyCircle_ExplainsHowToDimensionIt() => ArchUnitsTests.RunSta(() =>
    {
        var part = Part();
        var hole = new Circle(5, 3, 1) { Layer = part.SourceEntities![0].Layer };
        part.SourceEntities.Add(hole);

        using var form = new DimensionTestForm();
        Show(form);
        form.LoadDrawings([part]);
        Invoke(form, "OnEntityActionRequested", null, new EntityActionEventArgs(EntityAction.Display, [hole]));

        Invoke(form, "OnEntityActionRequested", null, new EntityActionEventArgs(EntityAction.AddRadiusDimension, [hole]));

        Assert.Contains("Cut or Etch", Assert.Single(form.Messages));
        Assert.Empty(Field<FileListControl>(form, "fileList").SelectedItem!.Dimensions);
        form.Close();
    });

    private static Drawing Part()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0), new Line(10, 0, 10, 6), new Line(10, 6, 0, 6), new Line(0, 6, 0, 0),
        };
        var layer = new Layer("0") { IsVisible = true };
        foreach (var e in entities)
            e.Layer = layer;
        return CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "dimensioned" }, entities,
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

    private sealed class DimensionTestForm : CadConverterForm
    {
        public List<string> Messages { get; } = new();

        internal override void ShowDimensionMessage(string message) => Messages.Add(message);
    }
}
