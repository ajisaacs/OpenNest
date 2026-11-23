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
    public void NoRoom_PlacesAFullSizeBoxOnTheRoomiestSpotToAdjustByHand() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new LabelTestForm();
        Show(form);
        form.LoadDrawings([Part(1, 0.5)]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;

        Invoke(form, "OnLabelBoxClick", null, EventArgs.Empty);

        var box = item.LabelBox!;
        var full = LabelBoxOptions.Default(Units.Inches);
        Assert.Equal(full.Width, box.Width, 6);
        Assert.Equal(full.Height, box.Height, 6);
        Assert.Equal(0.5, box.Center.X, 3);
        Assert.Equal(0.25, box.Center.Y, 3);
        Assert.NotNull(view.LabelBox);
        Assert.Contains("adjust", Assert.Single(form.Messages));
        form.Close();
    });

    [Fact]
    public void NoRoomOnABentTab_StartsTheBoxOnAFlangeNotTheBendLine() => ArchUnitsTests.RunSta(() =>
    {
        // A small angle like 4980 A01 PT103: 2.04 x 1, bent up the middle.
        using var form = new LabelTestForm();
        Show(form);
        form.LoadDrawings([Part(2.04, 1, [new Bend { StartPoint = new Vector(1.02, 0), EndPoint = new Vector(1.02, 1) }])]);
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;

        Invoke(form, "OnLabelBoxClick", null, EventArgs.Empty);

        var center = item.LabelBox!.Center;
        Assert.True(System.Math.Abs(center.X - 1.02) > 0.25, $"Box centered at {center.X} sits on the bend line.");
        Assert.Single(form.Messages);
        form.Close();
    });

    [Fact]
    public void DraggingTheRotateHandle_TurnsTheBoxAboutItsCenter_InFiveDegreeSteps() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new LabelTestForm();
        Show(form);
        form.LoadDrawings([Part(10, 6)]);
        var view = Field<EntityView>(form, "entityView1");
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;
        Invoke(form, "OnLabelBoxClick", null, EventArgs.Empty);
        var before = item.LabelBox!.Clone();
        Assert.Equal(0, before.Rotation, 9);

        // The knob sits straight above the center; drag it to 31 degrees left of straight up.
        var knob = view.PointWorldToControl(before.ToWorld(new Vector(0, before.Height / 2))) - new System.Drawing.Size(0, 18);
        var angle = 31 * System.Math.PI / 180;
        var target = new Vector(before.Center.X - 2 * System.Math.Sin(angle), before.Center.Y + 2 * System.Math.Cos(angle));
        Mouse(view, "OnMouseDown", knob);
        Mouse(view, "OnMouseMove", view.PointWorldToControl(target));
        Mouse(view, "OnMouseUp", view.PointWorldToControl(target));

        var after = item.LabelBox!;
        Assert.Equal(30, after.Rotation * 180 / System.Math.PI, 6);
        Assert.Equal(before.Center, after.Center);
        Assert.Equal(before.Width, after.Width, 9);
        Assert.Equal(before.Height, after.Height, 9);
        form.Close();
    });

    [Fact]
    public void RotateNinety_TurnsAQuarterAndBackWithoutGoingUpsideDown() => ArchUnitsTests.RunSta(() =>
    {
        using var form = new LabelTestForm();
        Show(form);
        form.LoadDrawings([Part(10, 6)]);
        var item = Field<FileListControl>(form, "fileList").SelectedItem!;
        Invoke(form, "OnLabelBoxClick", null, EventArgs.Empty);

        Invoke(form, "TurnLabelBox", (Func<LabelBox, double>)(b => b.Rotation + System.Math.PI / 2));
        Assert.Equal(System.Math.PI / 2, item.LabelBox!.Rotation, 9);
        Invoke(form, "TurnLabelBox", (Func<LabelBox, double>)(b => b.Rotation + System.Math.PI / 2));
        Assert.Equal(0, item.LabelBox!.Rotation, 9);
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
