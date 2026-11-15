using System;
using OpenNest.Desktop.Interaction;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests.Interaction;

public class SelectToolMoveTests
{
    private readonly Plate plate = InteractionFixtures.Plate();
    private readonly TestToolHost host;

    public SelectToolMoveTests()
    {
        host = new TestToolHost(plate);
    }

    private PlateTool Tool => host.Tools.Current;

    private static void AssertAt(Vector expected, Vector actual) =>
        Assert.True(expected.DistanceTo(actual) < 1e-9, $"expected {expected}, was {actual}");

    [Fact]
    public void Dragging_a_part_moves_every_selected_part_as_one_Move_step()
    {
        var recorder = host.RecordHistory();
        host.Click(25, 25);
        host.Click(25, 5, ToolModifiers.Control);

        Tool.PointerPressed(host.At(25, 5));
        Tool.PointerMoved(host.At(27, 6));
        Tool.PointerMoved(host.At(30, 8));
        Tool.PointerReleased(host.At(30, 8));

        AssertAt(new Vector(25, 3), plate.Parts[1].Location);
        AssertAt(new Vector(25, 23), plate.Parts[4].Location);
        Assert.Equal(1, recorder.History.UndoCount);
        Assert.Equal("Move", recorder.History.UndoLabel);
        Assert.False(Assert.IsType<SelectTool>(Tool).IsMoving);
    }

    [Fact]
    public void A_window_drag_moves_nothing()
    {
        host.Click(25, 5);

        host.Drag(15, -1, 31, 11);

        AssertAt(new Vector(20, 0), plate.Parts[1].Location);
        Assert.Equal(new[] { plate.Parts[1] }, host.Selection.Parts);
        // The one Move label is the earlier click on the part; the window drag began none.
        Assert.Equal(new[] { "Move" }, host.ChangeLabels);
    }

    [Fact]
    public void Cancelling_mid_drag_keeps_the_partial_move_as_one_step()
    {
        var recorder = host.RecordHistory();
        Tool.PointerPressed(host.At(25, 5));
        Tool.PointerMoved(host.At(30, 5));

        Tool.Cancel();

        AssertAt(new Vector(25, 0), plate.Parts[1].Location);
        Assert.True(host.Selection.IsEmpty);
        Assert.Equal(1, recorder.History.UndoCount);
        Assert.False(recorder.IsChangeInProgress);
    }

    [Fact]
    public void A_click_on_a_part_records_nothing_even_with_cut_offs_on_the_plate()
    {
        var recorder = host.RecordHistory();

        host.Click(25, 5);
        host.Click(25, 25);

        Assert.False(recorder.HasUnrecordedChange);
        Assert.Equal(0, recorder.History.UndoCount);
    }

    [Fact]
    public void Releasing_a_drag_rebuilds_the_cut_offs_around_the_moved_part()
    {
        var before = PlateEditsTests.CutOffStrokes(plate);

        host.Drag(25, 25, 80, 25);

        AssertAt(new Vector(75, 20), plate.Parts[4].Location);
        Assert.NotEqual(before, PlateEditsTests.CutOffStrokes(plate));
    }

    [Fact]
    public void Showing_another_plate_mid_drag_ends_the_move_as_one_Move_step()
    {
        var recorder = host.RecordHistory();
        Tool.PointerPressed(host.At(25, 5));
        Tool.PointerMoved(host.At(30, 5));

        host.Tools.OnPlateChanged();

        AssertAt(new Vector(25, 0), plate.Parts[1].Location);
        Assert.Equal(1, recorder.History.UndoCount);
        Assert.Equal("Move", recorder.History.UndoLabel);
        Assert.False(recorder.IsChangeInProgress);
    }

    [Fact]
    public void While_edits_are_locked_a_press_selects_nothing_and_a_drag_moves_nothing()
    {
        var recorder = host.RecordHistory();
        host.Click(25, 5);
        host.EditsLocked = true;
        var strokes = PlateEditsTests.CutOffStrokes(plate);

        Assert.False(Tool.PointerPressed(host.At(25, 25)));
        host.Drag(25, 25, 80, 25);
        host.Drag(60, 60, 90, 90);
        host.Click(80, 50);

        Assert.False(Assert.IsType<SelectTool>(Tool).IsMoving);
        AssertAt(new Vector(20, 20), plate.Parts[4].Location);
        Assert.Equal(new[] { plate.Parts[1] }, host.Selection.Parts);
        Assert.Empty(host.Selection.CutOffs);
        Assert.Equal(strokes, PlateEditsTests.CutOffStrokes(plate));
        // The one Move label is the unlocked click before the lock.
        Assert.Equal(new[] { "Move" }, host.ChangeLabels);
        Assert.Equal(0, recorder.History.UndoCount);
        Assert.False(recorder.HasUnrecordedChange);
    }

    [Fact]
    public void A_move_open_when_the_lock_appears_finishes_at_its_release()
    {
        var recorder = host.RecordHistory();
        Tool.PointerPressed(host.At(25, 5));
        Tool.PointerMoved(host.At(27, 5));
        host.EditsLocked = true;
        Tool.PointerMoved(host.At(30, 5));
        Tool.PointerReleased(host.At(30, 5));

        AssertAt(new Vector(25, 0), plate.Parts[1].Location);
        Assert.Equal(1, recorder.History.UndoCount);
        Assert.False(recorder.IsChangeInProgress);
    }

    [Fact]
    public void A_release_whose_cut_off_rebuild_throws_rolls_the_move_back()
    {
        var recorder = host.RecordHistory();
        Tool.PointerPressed(host.At(25, 25));
        Tool.PointerMoved(host.At(80, 25));
        host.ThrowOnCutOffSettings = true;

        Assert.Throws<InvalidOperationException>(() => Tool.PointerReleased(host.At(80, 25)));

        Assert.False(Assert.IsType<SelectTool>(Tool).IsMoving);
        Assert.False(recorder.IsChangeInProgress);
        Assert.Equal(0, recorder.History.UndoCount);
        var restored = recorder.Document.Nest.Plates[0];
        Assert.NotSame(plate, restored);
        AssertAt(new Vector(20, 20), restored.Parts[4].Location);
        AssertAt(new Vector(20, 0), restored.Parts[1].Location);
        Assert.False(recorder.HasUnrecordedChange);
    }

    [Fact]
    public void Losing_the_pointer_mid_drag_ends_the_move_as_one_step_and_keeps_the_selection()
    {
        var recorder = host.RecordHistory();
        var before = PlateEditsTests.CutOffStrokes(plate);
        Tool.PointerPressed(host.At(25, 25));
        Tool.PointerMoved(host.At(50, 25));
        Tool.PointerMoved(host.At(80, 25));

        Tool.PointerCaptureLost();

        AssertAt(new Vector(75, 20), plate.Parts[4].Location);
        Assert.NotEqual(before, PlateEditsTests.CutOffStrokes(plate));
        Assert.Equal(new[] { plate.Parts[4] }, host.Selection.Parts);
        Assert.False(Assert.IsType<SelectTool>(Tool).IsMoving);
        Assert.Equal(1, recorder.History.UndoCount);
        Assert.Equal("Move", recorder.History.UndoLabel);
        Assert.False(recorder.IsChangeInProgress);
        Assert.False(recorder.HasUnrecordedChange);
    }

    [Fact]
    public void Losing_the_pointer_mid_window_drops_the_window_without_selecting()
    {
        Tool.PointerPressed(host.At(15, 15));
        Tool.PointerMoved(host.At(35, 35));

        Tool.PointerCaptureLost();
        Tool.PointerReleased(host.At(35, 35));

        Assert.Null(Assert.IsType<SelectTool>(Tool).Window);
        Assert.True(host.Selection.IsEmpty);
    }
}
