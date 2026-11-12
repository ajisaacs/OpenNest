using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Desktop.Rendering;

/// <summary>A program's display layers in its own frame, before placement.</summary>
/// <param name="HasOpenCuts">
/// Whether a cut contour is open, such as a cutout left attached by tabs. Filling
/// <paramref name="Cut"/> would close it with a chord; fill <see cref="ProgramGeometry.Fill"/> instead.
/// </param>
public sealed record ProgramLayers(
    IReadOnlyList<PathFigure> Cut,
    IReadOnlyList<PathFigure> Lead,
    IReadOnlyList<PathFigure> Etch,
    bool HasOpenCuts);

/// <summary>
/// A CNC program's moves as toolkit-neutral figures, routed by layer as the WinForms plate view
/// routes them (its <c>GraphicsHelper</c>): cuts, lead-ins/outs, etches and the filled material.
/// Suppressed moves are skipped, a tabbed cutout's tabs are filled but never drawn as cuts, and
/// break cuts are drawn but never bound the fill. Coordinates stay in the program frame: callers
/// place the figures at the part's location.
/// </summary>
public static class ProgramGeometry
{
    /// <summary>Cut strokes: everything but etches, leads and tabs (tabs are never cut).</summary>
    public static bool IsCutLayer(LayerType layer) =>
        layer is not (LayerType.Scribe or LayerType.Leadin or LayerType.Leadout or LayerType.Tab);

    public static bool IsLeadLayer(LayerType layer) => layer is LayerType.Leadin or LayerType.Leadout;

    public static bool IsEtchLayer(LayerType layer) => layer == LayerType.Scribe;

    /// <summary>
    /// Material outline layers: everything but etches, leads and break cuts. Break cuts are open
    /// lines through material: a fill would close each with a chord.
    /// </summary>
    public static bool IsFillLayer(LayerType layer) =>
        layer is not (LayerType.Scribe or LayerType.Leadin or LayerType.Leadout or LayerType.Break);

    /// <summary>The cut, lead and etch figures of <paramref name="program"/>.</summary>
    public static ProgramLayers Layers(Program program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var cut = Figures(program, IsCutLayer, bridgeTabs: false, out var hasOpenCuts);
        var lead = Figures(program, IsLeadLayer, bridgeTabs: false, out _);
        var etch = Figures(program, IsEtchLayer, bridgeTabs: false, out _);
        return new ProgramLayers(cut, lead, etch, hasOpenCuts);
    }

    /// <summary>
    /// The closed material outlines to fill. A contour cut in pieces around tabs is rejoined
    /// across its tab gaps, so it fills as one region rather than as chord-closed pieces.
    /// </summary>
    public static IReadOnlyList<PathFigure> Fill(Program program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return Figures(program, IsFillLayer, bridgeTabs: true, out _);
    }

    /// <summary>Whether <paramref name="program"/> or a sub-program has a break cut.</summary>
    public static bool HasBreakCuts(Program program) =>
        program.Codes.Any(code => code is LinearMove { Layer: LayerType.Break } or ArcMove { Layer: LayerType.Break }
            || code is SubProgramCall { Program: { } sub } && HasBreakCuts(sub));

    /// <summary>
    /// The unsuppressed moves on layers <paramref name="includeLayer"/> accepts, one figure per
    /// continuous run. A run ends at a skipped move, a rapid that moves, or a sub-program call.
    /// With <paramref name="bridgeTabs"/>, open figures are chained end to start (see <see cref="Combine"/>).
    /// </summary>
    public static List<PathFigure> Figures(Program program, Func<LayerType, bool> includeLayer, bool bridgeTabs,
        out bool hasOpenFigures)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(includeLayer);
        var figures = new List<PathFigure>();
        var position = Vector.Zero;
        AddProgram(figures, program, ref position, includeLayer);
        hasOpenFigures = figures.Exists(figure => !figure.IsClosed);
        return Combine(figures, bridgeTabs);
    }

    /// <summary>
    /// With <paramref name="bridgeTabs"/>, each open figure is joined end-to-start with the open
    /// figure starting nearest its end, until its own start is nearer, so the tab-separated pieces
    /// of one contour fill as a single region. Closed figures pass through unchanged.
    /// </summary>
    public static List<PathFigure> Combine(IReadOnlyList<PathFigure> figures, bool bridgeTabs)
    {
        var result = new List<PathFigure>(figures.Count);
        var pending = new List<PathFigure>(figures);

        while (pending.Count > 0)
        {
            var figure = pending[0];
            pending.RemoveAt(0);

            if (!bridgeTabs || figure.IsClosed)
            {
                result.Add(figure);
                continue;
            }

            var segments = new List<PathSegment>(figure.Segments);
            var end = figure.End;

            while (true)
            {
                PathFigure next = null;
                var nextGap = end.DistanceTo(figure.Start);

                foreach (var candidate in pending)
                {
                    if (candidate.IsClosed)
                        continue;
                    var gap = end.DistanceTo(candidate.Start);
                    if (gap < nextGap)
                    {
                        next = candidate;
                        nextGap = gap;
                    }
                }

                if (next == null)
                    break;

                pending.Remove(next);
                // Bridge the tab gap with a line, as GDI+ AddPath(connect: true) does.
                if (nextGap > 0)
                    segments.Add(new LineTo(next.Start));
                segments.AddRange(next.Segments);
                end = next.End;
            }

            result.Add(new PathFigure(figure.Start, segments));
        }

        return result;
    }

    private static void AddProgram(List<PathFigure> figures, Program program, ref Vector position,
        Func<LayerType, bool> includeLayer)
    {
        // Sub-program offsets are relative to the frame origin at entry, not the tool position.
        var frameOrigin = position;
        var mode = program.Mode;
        List<PathSegment> current = null;
        var figureStart = position;

        // Call before moving past a skipped code so the figure records where its cut ended.
        void Flush()
        {
            if (current != null)
            {
                figures.Add(new PathFigure(figureStart, current));
                current = null;
            }
        }

        void Begin(Vector start)
        {
            if (current == null)
            {
                current = new List<PathSegment>();
                figureStart = start;
            }
        }

        for (var i = 0; i < program.Length; ++i)
        {
            switch (program[i])
            {
                case ArcMove arc:
                    {
                        if (!arc.Suppressed && includeLayer(arc.Layer))
                        {
                            Begin(position);
                            current.Add(Arc(arc, mode, position));
                        }
                        else
                            Flush();
                        position = mode == Mode.Incremental ? arc.EndPoint + position : arc.EndPoint;
                        break;
                    }

                case LinearMove line:
                    {
                        var end = mode == Mode.Incremental ? line.EndPoint + position : line.EndPoint;
                        if (!line.Suppressed && includeLayer(line.Layer))
                        {
                            Begin(position);
                            current.Add(new LineTo(end));
                        }
                        else
                            Flush();
                        position = end;
                        break;
                    }

                case RapidMove rapid:
                    {
                        var end = mode == Mode.Incremental ? rapid.EndPoint + position : rapid.EndPoint;
                        var dx = end.X - position.X;
                        var dy = end.Y - position.Y;
                        if (dx * dx + dy * dy > 0.001 * 0.001)
                            Flush();
                        position = end;
                        break;
                    }

                case SubProgramCall call:
                    {
                        Flush();
                        if (call.Program != null)
                        {
                            position = new Vector(frameOrigin.X + call.Offset.X, frameOrigin.Y + call.Offset.Y);
                            AddProgram(figures, call.Program, ref position, includeLayer);
                        }
                        break;
                    }
            }
        }

        Flush();
    }

    /// <summary>An arc move from <paramref name="position"/>, as GraphicsHelper.AddArc draws it.</summary>
    private static ArcTo Arc(ArcMove arc, Mode mode, Vector position)
    {
        var end = arc.EndPoint;
        var center = arc.CenterPoint;
        if (mode == Mode.Incremental)
        {
            end += position;
            center += position;
        }

        var startAngle = Angle.NormalizeDeg(Angle.ToDegrees(System.Math.Atan2(position.Y - center.Y, position.X - center.X)));
        var endAngle = Angle.NormalizeDeg(Angle.ToDegrees(System.Math.Atan2(end.Y - center.Y, end.X - center.X)));
        var radius = center.DistanceTo(end);

        if (startAngle.IsEqualTo(endAngle))
            return new ArcTo(end, center, radius, startAngle, arc.Rotation == RotationType.CW ? -360.0 : 360.0);

        if (arc.Rotation == RotationType.CCW && endAngle < startAngle)
            endAngle += 360.0;
        else if (arc.Rotation == RotationType.CW && startAngle < endAngle)
            startAngle += 360.0;

        return new ArcTo(end, center, radius, startAngle, endAngle - startAngle);
    }

    /// <summary>
    /// Geometry entities (display-only curves) as figures, one per entity, skipping lead-in and
    /// lead-out layers as the WinForms shape path does.
    /// </summary>
    public static List<PathFigure> EntityFigures(IEnumerable<Entity> entities)
    {
        var figures = new List<PathFigure>();
        foreach (var entity in entities)
            AddEntity(figures, entity);
        return figures;
    }

    private static void AddEntity(List<PathFigure> figures, Entity entity)
    {
        if (entity.Layer != null
            && (string.Equals(entity.Layer.Name, SpecialLayers.Leadin.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.Layer.Name, SpecialLayers.Leadout.Name, StringComparison.OrdinalIgnoreCase)))
            return;

        switch (entity)
        {
            case Line line:
                figures.Add(new PathFigure(line.StartPoint, new[] { new LineTo(line.EndPoint) }));
                break;

            case Arc arc:
                {
                    var endAngle = Angle.NormalizeDeg(Angle.ToDegrees(arc.EndAngle));
                    var startAngle = Angle.NormalizeDeg(Angle.ToDegrees(arc.StartAngle));
                    if (arc.Rotation == RotationType.CCW && endAngle < startAngle)
                        endAngle += 360.0;
                    else if (arc.Rotation == RotationType.CW && startAngle < endAngle)
                        startAngle += 360.0;
                    var start = PointOn(arc.Center, arc.Radius, startAngle);
                    var end = PointOn(arc.Center, arc.Radius, endAngle);
                    figures.Add(new PathFigure(start,
                        new[] { new ArcTo(end, arc.Center, arc.Radius, startAngle, endAngle - startAngle) }));
                    break;
                }

            case Circle circle:
                {
                    var start = new Vector(circle.Center.X + circle.Radius, circle.Center.Y);
                    figures.Add(new PathFigure(start,
                        new[] { new ArcTo(start, circle.Center, circle.Radius, 0, 360.0) }));
                    break;
                }

            case Polygon polygon when polygon.Vertices.Count > 0:
                {
                    var vertices = polygon.Vertices;
                    var segments = new List<PathSegment>(vertices.Count);
                    for (var i = 1; i < vertices.Count; i++)
                        segments.Add(new LineTo(vertices[i]));
                    // GDI+ AddPolygon closes the figure.
                    if (vertices[^1].DistanceTo(vertices[0]) > 0)
                        segments.Add(new LineTo(vertices[0]));
                    figures.Add(new PathFigure(vertices[0], segments));
                    break;
                }

            case Shape shape:
                foreach (var child in shape.Entities)
                    AddEntity(figures, child);
                break;
        }
    }

    private static Vector PointOn(Vector center, double radius, double degrees)
    {
        var radians = Angle.ToRadians(degrees);
        return new Vector(center.X + radius * System.Math.Cos(radians), center.Y + radius * System.Math.Sin(radians));
    }

    /// <summary>The bounding box of the figures' points and arc extremes, or null when empty.</summary>
    public static Box Bounds(IEnumerable<PathFigure> figures)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;

        void Add(Vector p)
        {
            any = true;
            minX = System.Math.Min(minX, p.X);
            minY = System.Math.Min(minY, p.Y);
            maxX = System.Math.Max(maxX, p.X);
            maxY = System.Math.Max(maxY, p.Y);
        }

        foreach (var figure in figures)
        {
            Add(figure.Start);
            foreach (var segment in figure.Segments)
            {
                if (segment is ArcTo arc)
                {
                    Add(PointOn(arc.Center, arc.Radius, arc.StartAngle));
                    var from = System.Math.Min(arc.StartAngle, arc.StartAngle + arc.SweepAngle);
                    var to = System.Math.Max(arc.StartAngle, arc.StartAngle + arc.SweepAngle);
                    for (var quadrant = System.Math.Ceiling(from / 90.0) * 90.0; quadrant <= to; quadrant += 90.0)
                        Add(PointOn(arc.Center, arc.Radius, quadrant));
                }
                Add(segment.End);
            }
        }

        return any ? new Box(minX, minY, maxX - minX, maxY - minY) : null;
    }
}
