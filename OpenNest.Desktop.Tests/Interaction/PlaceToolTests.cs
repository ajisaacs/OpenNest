using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.CutOffs;
using OpenNest.Desktop.Interaction;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests.Interaction;

public class PlaceToolTests
{
    private readonly Plate plate = InteractionFixtures.Plate();
    private readonly TestToolHost host;
    private readonly PlateEdits edits;
    private int orderReads;

    public PlaceToolTests()
    {
        host = new TestToolHost(plate);
        edits = new PlateEdits(host) { Tools = host.Tools };
    }

    private SettleOrder Order()
    {
        orderReads++;
        return SettleOrder.ClosestToCorner;
    }

    private static void AssertAt(Vector expected, Vector actual) =>
        Assert.True(expected.DistanceTo(actual) < 1e-9, $"expected {expected}, was {actual}");

    private List<Part> RealParts() => plate.Parts.Where(p => !p.BaseDrawing.IsCutOff).ToList();

    private PlaceTool Copy(params Part[] parts)
    {
        host.Selection.Clear();
        host.Selection.Select(parts);
        return PlateClipboard.Copy(host, host.Tools, Order);
    }

    private static Part Triangle(double x, double y)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(40, 0)));
        program.Codes.Add(new LinearMove(new Vector(0, 40)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Part(new Drawing("triangle", program), new Vector(x, y));
    }

    private static Part Square(double size, double x, double y) =>
        new(new Drawing("square", InteractionFixtures.Rect(size, size)), new Vector(x, y));

    [Fact]
    public void Previews_follow_the_pointer_by_their_lower_left_corner()
    {
        var tool = Copy(plate.Parts[2]);

        Assert.Same(tool, host.Tools.Current);
        Assert.Equal("Clone Parts", host.Tools.Status);
        Assert.True(tool.PointerMoved(host.At(30, 60, ToolButton.None)));

        Assert.Equal(2, tool.Previews.Count);
        AssertAt(new Vector(30, 60), tool.Previews.GetBoundingBox().Location);
        AssertAt(new Vector(30, 60), tool.Previews[0].Location);
        AssertAt(new Vector(40.05, 60), tool.Previews[1].Location);
        AssertAt(new Vector(40, 0), plate.Parts[2].Location);
        Assert.True(host.Invalidations > 0);
    }

    [Fact]
    public void A_move_to_the_same_screen_point_keeps_a_nudge()
    {
        var tool = Copy(plate.Parts[1]);
        tool.PointerMoved(host.At(30, 60));
        edits.NudgeBy(5, 0);
        AssertAt(new Vector(35, 60), tool.Previews[0].Location);

        tool.PointerMoved(host.At(30, 60));
        AssertAt(new Vector(35, 60), tool.Previews[0].Location);

        tool.PointerMoved(host.At(31, 60));
        AssertAt(new Vector(31, 60), tool.Previews[0].Location);
    }

    [Fact]
    public void A_click_adds_copies_and_one_undo_removes_them()
    {
        var recorder = host.RecordHistory();
        var source = plate.Parts[1];
        var tool = Copy(source);
        var count = plate.Parts.Count;
        tool.PointerMoved(host.At(30, 60));

        Assert.True(tool.PointerPressed(host.At(30, 60)));
        tool.PointerMoved(host.At(60, 60));
        Assert.True(tool.PointerPressed(host.At(60, 60)));

        Assert.Equal(count + 2, plate.Parts.Count);
        AssertAt(new Vector(20, 0), source.Location);
        AssertAt(new Vector(30, 60), plate.Parts[count].Location);
        AssertAt(new Vector(60, 60), plate.Parts[count + 1].Location);
        Assert.DoesNotContain(tool.Previews[0], plate.Parts);
        Assert.Equal(new[] { "Add Part", "Add Part" }, host.ChangeLabels);
        Assert.Same(tool, host.Tools.Current);

        recorder.Undo();
        Assert.Equal(count + 1, recorder.Document.Nest.Plates[0].Parts.Count);
    }

    [Fact]
    public void Placing_several_parts_is_labelled_add_parts()
    {
        var tool = Copy(plate.Parts[1], plate.Parts[4]);
        tool.PointerMoved(host.At(30, 60));

        tool.Apply(ToolModifiers.None);

        Assert.Equal(new[] { "Add Parts" }, host.ChangeLabels);
    }

    [Fact]
    public void Other_buttons_do_not_place()
    {
        var tool = Copy(plate.Parts[1]);
        var count = plate.Parts.Count;

        Assert.False(tool.PointerPressed(host.At(30, 60, ToolButton.Right)));
        Assert.False(tool.PointerPressed(host.At(30, 60, ToolButton.Middle)));

        Assert.Equal(count, plate.Parts.Count);
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, true)]
    [InlineData(4, false, true)]
    public void Shift_settles_toward_the_quadrant_corner(int quadrant, bool right, bool up)
    {
        var empty = new Plate(new Size(100, 100)) { Quadrant = quadrant };
        var emptyHost = new TestToolHost(empty);
        var tool = new PlaceTool(emptyHost, new[] { Square(10, 0, 0) }, Order);
        var center = empty.WorkArea().Center;
        tool.PointerMoved(emptyHost.At(center.X, center.Y));

        tool.Apply(ToolModifiers.Shift);

        var area = empty.WorkArea();
        var box = empty.Parts.Single().BoundingBox;
        Assert.Equal(right ? area.Right : area.Left, right ? box.Right : box.Left, 9);
        Assert.Equal(up ? area.Top : area.Bottom, up ? box.Top : box.Bottom, 9);
    }

    [Fact]
    public void Shift_settles_on_geometry_and_ctrl_on_bounding_boxes()
    {
        var shiftLocation = SettleBesideTriangle(ToolModifiers.Shift, out var shiftPlate);
        var ctrlLocation = SettleBesideTriangle(ToolModifiers.Control, out _);
        var bothLocation = SettleBesideTriangle(ToolModifiers.Shift | ToolModifiers.Control, out _);

        // Pushed left onto the hypotenuse x + y = 40, inside the triangle's box but clear of it.
        AssertAt(new Vector(10, 30), shiftLocation);
        // Boxes only: it starts inside the triangle's box, so it never nests into the shape.
        AssertAt(new Vector(30, 30), ctrlLocation);
        AssertAt(shiftLocation, bothLocation);
        Assert.Equal(2, shiftPlate.Parts.Count);
        Assert.Equal(3, orderReads);
    }

    private Vector SettleBesideTriangle(ToolModifiers modifiers, out Plate triangles)
    {
        triangles = new Plate(new Size(100, 100));
        triangles.Parts.Add(Triangle(0, 0));
        var trianglesHost = new TestToolHost(triangles);
        var tool = new PlaceTool(trianglesHost, new[] { Square(10, 0, 0) }, Order);
        tool.PointerMoved(trianglesHost.At(30, 30));
        tool.Apply(modifiers);
        AssertAt(triangles.Parts[1].Location, tool.Previews[0].Location);
        return triangles.Parts[1].Location;
    }

    [Fact]
    public void The_placement_order_is_read_at_each_settling_click()
    {
        var tool = Copy(plate.Parts[1]);
        Assert.Equal(0, orderReads);
        tool.PointerMoved(host.At(30, 60));

        tool.Apply(ToolModifiers.None);
        Assert.Equal(0, orderReads);
        tool.Apply(ToolModifiers.Shift);
        tool.Apply(ToolModifiers.Control);

        Assert.Equal(2, orderReads);
    }

    [Fact]
    public void Copied_common_lines_join_the_placed_copies_with_the_source_kerf()
    {
        var tool = Copy(plate.Parts[2]);
        tool.PointerMoved(host.At(20, 60));

        tool.Apply(ToolModifiers.None);

        var placed = plate.Parts.Skip(plate.Parts.Count - 2).ToList();
        var group = plate.CommonLineGroupOf(placed[0]);
        Assert.NotNull(group);
        Assert.True(group.Contains(placed[1]));
        Assert.Equal(0.05, group.Kerf, 12);
        Assert.Equal(2, plate.CommonLines.Count);
    }

    [Fact]
    public void A_click_rebuilds_the_cut_offs_around_the_copy()
    {
        var before = PlateEditsTests.CutOffStrokes(plate);
        var tool = Copy(plate.Parts[1]);
        tool.PointerMoved(host.At(75, 40));

        tool.Apply(ToolModifiers.None);

        Assert.NotEqual(before, PlateEditsTests.CutOffStrokes(plate));
        var twin = InteractionFixtures.Plate();
        twin.Parts.Add(new Part(plate.Parts[1].BaseDrawing, new Vector(75, 40)));
        twin.RegenerateCutOffs(new CutOffSettings());
        Assert.Equal(PlateEditsTests.CutOffStrokes(twin), PlateEditsTests.CutOffStrokes(plate));
    }

    [Fact]
    public void Delete_removes_the_sources_and_selected_parts_as_one_step()
    {
        var recorder = host.RecordHistory();
        var source = plate.Parts[1];
        var other = plate.Parts[4];
        var tool = Copy(source);
        host.Selection.Select(other);

        edits.Delete();

        Assert.DoesNotContain(source, plate.Parts);
        Assert.DoesNotContain(other, plate.Parts);
        Assert.Equal(3, RealParts().Count);
        Assert.True(host.Selection.IsEmpty);
        Assert.Equal(new[] { "Delete" }, host.ChangeLabels);
        Assert.Equal(1, recorder.History.UndoCount);
        Assert.Single(tool.Previews);
        Assert.Same(tool, host.Tools.Current);
    }

    [Fact]
    public void Delete_after_a_cut_records_nothing()
    {
        var recorder = host.RecordHistory();
        host.Selection.Select(plate.Parts[1]);
        var tool = PlateClipboard.Cut(host, host.Tools, Order);
        Assert.Equal(1, recorder.History.UndoCount);

        tool.DeleteSources();

        Assert.Equal(1, recorder.History.UndoCount);
        Assert.Equal("Cut", recorder.History.UndoLabel);
    }

    [Fact]
    public void Delete_with_no_source_left_keeps_the_cut_offs()
    {
        var crossing = new Part(plate.Parts[1].BaseDrawing, new Vector(75, 40));
        plate.Parts.Add(crossing);
        plate.RegenerateCutOffs(host.CutOffSettings);
        host.Selection.Select(crossing);
        var tool = PlateClipboard.Cut(host, host.Tools, Order);
        var strokes = PlateEditsTests.CutOffStrokes(plate);
        // A rebuild now would give the cut-offs new programs: an unrecorded change.
        host.ThrowOnCutOffSettings = true;

        tool.DeleteSources();

        Assert.Equal(strokes, PlateEditsTests.CutOffStrokes(plate));
    }

    [Fact]
    public void Delete_rebuilds_the_cut_offs()
    {
        var crossing = new Part(plate.Parts[1].BaseDrawing, new Vector(75, 40));
        var expected = PlateEditsTests.CutOffStrokes(plate);
        plate.Parts.Add(crossing);
        plate.RegenerateCutOffs(host.CutOffSettings);
        var tool = Copy(crossing);

        tool.DeleteSources();

        Assert.Equal(expected, PlateEditsTests.CutOffStrokes(plate));
    }

    [Fact]
    public void Cut_removes_the_sources_in_one_step_without_rebuilding_cut_offs()
    {
        var recorder = host.RecordHistory();
        var crossing = new Part(plate.Parts[1].BaseDrawing, new Vector(75, 40));
        plate.Parts.Add(crossing);
        plate.RegenerateCutOffs(host.CutOffSettings);
        var strokes = PlateEditsTests.CutOffStrokes(plate);
        recorder.Reset();
        host.Selection.Select(crossing);
        host.Selection.Select(plate.Parts[2]);

        var tool = PlateClipboard.Cut(host, host.Tools, Order);

        Assert.Same(tool, host.Tools.Current);
        Assert.Equal(3, tool.Previews.Count);
        Assert.DoesNotContain(crossing, plate.Parts);
        Assert.Equal(3, RealParts().Count);
        Assert.Equal(strokes, PlateEditsTests.CutOffStrokes(plate));
        Assert.True(host.Selection.IsEmpty);
        Assert.Equal(new[] { "Cut" }, host.ChangeLabels);
        Assert.Equal(1, recorder.History.UndoCount);

        // The cut group is joined again among its copies.
        tool.PointerMoved(host.At(10, 60));
        tool.Apply(ToolModifiers.None);
        Assert.Single(plate.CommonLines);

        recorder.Undo();
        recorder.Undo();
        Assert.Equal(6, recorder.Document.Nest.Plates[0].Parts.Count(p => !p.BaseDrawing.IsCutOff));
    }

    [Fact]
    public void Copy_and_cut_need_selected_parts_on_the_plate()
    {
        Assert.Null(PlateClipboard.Copy(host, host.Tools, Order));
        Assert.Null(PlateClipboard.Cut(host, host.Tools, Order));

        host.Selection.Select(plate.Parts[1]);
        plate.Parts.RemoveAt(1);
        Assert.Null(PlateClipboard.Cut(host, host.Tools, Order));

        Assert.IsType<SelectTool>(host.Tools.Current);
        Assert.Empty(host.ChangeLabels);
    }

    [Fact]
    public void Copy_keeps_the_sources_on_the_plate()
    {
        var count = plate.Parts.Count;
        var tool = Copy(plate.Parts[1], plate.Parts[4]);

        Assert.Equal(count, plate.Parts.Count);
        Assert.Equal(2, tool.Previews.Count);
        Assert.Empty(host.ChangeLabels);
    }

    [Fact]
    public void Edits_move_the_previews_without_an_undo_step()
    {
        var recorder = host.RecordHistory();
        var tool = Copy(plate.Parts[1]);
        tool.PointerMoved(host.At(30, 60));
        var before = RealParts().Select(p => p.Location).ToList();
        var strokes = PlateEditsTests.CutOffStrokes(plate);
        Assert.True(edits.HasPartsToEdit);

        edits.Nudge(PushDirection.Right);
        AssertAt(new Vector(40, 60), tool.Previews[0].Location);
        edits.Push(PushDirection.Down);
        AssertAt(new Vector(40, 10), tool.Previews[0].Location);
        edits.Step(PushDirection.Up);
        // A step adds a hair over the part's height.
        Assert.True(new Vector(40, 20).DistanceTo(tool.Previews[0].Location) < 1e-4);
        tool.Previews[0].Offset(new Vector(36, 0));
        // Rebuilding cut-offs would throw: previews are not on the plate.
        host.ThrowOnCutOffSettings = true;
        edits.RotateQuarterTurn();
        edits.RotateByWheel(1, fine: false);

        Assert.True(new Vector(76, 20).DistanceTo(tool.Previews[0].BoundingBox.Location) < 1e-4);
        Assert.NotEqual(0, tool.Previews[0].Rotation, 6);
        Assert.Equal(before, RealParts().Select(p => p.Location).ToList());
        Assert.Equal(strokes, PlateEditsTests.CutOffStrokes(plate));
        Assert.Empty(host.ChangeLabels);
        Assert.False(recorder.CanUndo);
    }

    [Fact]
    public void Without_tools_or_a_place_tool_edits_move_the_selection()
    {
        Assert.False(edits.HasPartsToEdit);
        host.Selection.Select(plate.Parts[4]);
        Assert.True(edits.HasPartsToEdit);

        edits.NudgeBy(1, 0);

        AssertAt(new Vector(21, 20), plate.Parts[4].Location);
        Assert.Equal(new[] { "Nudge" }, host.ChangeLabels);
    }

    [Fact]
    public void Locked_edits_refuse_placing_deleting_cutting_and_copying()
    {
        var tool = Copy(plate.Parts[1]);
        tool.PointerMoved(host.At(30, 60));
        var count = plate.Parts.Count;
        host.Selection.Select(plate.Parts[4]);
        host.EditsLocked = true;

        Assert.False(tool.PointerPressed(host.At(30, 60)));
        // WinForms raises no mouse move while locked, so the copy stays put.
        Assert.False(tool.PointerMoved(host.At(50, 20)));
        tool.Apply(ToolModifiers.None);
        tool.DeleteSources();
        edits.Delete();
        edits.NudgeBy(5, 0);
        Assert.Null(PlateClipboard.Copy(host, host.Tools, Order));
        Assert.Null(PlateClipboard.Cut(host, host.Tools, Order));

        Assert.Equal(count, plate.Parts.Count);
        AssertAt(new Vector(30, 60), tool.Previews[0].Location);
        Assert.Same(tool, host.Tools.Current);
        Assert.Empty(host.ChangeLabels);
    }

    [Fact]
    public void Without_a_plate_a_click_does_nothing()
    {
        var tool = Copy(plate.Parts[1]);
        host.Plate = null;

        Assert.False(tool.PointerPressed(host.At(30, 60)));
        tool.Apply(ToolModifiers.None);
        tool.DeleteSources();

        Assert.Empty(host.ChangeLabels);
    }

    [Fact]
    public void The_tool_survives_a_plate_change_with_its_previews()
    {
        var tool = Copy(plate.Parts[2]);
        var other = new Plate(new Size(100, 100));
        host.Plate = other;
        host.Selection.Plate = other;

        host.Tools.OnPlateChanged();

        Assert.Same(tool, host.Tools.Current);
        Assert.Equal(2, tool.Previews.Count);
        tool.PointerMoved(host.At(10, 10));
        tool.Apply(ToolModifiers.None);
        Assert.Equal(2, other.Parts.Count);
        Assert.Single(other.CommonLines);
    }

    [Fact]
    public void A_failing_settle_opens_no_step()
    {
        host.RecordHistory();
        host.Selection.Select(plate.Parts[1]);
        var tool = new PlaceTool(host, host.Selection.Parts.ToList(),
            () => throw new System.InvalidOperationException("settings unreadable"));
        host.Tools.SetTool(tool);
        var count = RealParts().Count;

        Assert.Throws<System.InvalidOperationException>(() => tool.Apply(ToolModifiers.Shift));

        // Nothing touched the nest, so no step opened and no restore replaced its objects.
        Assert.Empty(host.ChangeLabels);
        Assert.Same(plate, host.Recorder.Document.Nest.Plates[0]);
        Assert.Equal(count, RealParts().Count);
    }

    [Fact]
    public void A_failing_cut_off_rebuild_rolls_the_placement_back()
    {
        var crossing = new Part(plate.Parts[1].BaseDrawing, new Vector(75, 40));
        plate.Parts.Add(crossing);
        plate.RegenerateCutOffs(host.CutOffSettings);
        var recorder = host.RecordHistory();
        var count = RealParts().Count;
        var tool = Copy(plate.Parts[2]);
        host.ThrowOnCutOffSettings = true;

        Assert.ThrowsAny<System.Exception>(() => tool.Apply(ToolModifiers.None));

        Assert.Equal(0, recorder.History.UndoCount);
        Assert.Equal(count,
            recorder.Document.Nest.Plates[0].Parts.Count(p => !p.BaseDrawing.IsCutOff));
        // Whether the tool survives is the view's call: a rollback swaps the nest in, and the
        // Avalonia view then ends the tool, as WinForms does.
    }

    [Fact]
    public void A_failing_cut_off_rebuild_rolls_the_delete_back()
    {
        var crossing = new Part(plate.Parts[1].BaseDrawing, new Vector(75, 40));
        plate.Parts.Add(crossing);
        plate.RegenerateCutOffs(host.CutOffSettings);
        var recorder = host.RecordHistory();
        var count = RealParts().Count;
        var tool = Copy(crossing);
        host.ThrowOnCutOffSettings = true;

        Assert.ThrowsAny<System.Exception>(() => tool.DeleteSources());

        Assert.Equal(0, recorder.History.UndoCount);
        Assert.Equal(count,
            recorder.Document.Nest.Plates[0].Parts.Count(p => !p.BaseDrawing.IsCutOff));
    }

    [Fact]
    public void Turning_the_copy_after_a_click_leaves_the_placed_part()
    {
        var tool = Copy(plate.Parts[2]);
        tool.PointerMoved(host.At(60, 60));
        tool.Apply(ToolModifiers.None);
        var placed = plate.Parts[^1];
        var rotation = placed.Rotation;
        var location = placed.Location;

        edits.RotateQuarterTurn();
        edits.NudgeBy(5, 0);

        Assert.Equal(rotation, placed.Rotation);
        AssertAt(location, placed.Location);
    }

    [Fact]
    public void A_second_escape_resumes_the_tool()
    {
        var tool = Copy(plate.Parts[1]);

        host.Tools.Escape();
        host.Tools.Escape();

        Assert.Same(tool, host.Tools.Current);
    }

    [Fact]
    public void Escape_ends_the_tool()
    {
        Copy(plate.Parts[1]);

        host.Tools.Escape();

        Assert.IsType<SelectTool>(host.Tools.Current);
    }
}
