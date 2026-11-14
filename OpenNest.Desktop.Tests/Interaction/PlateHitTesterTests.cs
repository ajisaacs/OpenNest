using OpenNest.Desktop.Interaction;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests.Interaction;

public class PlateHitTesterTests
{
    private readonly Plate plate = InteractionFixtures.Plate();
    private readonly PlateHitTester hits = new();

    private Part Holed => plate.Parts[0];

    private Part Square => plate.Parts[1];

    [Fact]
    public void A_window_dragged_right_contains_and_dragged_left_or_vertically_intersects()
    {
        Assert.Equal(WindowSelectionMode.Contains, new SelectionWindow(new Vector(0, 0), new Vector(5, 5)).Mode);
        Assert.Equal(WindowSelectionMode.Intersect, new SelectionWindow(new Vector(5, 0), new Vector(0, 5)).Mode);
        Assert.Equal(WindowSelectionMode.Intersect, new SelectionWindow(new Vector(5, 0), new Vector(5, 9)).Mode);
    }

    [Fact]
    public void A_segment_crossing_the_window_without_ending_in_it_intersects()
    {
        var window = new SelectionWindow(new Vector(4, 4), new Vector(6, 6));

        Assert.True(window.Intersects(new Vector(0, 5), new Vector(10, 5)));
        Assert.False(window.Intersects(new Vector(0, 0), new Vector(10, 1)));
        Assert.True(window.Intersects(new Vector(0, 0), new Vector(10, 10)));
    }

    [Fact]
    public void A_point_hits_the_material_but_not_a_hole()
    {
        Assert.Same(Holed, hits.PartAt(plate, new Vector(1, 1)));
        Assert.Null(hits.PartAt(plate, new Vector(5, 5)));
        Assert.Same(Square, hits.PartAt(plate, new Vector(25, 5)));
        Assert.Null(hits.PartAt(plate, new Vector(15, 5)));
    }

    [Fact]
    public void The_topmost_part_is_hit_where_parts_overlap()
    {
        var top = new Part(Square.BaseDrawing, new Vector(25, 5));
        plate.Parts.Add(top);

        Assert.Same(top, hits.PartAt(plate, new Vector(27, 7)));
        Assert.Same(Square, hits.PartAt(plate, new Vector(22, 2)));
    }

    [Fact]
    public void A_moved_part_is_hit_where_it_now_is()
    {
        Assert.Same(Square, hits.PartAt(plate, new Vector(25, 5)));

        Square.Offset(0, 50);

        Assert.Null(hits.PartAt(plate, new Vector(25, 5)));
        Assert.Same(Square, hits.PartAt(plate, new Vector(25, 55)));
    }

    [Fact]
    public void Cut_off_segments_are_never_hit_as_parts()
    {
        var segment = plate.Parts.Single(p => p.BaseDrawing.IsCutOff);
        var box = segment.BoundingBox;

        Assert.Null(hits.PartAt(plate, box.Center));
        Assert.DoesNotContain(segment, hits.PartsIn(plate, new SelectionWindow(new Vector(-1, -1), new Vector(101, 101))));
    }

    [Fact]
    public void A_contains_window_takes_only_parts_wholly_inside()
    {
        var window = new SelectionWindow(new Vector(-1, -1), new Vector(31, 11));

        Assert.Equal(new[] { Holed, Square }, hits.PartsIn(plate, window));

        var partial = new SelectionWindow(new Vector(-1, -1), new Vector(25, 11));
        Assert.Equal(new[] { Holed }, hits.PartsIn(plate, partial));
    }

    [Fact]
    public void A_contains_window_includes_a_part_touching_its_edge()
    {
        var window = new SelectionWindow(new Vector(20, 0), new Vector(30, 10));

        Assert.Contains(Square, hits.PartsIn(plate, window));
    }

    [Fact]
    public void An_intersect_window_takes_parts_it_touches()
    {
        var window = new SelectionWindow(new Vector(25, 5), new Vector(8, 8));

        Assert.Equal(new[] { Holed, Square }, hits.PartsIn(plate, window));
    }

    [Fact]
    public void An_intersect_window_inside_a_part_selects_it_but_one_inside_a_hole_does_not()
    {
        var inMaterial = new SelectionWindow(new Vector(27, 7), new Vector(23, 3));
        var inHole = new SelectionWindow(new Vector(6, 6), new Vector(4, 4));

        Assert.Equal(new[] { Square }, hits.PartsIn(plate, inMaterial));
        Assert.Empty(hits.PartsIn(plate, inHole));
    }

    [Fact]
    public void A_cut_off_is_hit_near_its_stroke_within_the_tolerance()
    {
        var cutOff = plate.CutOffs[0];

        Assert.Same(cutOff, hits.CutOffAt(plate, new Vector(80.4, 50), 0.5));
        Assert.Null(hits.CutOffAt(plate, new Vector(81, 50), 0.5));
    }

    [Fact]
    public void Cut_offs_are_windowed_on_their_strokes()
    {
        var cutOff = plate.CutOffs[0];

        Assert.Equal(new[] { cutOff }, hits.CutOffsIn(plate, new SelectionWindow(new Vector(85, 60), new Vector(75, 40))));
        Assert.Empty(hits.CutOffsIn(plate, new SelectionWindow(new Vector(75, 40), new Vector(85, 60))));
        Assert.Equal(new[] { cutOff }, hits.CutOffsIn(plate, new SelectionWindow(new Vector(75, -1), new Vector(85, 101))));
    }
}
