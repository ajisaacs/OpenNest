using OpenNest.CNC;
using OpenNest.Desktop.Rendering;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Desktop.Tests.Rendering;

public class PartGeometryTests
{
    private static Program Square(double size = 10)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(size, 0)));
        program.Codes.Add(new LinearMove(new Vector(size, size)));
        program.Codes.Add(new LinearMove(new Vector(0, size)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        return program;
    }

    [Fact]
    public void A_plain_part_fills_its_cut_outline_and_hides_planned_leads()
    {
        var program = Square();
        program.Codes.Insert(1, new LinearMove(new Vector(0, 0)) { Layer = LayerType.Leadin });
        var part = new Part(new Drawing("square", program));

        var geometry = PartDisplayGeometry.Build(part);

        Assert.Same(geometry.Cut, geometry.Material);
        Assert.Empty(geometry.Lead);
        Assert.Empty(geometry.Display);
        Assert.Same(geometry.Material, geometry.Selection);
        Assert.True(geometry.IsCurrentFor(part));
    }

    [Fact]
    public void Break_cuts_fill_from_the_material_outline_only()
    {
        var program = Square();
        program.Codes.Add(new RapidMove(new Vector(5, 0)));
        program.Codes.Add(new LinearMove(new Vector(5, 10)) { Layer = LayerType.Break });

        var geometry = PartDisplayGeometry.Build(new Part(new Drawing("break", program)));

        Assert.Equal(2, geometry.Cut.Count);
        Assert.True(Assert.Single(geometry.Material).IsClosed);
    }

    [Fact]
    public void An_etch_only_part_is_selected_by_its_marks()
    {
        var program = Square();
        foreach (var line in program.Codes.OfType<LinearMove>())
            line.Layer = LayerType.Scribe;

        var geometry = PartDisplayGeometry.Build(new Part(new Drawing("etch", program)));

        Assert.Empty(geometry.Cut);
        Assert.Empty(geometry.Material);
        Assert.Same(geometry.Etch, geometry.Selection);
        Assert.Single(geometry.Etch);
    }

    [Fact]
    public void A_common_line_member_cut_once_fills_with_its_drawing_material()
    {
        var part = new Part(new Drawing("member", Square()));
        // Cut once: the left edge is the shared cut's, so the part's own program leaves it out.
        part.Program.Codes.RemoveAt(part.Program.Codes.Count - 1);
        part.Program.Codes.Add(new LinearMove(new Vector(0, 0)) { Layer = LayerType.CommonLine, Suppressed = true });

        var geometry = PartDisplayGeometry.Build(part);

        Assert.True(Assert.Single(geometry.Cut).Segments.Count == 3);
        var material = Assert.Single(geometry.Material);
        Assert.True(material.IsClosed);
        Assert.Equal(4, material.Segments.Count);
    }

    [Fact]
    public void A_rotation_makes_the_geometry_stale()
    {
        var part = new Part(new Drawing("square", Square()));
        var geometry = PartDisplayGeometry.Build(part);

        part.Rotate(System.Math.PI / 2);

        Assert.False(geometry.IsCurrentFor(part));
    }

    [Fact]
    public void Labels_sit_inside_the_material_and_cut_off_segments_at_their_middle()
    {
        var drawing = new Drawing("square", Square(20));
        var point = PartLabels.LabelPoint(drawing);
        Assert.InRange(point.X, 9, 11);
        Assert.InRange(point.Y, 9, 11);

        var part = new Part(drawing, new Vector(100, 50));
        var placed = PartLabels.Place(part, point);
        Assert.Equal(point.X + 100, placed.X, 9);
        Assert.Null(PartLabels.CutOffSegmentPoint(part));
    }
}

public class PlateOverlaysTests
{
    private static Program Square(double size)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(size, 0)));
        program.Codes.Add(new LinearMove(new Vector(size, size)));
        program.Codes.Add(new LinearMove(new Vector(0, size)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        return program;
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, -96, 0)]
    [InlineData(3, -96, -48)]
    [InlineData(4, 0, -48)]
    public void The_sheet_is_placed_in_its_quadrant(int quadrant, double left, double bottom)
    {
        var plate = new Plate(new Size(48, 96)) { Quadrant = quadrant, EdgeSpacing = new Spacing(1, 2, 3, 4) };

        var sheet = PlateOverlays.Sheet(plate);
        var edge = PlateOverlays.EdgeSpacing(plate);

        Assert.Equal((left, bottom, 96.0, 48.0), (sheet.X, sheet.Y, sheet.Length, sheet.Width));
        Assert.Equal(sheet.X + plate.EdgeSpacing.Left, edge.X, 9);
        Assert.Equal(sheet.Y + plate.EdgeSpacing.Bottom, edge.Y, 9);
        Assert.Equal(96 - plate.EdgeSpacing.Left - plate.EdgeSpacing.Right, edge.Length, 9);
        Assert.Equal(48 - plate.EdgeSpacing.Top - plate.EdgeSpacing.Bottom, edge.Width, 9);
    }

    [Fact]
    public void Pierce_points_are_every_rapid_end_with_holes_at_their_offsets()
    {
        var hole = new Program();
        hole.Codes.Add(new RapidMove(new Vector(1, 0)));
        hole.Codes.Add(new ArcMove(new Vector(1, 0), Vector.Zero));
        var program = Square(10);
        program.Codes.Add(new SubProgramCall(hole, 0) { Offset = new Vector(5, 5) });
        var plate = new Plate(new Size(48, 96));
        plate.Parts.Add(new Part(new Drawing("holed", program), new Vector(20, 10)));

        var points = PlateOverlays.PiercePoints(plate);

        Assert.Equal(new[] { new Vector(20, 10), new Vector(26, 15) }, points);
    }

    [Fact]
    public void Rapids_and_cut_direction_come_from_the_core_walkers()
    {
        var plate = new Plate(new Size(48, 96));
        plate.Parts.Add(new Part(new Drawing("a", Square(10)), new Vector(20, 10)));

        var rapid = Assert.Single(PlateOverlays.Rapids(plate));
        Assert.Equal(new Vector(20, 10), rapid.To);
        var samples = PlateOverlays.CutDirection(plate, 5);
        Assert.NotEmpty(samples);
        // The first edge runs +X.
        Assert.Equal(1, samples[0].Direction.X, 6);
    }

    [Fact]
    public void Clamps_and_cut_off_strokes_are_placed_on_the_sheet()
    {
        var plate = new Plate(new Size(48, 96)) { Quadrant = 2 };
        plate.ClampZones.Add(new ClampZone("c1", new Box(10, 0, 4, 2)));
        plate.CutOffs.Add(new CutOff(new Vector(-40, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());

        var clamp = Assert.Single(PlateOverlays.Clamps(plate));
        Assert.Equal(-86, clamp.X, 9);
        var strokes = PlateOverlays.CutOffStrokes(plate);
        Assert.NotEmpty(strokes);
        Assert.All(strokes, s => Assert.Equal(-40, s.StartPoint.X, 6));
        Assert.Empty(PlateOverlays.RemovedClamps(plate));
    }

    [Fact]
    public void The_offset_outline_grows_the_part_by_the_spacing()
    {
        var part = new Part(new Drawing("a", Square(10)));

        var outline = PlateOverlays.OffsetOutline(part, 1, 0.001);

        var outer = Assert.Single(outline);
        var bounds = outer.Vertices.GetBoundingBox();
        Assert.Equal(-1, bounds.Left, 3);
        Assert.Equal(11, bounds.Right, 3);
    }

    [Fact]
    public void Selection_bounds_are_null_when_nothing_is_selected()
    {
        Assert.Null(PlateOverlays.SelectionBounds(Array.Empty<Part>()));
        var a = new Part(new Drawing("a", Square(10)), new Vector(0, 0));
        var b = new Part(new Drawing("b", Square(10)), new Vector(20, 5));
        var box = PlateOverlays.SelectionBounds(new[] { a, b });
        Assert.Equal((0.0, 0.0, 30.0, 15.0), (box.X, box.Y, box.Length, box.Width));
    }

    [Fact]
    public void Support_analyses_are_cached_until_the_part_moves_and_skip_cut_offs()
    {
        var plate = new Plate(new Size(48, 96)) { SupportBed = new SupportGrid(2, 2) };
        var part = new Part(new Drawing("a", Square(10)), new Vector(20, 10));
        plate.Parts.Add(part);
        var cache = new SupportAnalysisCache();
        var clearance = PlateOverlays.SupportClearance(plate);

        var first = cache.Analyze(part, plate.SupportBed, plate.Quadrant, clearance);
        Assert.NotNull(first);
        Assert.Same(first, cache.Analyze(part, plate.SupportBed, plate.Quadrant, clearance));
        part.Offset(1, 0);
        Assert.NotSame(first, cache.Analyze(part, plate.SupportBed, plate.Quadrant, clearance));
        Assert.Equal(0, clearance);
    }
}

public class PlateViewOptionsTests
{
    [Fact]
    public void Defaults_match_the_desktop_view()
    {
        var options = PlateViewOptions.Read(new Dictionary<string, string>());

        Assert.True(options.DrawOrigin);
        Assert.False(options.DrawRapid);
        Assert.True(options.DrawBounds);
        Assert.False(options.DrawPiercePoints);
        Assert.False(options.DrawOffset);
        Assert.False(options.DrawCutDirection);
        Assert.False(options.DrawSupport);
    }

    [Fact]
    public void Rapids_and_bounds_round_trip_under_the_winforms_names()
    {
        var settings = new Dictionary<string, string> { ["Other"] = "kept" };
        new PlateViewOptions { DrawRapid = true, DrawBounds = false, DrawSupport = true }.WriteTo(settings);

        Assert.Equal("True", settings["PlateViewDrawRapid"]);
        Assert.Equal("False", settings["PlateViewDrawBounds"]);
        Assert.Equal(3, settings.Count);
        var read = PlateViewOptions.Read(settings);
        Assert.True(read.DrawRapid);
        Assert.False(read.DrawBounds);
        // Only rapids and bounds persist, as in WinForms.
        Assert.False(read.DrawSupport);
    }

    [Fact]
    public void Untouched_defaults_stay_out_of_the_settings()
    {
        var settings = new Dictionary<string, string> { ["Other"] = "kept" };
        new PlateViewOptions().WriteTo(settings);

        Assert.Single(settings);
    }

    [Fact]
    public void A_saved_toggle_set_back_to_its_default_is_written()
    {
        var settings = new Dictionary<string, string> { ["PlateViewDrawRapid"] = "True" };
        new PlateViewOptions().WriteTo(settings);

        Assert.Equal("False", settings["PlateViewDrawRapid"]);
        Assert.False(settings.ContainsKey("PlateViewDrawBounds"));
    }

    [Fact]
    public void Unreadable_values_are_defaults()
    {
        var read = PlateViewOptions.Read(new Dictionary<string, string>
        {
            ["PlateViewDrawRapid"] = "yes",
            ["PlateViewDrawBounds"] = "",
        });

        Assert.False(read.DrawRapid);
        Assert.True(read.DrawBounds);
    }
}
