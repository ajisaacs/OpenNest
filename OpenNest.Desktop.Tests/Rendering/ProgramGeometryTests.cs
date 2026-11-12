using OpenNest.CNC;
using OpenNest.Desktop.Rendering;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests.Rendering;

public class ProgramGeometryTests
{
    private static LinearMove Line(double x, double y, LayerType layer = LayerType.Cut, bool suppressed = false) =>
        new(new Vector(x, y)) { Layer = layer, Suppressed = suppressed };

    private static Program Square(double size = 10, LayerType layer = LayerType.Cut)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(Line(size, 0, layer));
        program.Codes.Add(Line(size, size, layer));
        program.Codes.Add(Line(0, size, layer));
        program.Codes.Add(Line(0, 0, layer));
        return program;
    }

    private static Vector[] Points(PathFigure figure) =>
        new[] { figure.Start }.Concat(figure.Segments.Select(s => s.End)).ToArray();

    private static void Near(Vector expected, Vector actual) =>
        Assert.True(expected.DistanceTo(actual) < 1e-9, $"expected {expected}, got {actual}");

    [Fact]
    public void A_closed_square_is_one_closed_cut_figure_filled_as_cut()
    {
        var layers = ProgramGeometry.Layers(Square());

        var cut = Assert.Single(layers.Cut);
        Assert.True(cut.IsClosed);
        Assert.Equal(4, cut.Segments.Count);
        Assert.All(cut.Segments, s => Assert.IsType<LineTo>(s));
        Assert.Equal(new[] { new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(0, 10), new Vector(0, 0) },
            Points(cut));
        Assert.Empty(layers.Lead);
        Assert.Empty(layers.Etch);
        Assert.False(layers.HasOpenCuts);
        Assert.Single(ProgramGeometry.Fill(Square()));
    }

    [Fact]
    public void Ccw_and_cw_arcs_sweep_from_the_current_point_in_degrees()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(10, 0)));
        // Counterclockwise half circle about the origin to (-10, 0), then clockwise back.
        program.Codes.Add(new ArcMove(new Vector(-10, 0), Vector.Zero, RotationType.CCW));
        program.Codes.Add(new ArcMove(new Vector(10, 0), Vector.Zero, RotationType.CW));

        var figure = Assert.Single(ProgramGeometry.Layers(program).Cut);

        var ccw = Assert.IsType<ArcTo>(figure.Segments[0]);
        Assert.Equal(0, ccw.StartAngle, 9);
        Assert.Equal(180, ccw.SweepAngle, 9);
        Assert.Equal(10, ccw.Radius, 9);
        Near(new Vector(-10, 0), ccw.End);

        var cw = Assert.IsType<ArcTo>(figure.Segments[1]);
        Assert.Equal(180, cw.StartAngle, 9);
        Assert.Equal(-180, cw.SweepAngle, 9);
        Assert.True(figure.IsClosed);
    }

    [Theory]
    [InlineData(RotationType.CCW, 360)]
    [InlineData(RotationType.CW, -360)]
    public void An_arc_ending_where_it_starts_is_a_full_circle(RotationType rotation, double sweep)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(5, 0)));
        program.Codes.Add(new ArcMove(new Vector(5, 0), new Vector(2, 0), rotation));

        var arc = Assert.IsType<ArcTo>(Assert.Single(Assert.Single(ProgramGeometry.Layers(program).Cut).Segments));

        Assert.True(arc.IsFullCircle);
        Assert.Equal(sweep, arc.SweepAngle, 9);
        Assert.Equal(3, arc.Radius, 9);
        Near(new Vector(2, 0), arc.Center);
    }

    [Fact]
    public void Incremental_moves_and_arc_centres_are_relative_to_the_current_point()
    {
        var program = new Program(Mode.Incremental);
        program.Codes.Add(new RapidMove(new Vector(5, 5)));
        program.Codes.Add(Line(10, 0));
        // A quarter circle CCW about a centre 5 above the current point.
        program.Codes.Add(new ArcMove(new Vector(5, 5), new Vector(0, 5), RotationType.CCW));

        var figure = Assert.Single(ProgramGeometry.Layers(program).Cut);

        Near(new Vector(5, 5), figure.Start);
        Near(new Vector(15, 5), figure.Segments[0].End);
        var arc = Assert.IsType<ArcTo>(figure.Segments[1]);
        Near(new Vector(15, 10), arc.Center);
        Near(new Vector(20, 10), arc.End);
        Assert.Equal(270, arc.StartAngle, 9);
        Assert.Equal(90, arc.SweepAngle, 9);
    }

    [Fact]
    public void Leads_etches_and_cuts_are_routed_to_their_own_layers()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(-2, 0)));
        program.Codes.Add(Line(0, 0, LayerType.Leadin));
        program.Codes.Add(Line(10, 0));
        program.Codes.Add(Line(10, 10));
        program.Codes.Add(Line(0, 10));
        program.Codes.Add(Line(0, 0));
        program.Codes.Add(Line(-2, -2, LayerType.Leadout));
        program.Codes.Add(new RapidMove(new Vector(3, 3)));
        program.Codes.Add(Line(7, 7, LayerType.Scribe));

        var layers = ProgramGeometry.Layers(program);

        var cut = Assert.Single(layers.Cut);
        Assert.True(cut.IsClosed);
        Near(new Vector(0, 0), cut.Start);
        Assert.Equal(2, layers.Lead.Count);
        Near(new Vector(-2, 0), layers.Lead[0].Start);
        Near(new Vector(0, 0), layers.Lead[1].Start);
        var etch = Assert.Single(layers.Etch);
        Near(new Vector(3, 3), etch.Start);
        Near(new Vector(7, 7), etch.End);

        // Marks and leads never bound the material.
        var fill = Assert.Single(ProgramGeometry.Fill(program));
        Assert.Equal(Points(cut), Points(fill));
    }

    [Fact]
    public void Suppressed_moves_are_skipped_and_end_the_figure()
    {
        var program = Square();
        ((LinearMove)program.Codes[2]).Suppressed = true;

        var layers = ProgramGeometry.Layers(program);

        Assert.Equal(2, layers.Cut.Count);
        Near(new Vector(10, 0), layers.Cut[0].End);
        Near(new Vector(10, 10), layers.Cut[1].Start);
        Assert.True(layers.HasOpenCuts);
    }

    [Fact]
    public void A_suppressed_lead_is_not_drawn()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(-2, 0)));
        program.Codes.Add(Line(0, 0, LayerType.Leadin, suppressed: true));
        program.Codes.Add(Line(10, 0));

        var layers = ProgramGeometry.Layers(program);

        Assert.Empty(layers.Lead);
        Near(new Vector(0, 0), Assert.Single(layers.Cut).Start);
    }

    [Fact]
    public void Break_cuts_are_drawn_as_cuts_but_never_bound_the_fill()
    {
        var program = Square();
        program.Codes.Add(new RapidMove(new Vector(5, 0)));
        program.Codes.Add(Line(5, 10, LayerType.Break));

        var layers = ProgramGeometry.Layers(program);
        var fill = ProgramGeometry.Fill(program);

        Assert.Equal(2, layers.Cut.Count);
        Assert.True(layers.HasOpenCuts);
        Assert.True(ProgramGeometry.HasBreakCuts(program));
        Assert.False(ProgramGeometry.HasBreakCuts(Square()));
        var outline = Assert.Single(fill);
        Assert.True(outline.IsClosed);
        Assert.DoesNotContain(outline.Segments, s => s.End.X == 5);
    }

    [Fact]
    public void Break_cuts_in_a_sub_program_count()
    {
        var hole = new Program();
        hole.Codes.Add(Line(1, 1, LayerType.Break));
        var program = new Program();
        program.Codes.Add(new SubProgramCall(hole, 0));

        Assert.True(ProgramGeometry.HasBreakCuts(program));
    }

    [Fact]
    public void Tab_spans_are_filled_but_never_drawn_as_cuts()
    {
        var program = Square();
        ((LinearMove)program.Codes[3]).Layer = LayerType.Tab;

        var layers = ProgramGeometry.Layers(program);
        var fill = Assert.Single(ProgramGeometry.Fill(program));

        Assert.Equal(2, layers.Cut.Count);
        Assert.True(layers.HasOpenCuts);
        Assert.True(fill.IsClosed);
        Assert.Equal(4, fill.Segments.Count);
    }

    [Fact]
    public void Pieces_cut_around_tab_gaps_are_bridged_into_one_fill_figure()
    {
        // A square cut in two pieces with gaps at (10,10)-(8,10) and (0,2)-(0,0).
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(Line(10, 0));
        program.Codes.Add(Line(10, 10));
        program.Codes.Add(new RapidMove(new Vector(8, 10)));
        program.Codes.Add(Line(0, 10));
        program.Codes.Add(Line(0, 2));

        var cut = ProgramGeometry.Layers(program).Cut;
        var fill = Assert.Single(ProgramGeometry.Fill(program));

        Assert.Equal(2, cut.Count);
        Assert.Equal(
            new[] { new Vector(0, 0), new Vector(10, 0), new Vector(10, 10), new Vector(8, 10), new Vector(0, 10), new Vector(0, 2) },
            Points(fill));
    }

    [Fact]
    public void Bridging_joins_the_nearest_open_piece_and_leaves_closed_figures_alone()
    {
        var a = new PathFigure(new Vector(0, 0), new PathSegment[] { new LineTo(new Vector(10, 0)) });
        var far = new PathFigure(new Vector(50, 50), new PathSegment[] { new LineTo(new Vector(60, 50)) });
        var near = new PathFigure(new Vector(10, 1), new PathSegment[] { new LineTo(new Vector(0, 1)) });
        var closed = new PathFigure(new Vector(20, 0), new PathSegment[]
        {
            new LineTo(new Vector(21, 0)), new LineTo(new Vector(21, 1)), new LineTo(new Vector(20, 0)),
        });

        var combined = ProgramGeometry.Combine(new[] { a, closed, far, near }, bridgeTabs: true);

        Assert.Equal(3, combined.Count);
        Assert.Equal(new[] { new Vector(0, 0), new Vector(10, 0), new Vector(10, 1), new Vector(0, 1) },
            Points(combined[0]));
        Assert.Same(closed, combined[1]);
        Assert.Equal(Points(far), Points(combined[2]));
        Assert.Equal(4, ProgramGeometry.Combine(new[] { a, closed, far, near }, bridgeTabs: false).Count);
    }

    [Fact]
    public void A_rapid_that_does_not_move_does_not_split_a_figure()
    {
        var program = Square();
        program.Codes.Insert(3, new RapidMove(new Vector(10, 10.0001)));

        Assert.Single(ProgramGeometry.Layers(program).Cut);
    }

    [Fact]
    public void Sub_program_calls_start_at_the_frame_origin_plus_their_offset()
    {
        // Hole programs are incremental: they start from the call's place.
        var hole = new Program(Mode.Incremental);
        hole.Codes.Add(new RapidMove(new Vector(1, 0)));
        hole.Codes.Add(new ArcMove(Vector.Zero, new Vector(-1, 0), RotationType.CW));
        var program = Square();
        program.Codes.Add(new SubProgramCall(hole, 0) { Offset = new Vector(5, 5) });

        var cut = ProgramGeometry.Layers(program).Cut;

        Assert.Equal(2, cut.Count);
        var arc = Assert.IsType<ArcTo>(Assert.Single(cut[1].Segments));
        Near(new Vector(6, 5), cut[1].Start);
        Near(new Vector(5, 5), arc.Center);
    }

    [Fact]
    public void Entity_figures_keep_lines_arcs_circles_and_closed_polygons_but_skip_leads()
    {
        var lead = new Line(new Vector(0, 0), new Vector(1, 1)) { Layer = SpecialLayers.Leadin };
        var polygon = new Polygon();
        polygon.Vertices.Add(new Vector(0, 0));
        polygon.Vertices.Add(new Vector(4, 0));
        polygon.Vertices.Add(new Vector(4, 4));
        var figures = ProgramGeometry.EntityFigures(new Entity[]
        {
            new Line(new Vector(0, 0), new Vector(3, 4)),
            new Circle(new Vector(10, 10), 2),
            new Arc(new Vector(0, 0), 5, 0, System.Math.PI / 2),
            polygon,
            lead,
        });

        Assert.Equal(4, figures.Count);
        Near(new Vector(3, 4), figures[0].End);
        var circle = Assert.IsType<ArcTo>(Assert.Single(figures[1].Segments));
        Assert.True(circle.IsFullCircle);
        Near(new Vector(12, 10), figures[1].Start);
        var arc = Assert.IsType<ArcTo>(Assert.Single(figures[2].Segments));
        Near(new Vector(5, 0), figures[2].Start);
        Near(new Vector(0, 5), arc.End);
        Assert.Equal(90, arc.SweepAngle, 9);
        Assert.True(figures[3].IsClosed);
        Assert.Equal(3, figures[3].Segments.Count);
    }

    [Fact]
    public void Bounds_include_arc_extremes()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(10, 0)));
        program.Codes.Add(new ArcMove(new Vector(-10, 0), Vector.Zero, RotationType.CCW));

        var bounds = ProgramGeometry.Bounds(ProgramGeometry.Layers(program).Cut);

        Assert.Equal(-10, bounds.Left, 9);
        Assert.Equal(10, bounds.Right, 9);
        Assert.Equal(0, bounds.Bottom, 9);
        Assert.Equal(10, bounds.Top, 9);
        Assert.Null(ProgramGeometry.Bounds(Array.Empty<PathFigure>()));
    }
}
