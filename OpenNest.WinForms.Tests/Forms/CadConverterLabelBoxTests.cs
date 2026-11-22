using System.Reflection;
using OpenNest.Bending;
using OpenNest.Controls;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Labels;
using static OpenNest.WinForms.Tests.FormTesting;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>Drives the CAD Converter's part number label box through its real mouse handling.</summary>
public class CadConverterLabelBoxTests
{
    [Fact]
    public void Finding_PlacesTheBox_DraggingMovesIt_ItComesBackOnTheDrawing() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new LabelTestForm();
        Show(form);
        form.LoadDrawings([Part(10, 6)]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;
        Assert.Null(item.LabelBox);
        Assert.Null(view.LabelBox);

        Invoke(form, "OnLabelBoxClick", null, EventArgs.Empty);

        Assert.NotNull(item.LabelBox);
        Assert.NotNull(view.LabelBox);
        var placed = item.LabelBox!.Center;
        Assert.InRange(placed.X, 0, 10);
        Assert.InRange(placed.Y, 0, 6);

        // Drag the body one unit right and half a unit up.
        var press = view.PointWorldToControl(placed);
        var release = view.PointWorldToControl(new Vector(placed.X + 1, placed.Y + 0.5));
        Mouse(view, "OnMouseDown", press);
        Mouse(view, "OnMouseMove", release);
        Mouse(view, "OnMouseUp", release);

        Assert.InRange(item.LabelBox!.Center.X, placed.X + 0.9, placed.X + 1.1);
        Assert.InRange(item.LabelBox.Center.Y, placed.Y + 0.4, placed.Y + 0.6);

        var drawing = Assert.Single(form.GetDrawings());
        Assert.Equal(item.LabelBox.Center, drawing.LabelBox!.Center);
        form.Close();
    });

    [Fact]
    public void DraggingACorner_ResizesAndKeepsTheOppositeCornerFixed() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new LabelTestForm();
        Show(form);
        form.LoadDrawings([Part(10, 6)]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;
        Invoke(form, "OnLabelBoxClick", null, EventArgs.Empty);
        var before = item.LabelBox!.Clone();
        var corners = before.Corners();

        var press = view.PointWorldToControl(corners[2]);
        var release = view.PointWorldToControl(new Vector(corners[2].X + 1, corners[2].Y + 1));
        Mouse(view, "OnMouseDown", press);
        Mouse(view, "OnMouseMove", release);
        Mouse(view, "OnMouseUp", release);

        var after = item.LabelBox!;
        Assert.InRange(after.Width, before.Width + 0.9, before.Width + 1.1);
        Assert.InRange(after.Height, before.Height + 0.9, before.Height + 1.1);
        Assert.InRange(after.Corners()[0].X, corners[0].X - 0.01, corners[0].X + 0.01);
        Assert.InRange(after.Corners()[0].Y, corners[0].Y - 0.01, corners[0].Y + 0.01);
        form.Close();
    });

    [Fact]
    public void NoRoom_ExplainsAndLeavesNoBox() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new LabelTestForm();
        Show(form);
        form.LoadDrawings([Part(1, 0.5)]);
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;

        Invoke(form, "OnLabelBoxClick", null, EventArgs.Empty);

        Assert.Null(item.LabelBox);
        Assert.Single(form.Messages);
        form.Close();
    });

    [Fact]
    public void ABendLine_IsKeptClear() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new LabelTestForm();
        Show(form);
        form.LoadDrawings([Part(10, 6, [new Bend { StartPoint = new Vector(5, 0), EndPoint = new Vector(5, 6) }])]);
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;

        Invoke(form, "OnLabelBoxClick", null, EventArgs.Empty);

        var xs = item.LabelBox!.Corners().Select(c => c.X).ToList();
        Assert.True(xs.Max() <= 5 - 0.125 + 1e-3 || xs.Min() >= 5 + 0.125 - 1e-3);
        form.Close();
    });

    [Fact]
    public void Remove_DropsTheBox_AndSelectingAgainShowsIt() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new LabelTestForm();
        Show(form);
        form.LoadDrawings([Part(10, 6)]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;
        Invoke(form, "OnLabelBoxClick", null, EventArgs.Empty);
        Assert.NotNull(view.LabelBox);

        form.FindLabelBox(item);
        Assert.NotNull(item.LabelBox);
        Invoke(form, "RefreshLabelBox", item);
        Assert.NotNull(view.LabelBox);

        item.LabelBox = null;
        Invoke(form, "RefreshLabelBox", item);
        Assert.Null(view.LabelBox);
        Assert.Null(Assert.Single(form.GetDrawings()).LabelBox);
        form.Close();
    });

    private static Drawing Part(double width, double height, List<Bend>? bends = null)
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, width, 0), new Line(width, 0, width, height),
            new Line(width, height, 0, height), new Line(0, height, 0, 0),
        };
        var layer = new Layer("0") { IsVisible = true };
        foreach (var e in entities)
            e.Layer = layer;
        return CadImporter.BuildDrawing(new CadImportResult { Entities = entities, Name = "labelled" }, entities,
            bends ?? new List<Bend>(), 1, null, null);
    }

    private static T Field<T>(object owner, string name) =>
        TestReflection.Field<T>(owner.GetType().BaseType!, owner, name);

    private static void Invoke(CadConverterForm form, string name, params object?[] args) =>
        typeof(CadConverterForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);

    private sealed class LabelTestForm : CadConverterForm
    {
        public List<string> Messages { get; } = new();

        internal override void ShowLabelBoxMessage(string message) => Messages.Add(message);
    }
}
