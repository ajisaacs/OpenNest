using System.Collections.Generic;
using System.Linq;
using OpenNest.Desktop.Interaction;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Desktop.Tests.Interaction;

public class PlateEditsTests
{
    private readonly Plate plate = InteractionFixtures.Plate();
    private readonly TestToolHost host;
    private readonly PlateEdits edits;

    public PlateEditsTests()
    {
        host = new TestToolHost(plate);
        edits = new PlateEdits(host);
    }

    private PlateSelection Selection => host.Selection;

    /// <summary>Where the plate's cut-off strokes run, piece by piece.</summary>
    internal static string CutOffStrokes(Plate plate) =>
        string.Join(";", plate.Parts.Where(p => p.BaseDrawing.IsCutOff).Select(p =>
        {
            var box = p.BoundingBox;
            return $"{p.Program.Codes.Count}:{box.Left:0.###},{box.Bottom:0.###},"
                + $"{box.Right:0.###},{box.Top:0.###}";
        }));

    private static void AssertAt(Vector expected, Vector actual) =>
        Assert.True(expected.DistanceTo(actual) < 1e-9, $"expected {expected}, was {actual}");

    [Fact]
    public void Delete_removes_selected_parts_and_cut_offs_and_clears_the_selection()
    {
        var part = plate.Parts[1];
        Selection.Select(part);
        Selection.Select(plate.CutOffs[0]);

        edits.Delete();

        Assert.DoesNotContain(part, plate.Parts);
        Assert.Empty(plate.CutOffs);
        Assert.DoesNotContain(plate.Parts, p => p.BaseDrawing.IsCutOff);
        Assert.True(Selection.IsEmpty);
        Assert.Equal(new[] { "Delete" }, host.ChangeLabels);
        Assert.True(host.Invalidations > 0);
    }

    [Fact]
    public void Deleting_the_only_cut_off_alone_removes_its_strokes()
    {
        Selection.Select(plate.CutOffs[0]);

        edits.Delete();

        Assert.Empty(plate.CutOffs);
        Assert.DoesNotContain(plate.Parts, p => p.BaseDrawing.IsCutOff);
        Assert.Equal(5, plate.Parts.Count);
    }

    [Fact]
    public void Delete_rebuilds_the_remaining_cut_offs_without_the_deleted_parts_gap()
    {
        var expected = CutOffStrokes(plate);
        var crossing = new Part(plate.Parts[1].BaseDrawing, new Vector(75, 40));
        plate.Parts.Add(crossing);
        plate.RegenerateCutOffs(host.CutOffSettings);
        Assert.NotEqual(expected, CutOffStrokes(plate));
        Selection.Select(crossing);

        edits.Delete();

        Assert.Equal(expected, CutOffStrokes(plate));
    }

    [Fact]
    public void Delete_with_nothing_selected_records_no_change()
    {
        var recorder = host.RecordHistory();
        var count = plate.Parts.Count;

        edits.Delete();

        Assert.Equal(count, plate.Parts.Count);
        Assert.Empty(host.ChangeLabels);
        Assert.False(recorder.CanUndo);
    }

    [Fact]
    public void Nudge_moves_by_the_increment_and_repeats_undo_as_one_step()
    {
        var recorder = host.RecordHistory();
        Selection.Select(plate.Parts[1]);

        edits.Nudge(PushDirection.Right);
        edits.Nudge(PushDirection.Right);

        AssertAt(new Vector(40, 0), plate.Parts[1].Location);
        Assert.Equal(1, recorder.History.UndoCount);
        Assert.Equal("Nudge", recorder.History.UndoLabel);

        Selection.SelectOnly(plate.Parts[4]);
        edits.NudgeBy(0, 2);

        AssertAt(new Vector(20, 22), plate.Parts[4].Location);
        Assert.Equal(2, recorder.History.UndoCount);

        recorder.Undo();
        recorder.Undo();
        AssertAt(new Vector(20, 0), recorder.Document.Nest.Plates[0].Parts[1].Location);
    }

    [Fact]
    public void A_calculated_nudge_steps_the_selection_by_its_own_size()
    {
        var twin = InteractionFixtures.Plate();
        twin.PartSpacing = plate.PartSpacing = 1.5;
        var step = Compactor.Step(new() { twin.Parts[4] }, twin, PushDirection.Up);
        Assert.True(step > 0);
        edits.OffsetIncrement = 3;
        edits.CalculatedOffset = true;
        Selection.Select(plate.Parts[4]);

        edits.Nudge(PushDirection.Up);

        AssertAt(new Vector(20, 20 + step), plate.Parts[4].Location);
        Assert.Equal(new[] { "Nudge" }, host.ChangeLabels);
    }

    [Fact]
    public void A_fixed_nudge_moves_by_the_offset_increment()
    {
        edits.OffsetIncrement = 3;
        Selection.Select(plate.Parts[4]);

        edits.Nudge(PushDirection.Down);

        AssertAt(new Vector(20, 17), plate.Parts[4].Location);
    }

    [Fact]
    public void Push_moves_the_selection_until_it_meets_another_part()
    {
        Selection.Select(plate.Parts[4]);

        edits.Push(PushDirection.Down);

        Assert.Equal(10 + plate.PartSpacing, plate.Parts[4].BoundingBox.Bottom, 9);
        Assert.Equal(20, plate.Parts[4].Location.X, 9);
        Assert.Equal(new[] { "Push" }, host.ChangeLabels);
    }

    [Fact]
    public void A_quarter_turn_keeps_the_selection_bounds_lower_left_corner()
    {
        var recorder = host.RecordHistory();
        Selection.Select(plate.Parts[1]);
        Selection.Select(plate.Parts[4]);

        edits.RotateQuarterTurn();

        var bounds = new[] { plate.Parts[1], plate.Parts[4] }.GetBoundingBox();
        AssertAt(new Vector(20, 0), bounds.Location);
        Assert.Equal(30, bounds.Length, 9);
        Assert.Equal(10, bounds.Width, 9);
        Assert.Equal(1, recorder.History.UndoCount);
        Assert.Equal("Rotate", recorder.History.UndoLabel);

        edits.RotateQuarterTurn();

        Assert.Equal(2, recorder.History.UndoCount);
    }

    [Theory]
    [InlineData(1, false, -10)]
    [InlineData(2, false, -20)]
    [InlineData(-1, false, 10)]
    [InlineData(1, true, -1)]
    [InlineData(-3, true, 3)]
    public void The_wheel_turns_clockwise_away_from_the_user_and_finely_with_Ctrl(
        int notches, bool fine, double degrees)
    {
        var part = plate.Parts[1];
        Selection.Select(part);

        edits.RotateByWheel(notches, fine);

        var expected = Angle.ToRadians(degrees);
        Assert.Equal(System.Math.Cos(expected), System.Math.Cos(part.Rotation), 9);
        Assert.Equal(System.Math.Sin(expected), System.Math.Sin(part.Rotation), 9);
    }

    [Fact]
    public void Wheel_turns_undo_as_one_rotation()
    {
        var recorder = host.RecordHistory();
        Selection.Select(plate.Parts[1]);

        edits.RotateByWheel(1, false);
        edits.RotateByWheel(1, false);

        Assert.Equal(1, recorder.History.UndoCount);
    }

    [Fact]
    public void Without_a_selection_no_edit_changes_anything()
    {
        Selection.Select(plate.CutOffs[0]);
        var before = plate.Parts.Select(p => p.Location).ToList();

        edits.Nudge(PushDirection.Left);
        edits.NudgeBy(1, 1);
        edits.Push(PushDirection.Left);
        edits.Rotate(1);
        edits.RotateQuarterTurn();
        edits.RotateByWheel(1, false);
        edits.CalculatedOffset = true;
        edits.Nudge(PushDirection.Left);

        AssertUnmoved(before);
        Assert.Empty(host.ChangeLabels);
    }

    private void AssertUnmoved(List<Vector> before)
    {
        Assert.Equal(before.Count, plate.Parts.Count);
        for (var i = 0; i < before.Count; i++)
            AssertAt(before[i], plate.Parts[i].Location);
    }

    [Fact]
    public void Rotating_rebuilds_the_cut_offs_around_the_turned_parts()
    {
        var strip = new Part(new Drawing("strip", InteractionFixtures.Rect(10, 4)),
            new Vector(72, 40));
        plate.Parts.Add(strip);
        plate.RegenerateCutOffs(host.CutOffSettings);
        var before = CutOffStrokes(plate);
        Selection.Select(strip);

        edits.RotateQuarterTurn();

        Assert.True(strip.BoundingBox.Right < 80);
        Assert.NotEqual(before, CutOffStrokes(plate));
        var twin = InteractionFixtures.Plate();
        var turned = new Part(new Drawing("strip", InteractionFixtures.Rect(4, 10)),
            new Vector(72, 40));
        twin.Parts.Add(turned);
        twin.RegenerateCutOffs(new());
        Assert.Equal(CutOffStrokes(twin), CutOffStrokes(plate));
    }

    [Fact]
    public void A_step_that_cannot_move_records_no_undo_step()
    {
        // A flat strip has no height, so an upward step with no spacing has no distance.
        plate.PartSpacing = 0;
        var flat = new Part(new Drawing("flat", InteractionFixtures.Rect(10, 0)),
            new Vector(60, 60));
        plate.Parts.Add(flat);
        var recorder = host.RecordHistory();
        Assert.Equal(0, Compactor.Step(new() { flat }, plate, PushDirection.Up));
        edits.CalculatedOffset = true;
        Selection.Select(flat);

        edits.Nudge(PushDirection.Up);

        Assert.Equal(new[] { "Nudge" }, host.ChangeLabels);
        Assert.Equal(0, recorder.History.UndoCount);
        Assert.False(recorder.HasUnrecordedChange);
    }

    [Fact]
    public void Delete_on_a_plate_without_cut_offs_does_not_rebuild_them()
    {
        plate.CutOffs.Clear();
        plate.RegenerateCutOffs(host.CutOffSettings);
        host.ThrowOnCutOffSettings = true;
        var part = plate.Parts[1];
        Selection.Select(part);

        edits.Delete();

        Assert.DoesNotContain(part, plate.Parts);
        Assert.Equal(new[] { "Delete" }, host.ChangeLabels);
    }

    [Fact]
    public void Undo_after_Delete_restores_the_parts()
    {
        var recorder = host.RecordHistory();
        Selection.Select(plate.Parts[1]);
        Selection.Select(plate.Parts[4]);

        edits.Delete();
        Assert.Equal(3, plate.Parts.Count(p => !p.BaseDrawing.IsCutOff));
        Assert.Equal("Delete", recorder.History.UndoLabel);

        recorder.Undo();

        var restored = recorder.Document.Nest.Plates[0].Parts
            .Where(p => !p.BaseDrawing.IsCutOff).ToList();
        Assert.Equal(5, restored.Count);
        AssertAt(new Vector(20, 0), restored[1].Location);
        AssertAt(new Vector(20, 20), restored[4].Location);
    }

    [Fact]
    public void While_edits_are_locked_no_edit_changes_or_records_anything()
    {
        var recorder = host.RecordHistory();
        host.EditsLocked = true;
        Selection.Select(plate.Parts[1]);
        Selection.Select(plate.Parts[4]);
        var before = plate.Parts.Select(p => p.Location).ToList();
        var strokes = CutOffStrokes(plate);

        edits.Delete();
        edits.Nudge(PushDirection.Left);
        edits.NudgeBy(1, 1);
        edits.Push(PushDirection.Down);
        edits.Rotate(1);
        edits.RotateQuarterTurn();
        edits.RotateByWheel(1, false);
        edits.CalculatedOffset = true;
        edits.Nudge(PushDirection.Up);

        AssertUnmoved(before);
        Assert.Equal(0, plate.Parts[1].Rotation, 12);
        Assert.Single(plate.CutOffs);
        Assert.Equal(strokes, CutOffStrokes(plate));
        Assert.Equal(2, Selection.Parts.Count);
        Assert.Empty(host.ChangeLabels);
        Assert.Equal(0, recorder.History.UndoCount);
        Assert.False(recorder.HasUnrecordedChange);
    }
}
