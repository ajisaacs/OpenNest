#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.CommonLines;

/// <summary>
/// Two copies of one part sharing their longest straight edge one kerf apart: the second copy
/// is the first turned half a turn, so that edge faces its own copy. Engines place the pair as one
/// compound part (<see cref="Geometry"/>), and the pipeline expands it back into the two copies
/// and their common-line group.
/// </summary>
internal sealed class CommonLinePair
{
    /// <summary>Chord tolerance of the compound outline; arcs are circumscribed, so it only
    /// controls vertex count, never makes the compound smaller than its copies.</summary>
    private const double FlattenTolerance = 0.002;

    /// <summary>How far the bridge over the kerf reaches into each copy's material, so the union
    /// joins the copies into one outline.</summary>
    private const double BridgeOverlap = 1e-4;

    private CommonLinePair(PartGeometrySnapshot geometry, double secondX, double secondY, double sharedLength)
    {
        Geometry = geometry;
        SecondX = secondX;
        SecondY = secondY;
        SharedLength = sharedLength;
    }

    /// <summary>The compound: both copies and the kerf between them as one solid outline, in the
    /// first copy's snapshot frame. Holes are not offered (they read as solid).</summary>
    public PartGeometrySnapshot Geometry { get; }

    /// <summary>Where the second copy sits in the compound's frame; it is turned half a turn.</summary>
    public double SecondX { get; }

    public double SecondY { get; }

    /// <summary>Length of the shared span (the cut saved once per pair when cut once).</summary>
    public double SharedLength { get; }

    /// <summary>The second copy's rotation relative to the compound.</summary>
    public const double SecondRotation = System.Math.PI;

    /// <summary>
    /// The pair for <paramref name="geometry"/> at <paramref name="kerf"/>, or null when the part
    /// has no straight edge of at least <see cref="CommonLineGeometry.MinSharedLength"/>, or when
    /// the half-turned copy would come closer than the kerf anywhere else.
    /// </summary>
    public static CommonLinePair? Build(PartGeometrySnapshot geometry, double kerf)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!double.IsFinite(kerf) || kerf <= 0)
            return null;
        Drawing drawing;
        Part first;
        IReadOnlyList<CommonLineGeometry.Edge> edges;
        try
        {
            drawing = new Drawing("pair", DrawingJobMapper.ToProgram(geometry));
            first = new Part(drawing);
            edges = CommonLineGeometry.Edges(first, new CommonLineGeometry.ProfileCache());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or NotSupportedException or ArithmeticException)
        {
            return null;
        }
        // Longest edges first; equal lengths keep perimeter order, so the choice is deterministic.
        foreach (var edge in edges.OrderByDescending(e => System.Math.Round(e.Length, 9)).Take(4))
        {
            var pair = TryEdge(drawing, first, edge, kerf);
            if (pair != null)
                return pair;
        }
        return null;
    }

    private static CommonLinePair? TryEdge(Drawing drawing, Part first, CommonLineGeometry.Edge edge, double kerf)
    {
        // Turning a copy half a turn maps this edge to itself reversed, outward normal opposed;
        // moving it to the edge's mirror across the kerf centerline makes the two face each other
        // over their whole length.
        var middle = new Vector((edge.Start.X + edge.End.X) / 2, (edge.Start.Y + edge.End.Y) / 2);
        var offset = new Vector(2 * middle.X + edge.Outward.X * kerf, 2 * middle.Y + edge.Outward.Y * kerf);
        var second = new Part(drawing);
        second.Rotate(SecondRotation);
        second.Location = offset;
        second.UpdateBounds();

        var spans = CommonLineGeometry.Spans(first, second, kerf);
        if (spans.Count == 0)
            return null;
        var firstProfile = Profile(first);
        var secondProfile = Profile(second);
        if (firstProfile == null || secondProfile == null)
            return null;
        // Away from the shared edge the copies must still clear one kerf (a concave part's
        // half-turned copy can reach back into it).
        if (NativeContours.Of(firstProfile, first.Location).TooClose(NativeContours.Of(secondProfile, second.Location), kerf))
            return null;

        var outline = Outline(firstProfile, first.Location, secondProfile, second.Location, edge, kerf);
        if (outline == null)
            return null;
        var geometry = Snapshot(outline);
        // Engines accept only usable closed edges; a compound they would refuse is no pair.
        try
        {
            _ = JobPartGeometry.Read(geometry);
        }
        catch (ArgumentException)
        {
            return null;
        }
        return new CommonLinePair(geometry, offset.X, offset.Y, spans.Sum(s => s.Length));
    }

    private static ShapeProfile? Profile(Part part)
    {
        var entities = ConvertProgram.ToGeometry(part.Program).ForNesting().ToList();
        if (entities.Count == 0)
            return null;
        var profile = new ShapeProfile(entities);
        return profile.Perimeter == null ? null : profile;
    }

    /// <summary>The union of both perimeters (arcs circumscribed) and the kerf between them, or
    /// null when it is not one outline.</summary>
    private static PathD? Outline(ShapeProfile first, Vector firstAt, ShapeProfile second, Vector secondAt,
        CommonLineGeometry.Edge edge, double kerf)
    {
        var a = ClipperBridge.ToPath(ClipperBridge.Flatten(first.Perimeter, FlattenTolerance, circumscribe: true), firstAt);
        var b = ClipperBridge.ToPath(ClipperBridge.Flatten(second.Perimeter, FlattenTolerance, circumscribe: true), secondAt);
        var n = edge.Outward;
        double inner = -BridgeOverlap, outer = kerf + BridgeOverlap;
        var bridge = new PathD
        {
            new(edge.Start.X + n.X * inner, edge.Start.Y + n.Y * inner),
            new(edge.End.X + n.X * inner, edge.End.Y + n.Y * inner),
            new(edge.End.X + n.X * outer, edge.End.Y + n.Y * outer),
            new(edge.Start.X + n.X * outer, edge.Start.Y + n.Y * outer),
        };
        // Clipper rounds PathsD to its precision; 8 decimals keeps the outline within 5e-9 of the copies.
        // Every piece counter-clockwise, whatever the program's winding, so overlaps add up.
        var pieces = new PathsD { a, b, bridge };
        foreach (var piece in pieces)
            if (!Clipper.IsPositive(piece))
                piece.Reverse();
        var union = Clipper.Union(pieces, new PathsD(), FillRule.NonZero, 8);
        var outers = union.Where(path => Clipper.Area(path) > 0).ToList();
        return outers.Count == 1 ? outers[0] : null;
    }

    private static PartGeometrySnapshot Snapshot(PathD outline)
    {
        // Clipper can return (nearly) repeated vertices; a zero-length edge is not usable geometry.
        var points = new PathD(outline.Count);
        foreach (var point in outline)
            if (points.Count == 0 || !Same(points[^1], point))
                points.Add(point);
        while (points.Count > 1 && Same(points[^1], points[0]))
            points.RemoveAt(points.Count - 1);
        outline = points;
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(outline[0].x, outline[0].y));
        for (var i = 1; i <= outline.Count; i++)
        {
            var point = outline[i % outline.Count];
            program.Codes.Add(new LinearMove(point.x, point.y) { Layer = LayerType.Cut });
        }
        return PartGeometrySnapshot.FromProgram(program);
    }

    // Engines refuse edges shorter than 1e-7; dropping a vertex this close moves the outline by
    // far less than the spacing slack.
    private static bool Same(PointD a, PointD b) =>
        System.Math.Abs(a.x - b.x) <= 1e-6 && System.Math.Abs(a.y - b.y) <= 1e-6;
}
