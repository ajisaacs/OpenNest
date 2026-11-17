using System.Linq;
using OpenNest.Desktop.Interaction;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests.Interaction;

public class DrawingPlacementTests
{
    private readonly Plate plate = InteractionFixtures.Plate();
    private readonly TestToolHost host;
    private readonly Drawing drawing = new("dropped", InteractionFixtures.Rect(10, 6));

    public DrawingPlacementTests()
    {
        host = new TestToolHost(plate);
    }

    private static SettleOrder Order() => SettleOrder.ClosestToCorner;

    [Fact]
    public void A_drop_adds_one_part_centred_on_the_drop_point_as_one_step()
    {
        var recorder = host.RecordHistory();
        var count = plate.Parts.Count;

        var part = DrawingPlacement.AddPart(host, drawing, new Vector(60, 50));

        Assert.NotNull(part);
        Assert.Equal(count + 1, plate.Parts.Count);
        Assert.Same(part, plate.Parts[^1]);
        Assert.Same(drawing, part.BaseDrawing);
        Assert.True(part.BoundingBox.Center.DistanceTo(new Vector(60, 50)) < 1e-9,
            $"centred at {part.BoundingBox.Center}");
        Assert.Equal(new[] { "Add Part" }, host.ChangeLabels);
        Assert.Equal(1, recorder.History.UndoCount);
    }

    [Fact]
    public void A_drop_keeps_the_cut_offs_as_they_are()
    {
        var crossing = new Part(plate.Parts[1].BaseDrawing, new Vector(75, 40));
        plate.Parts.Add(crossing);
        plate.RegenerateCutOffs(host.CutOffSettings);
        var strokes = PlateEditsTests.CutOffStrokes(plate);
        host.ThrowOnCutOffSettings = true;

        DrawingPlacement.AddPart(host, drawing, new Vector(20, 80));

        Assert.Equal(strokes, PlateEditsTests.CutOffStrokes(plate));
    }

    [Fact]
    public void A_drop_does_nothing_while_locked()
    {
        var count = plate.Parts.Count;
        host.EditsLocked = true;

        Assert.Null(DrawingPlacement.AddPart(host, drawing, new Vector(60, 50)));

        Assert.Equal(count, plate.Parts.Count);
        Assert.Empty(host.ChangeLabels);
    }

    [Fact]
    public void A_drop_does_nothing_without_a_plate()
    {
        var count = plate.Parts.Count;
        host.Plate = null;

        Assert.Null(DrawingPlacement.AddPart(host, drawing, new Vector(60, 50)));

        Assert.Equal(count, plate.Parts.Count);
        Assert.Empty(host.ChangeLabels);
    }

    [Fact]
    public void Undo_takes_a_dropped_part_off_and_redo_puts_it_back()
    {
        var recorder = host.RecordHistory();
        // The list offers the nest's own drawings.
        recorder.Document.Nest.Drawings.Add(drawing);
        recorder.Reset();
        static int Real(Plate p) => p.Parts.Count(part => !part.BaseDrawing.IsCutOff);
        DrawingPlacement.AddPart(host, drawing, new Vector(60, 50));

        recorder.Undo();
        var undone = recorder.Document.Nest.Plates[0];
        Assert.DoesNotContain(undone.Parts, p => p.BaseDrawing.Name == "dropped");
        var count = Real(undone);
        recorder.Redo();

        var restored = recorder.Document.Nest.Plates[0];
        Assert.Equal(count + 1, Real(restored));
        var dropped = restored.Parts.Single(p => p.BaseDrawing.Name == "dropped");
        Assert.True(dropped.BoundingBox.Center.DistanceTo(new Vector(60, 50)) < 1e-9);
    }

    [Fact]
    public void An_off_centre_drawing_is_centred_on_its_bounding_box()
    {
        // A drawing whose program does not start at its bounding box's corner.
        var offset = new Drawing("offset", InteractionFixtures.Rect(8, 4, 3, 7));

        var part = DrawingPlacement.AddPart(host, offset, new Vector(40, 30));

        Assert.True(part.BoundingBox.Center.DistanceTo(new Vector(40, 30)) < 1e-9,
            $"centred at {part.BoundingBox.Center}");
    }

    [Fact]
    public void The_place_tool_for_a_drawing_places_a_new_part_of_it()
    {
        var tool = DrawingPlacement.CreatePlaceTool(host, drawing, Order);
        host.Tools.SetTool(tool);
        var count = plate.Parts.Count;

        tool.PointerMoved(host.At(30, 60));
        tool.Apply(ToolModifiers.None);

        Assert.Single(tool.Previews);
        Assert.Equal(count + 1, plate.Parts.Count);
        Assert.Same(drawing, plate.Parts[^1].BaseDrawing);
        Assert.True(plate.Parts[^1].BoundingBox.Location.DistanceTo(new Vector(30, 60)) < 1e-9);
        Assert.Equal(new[] { "Add Part" }, host.ChangeLabels);
    }

    [Fact]
    public void Delete_with_a_drawing_tool_removes_nothing()
    {
        var tool = DrawingPlacement.CreatePlaceTool(host, drawing, Order);
        host.Tools.SetTool(tool);
        var count = plate.Parts.Count;
        var recorder = host.RecordHistory();

        tool.DeleteSources();

        Assert.Equal(count, plate.Parts.Count);
        Assert.Equal(0, recorder.History.UndoCount);
    }

    [Fact]
    public void A_pick_up_is_taken_once_by_the_plate()
    {
        var pickup = new DrawingPickup();
        Assert.Null(pickup.TakeOnPlateEnter(drawing));

        pickup.PickUp();

        Assert.Same(drawing, pickup.TakeOnPlateEnter(drawing));
        Assert.Null(pickup.TakeOnPlateEnter(drawing));
    }

    [Fact]
    public void A_pick_up_with_nothing_selected_stays_armed()
    {
        var pickup = new DrawingPickup();
        pickup.PickUp();

        Assert.Null(pickup.TakeOnPlateEnter(null));

        Assert.True(pickup.IsArmed);
        Assert.Same(drawing, pickup.TakeOnPlateEnter(drawing));
    }

    [Fact]
    public void Putting_down_disarms_the_pick_up()
    {
        var pickup = new DrawingPickup();
        pickup.PickUp();

        pickup.PutDown();

        Assert.False(pickup.IsArmed);
        Assert.Null(pickup.TakeOnPlateEnter(drawing));
    }
}
