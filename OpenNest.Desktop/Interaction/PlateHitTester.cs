using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenNest.CutOffs;
using OpenNest.Desktop.Rendering;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Desktop.Interaction;

/// <summary>
/// Finds the parts and cut-offs under a point or in a selection window, by the rules of the
/// WinForms plate view: a part is hit inside its filled material (an etch-only part inside its
/// closed mark), even-odd so holes are not part of it; cut-offs are hit on the cut strokes they
/// draw, not their nominal full-length line. Coordinates are plate (world) units.
/// </summary>
/// <remarks>
/// Cut-off segments are parts on the plate, but they are selected as their
/// <see cref="CutOff"/> definitions (<see cref="CutOffAt"/>, <see cref="CutOffsIn"/>), never as
/// parts.
/// </remarks>
public sealed class PlateHitTester
{
    // Degrees per chord when an arc is flattened: well under a pixel of error at working zooms.
    private const double ArcStepDegrees = 3;

    private readonly Func<Part, PartDisplayGeometry> geometryOf;
    private readonly Dictionary<Part, PartDisplayGeometry> ownGeometry = new();
    private readonly ConditionalWeakTable<PartDisplayGeometry, HitShape> shapes = new();

    /// <param name="geometryOf">
    /// The part's current display geometry, when the view already caches it; by default the hit
    /// tester builds and keeps its own.
    /// </param>
    public PlateHitTester(Func<Part, PartDisplayGeometry> geometryOf = null)
    {
        this.geometryOf = geometryOf ?? OwnGeometry;
    }

    /// <summary>The topmost part whose material holds <paramref name="world"/>, or null.</summary>
    public Part PartAt(Plate plate, Vector world)
    {
        if (plate == null)
            return null;
        for (var i = plate.Parts.Count - 1; i >= 0; i--)
        {
            var part = plate.Parts[i];
            if (!IsSelectable(part))
                continue;
            var box = part.BoundingBox;
            if (world.X < box.Left || world.X > box.Right || world.Y < box.Bottom || world.Y > box.Top)
                continue;
            if (ShapeOf(part).Contains(world - part.Location))
                return part;
        }
        return null;
    }

    /// <summary>
    /// The parts <paramref name="window"/> selects, in plate order: in
    /// <see cref="WindowSelectionMode.Contains"/>, those whose material bounds lie inside it; in
    /// <see cref="WindowSelectionMode.Intersect"/>, those whose material it touches.
    /// </summary>
    public IReadOnlyList<Part> PartsIn(Plate plate, SelectionWindow window)
    {
        var result = new List<Part>();
        if (plate == null)
            return result;
        foreach (var part in plate.Parts)
        {
            if (!IsSelectable(part))
                continue;
            var shape = ShapeOf(part);
            if (shape.Bounds == null)
                continue;
            var local = window.RelativeTo(part.Location);
            var hit = window.Mode == WindowSelectionMode.Contains
                ? local.Contains(shape.Bounds)
                : shape.Touches(local);
            if (hit)
                result.Add(part);
        }
        return result;
    }

    /// <summary>
    /// The first cut-off with a cut stroke within <paramref name="tolerance"/> of
    /// <paramref name="world"/>, or null. The WinForms view uses five pixels.
    /// </summary>
    public CutOff CutOffAt(Plate plate, Vector world, double tolerance)
    {
        if (plate?.CutOffs == null)
            return null;
        foreach (var cutOff in plate.CutOffs)
        {
            foreach (var stroke in CutOff.CutStrokes(cutOff.Drawing?.Program))
            {
                if (stroke.ClosestPointTo(world).DistanceTo(world) <= tolerance)
                    return cutOff;
            }
        }
        return null;
    }

    /// <summary>
    /// The cut-offs <paramref name="window"/> selects: in <see cref="WindowSelectionMode.Contains"/>,
    /// those whose every cut stroke lies inside it; in <see cref="WindowSelectionMode.Intersect"/>,
    /// those with a stroke it touches. A cut-off with no strokes is never selected.
    /// </summary>
    public IReadOnlyList<CutOff> CutOffsIn(Plate plate, SelectionWindow window)
    {
        var result = new List<CutOff>();
        if (plate?.CutOffs == null)
            return result;
        foreach (var cutOff in plate.CutOffs)
        {
            var any = false;
            var matches = window.Mode == WindowSelectionMode.Contains;
            foreach (var stroke in CutOff.CutStrokes(cutOff.Drawing?.Program))
            {
                any = true;
                if (window.Mode == WindowSelectionMode.Contains)
                {
                    if (!window.Contains(stroke.StartPoint) || !window.Contains(stroke.EndPoint))
                    {
                        matches = false;
                        break;
                    }
                }
                else if (window.Intersects(stroke.StartPoint, stroke.EndPoint))
                {
                    matches = true;
                    break;
                }
            }
            if (any && matches)
                result.Add(cutOff);
        }
        return result;
    }

    /// <summary>Whether <paramref name="part"/> is selected as a part: everything but cut-off segments.</summary>
    public static bool IsSelectable(Part part) => part?.BaseDrawing is { IsCutOff: false };

    private PartDisplayGeometry OwnGeometry(Part part)
    {
        if (!ownGeometry.TryGetValue(part, out var geometry) || !geometry.IsCurrentFor(part))
            ownGeometry[part] = geometry = PartDisplayGeometry.Build(part);
        return geometry;
    }

    private HitShape ShapeOf(Part part) => shapes.GetValue(geometryOf(part), HitShape.Build);

    /// <summary>Drops the parts no longer on <paramref name="plate"/> from the hit tester's own cache.</summary>
    public void Trim(Plate plate)
    {
        if (ownGeometry.Count == 0)
            return;
        var shown = plate == null ? new HashSet<Part>() : new HashSet<Part>(plate.Parts);
        var stale = new List<Part>();
        foreach (var part in ownGeometry.Keys)
            if (!shown.Contains(part))
                stale.Add(part);
        foreach (var part in stale)
            ownGeometry.Remove(part);
    }

    /// <summary>A part's selection figures flattened to closed rings in its program frame.</summary>
    private sealed class HitShape
    {
        private readonly List<Vector[]> rings;

        private HitShape(List<Vector[]> rings, Box bounds)
        {
            this.rings = rings;
            Bounds = bounds;
        }

        public Box Bounds { get; }

        public static HitShape Build(PartDisplayGeometry geometry)
        {
            var figures = geometry.Selection;
            var rings = new List<Vector[]>();
            foreach (var figure in figures)
            {
                var ring = Flatten(figure);
                if (ring.Count >= 2)
                    rings.Add(ring.ToArray());
            }
            return new HitShape(rings, ProgramGeometry.Bounds(figures));
        }

        private static List<Vector> Flatten(PathFigure figure)
        {
            var points = new List<Vector> { figure.Start };
            foreach (var segment in figure.Segments)
            {
                if (segment is ArcTo arc)
                {
                    var steps = System.Math.Max(2, (int)System.Math.Ceiling(System.Math.Abs(arc.SweepAngle) / ArcStepDegrees));
                    for (var i = 0; i <= steps; i++)
                    {
                        var radians = Angle.ToRadians(arc.StartAngle + arc.SweepAngle * i / steps);
                        points.Add(new Vector(arc.Center.X + arc.Radius * System.Math.Cos(radians),
                            arc.Center.Y + arc.Radius * System.Math.Sin(radians)));
                    }
                }
                else
                {
                    points.Add(segment.End);
                }
            }
            return points;
        }

        /// <summary>Even-odd containment over every ring, each closed back to its start as a fill closes it.</summary>
        public bool Contains(Vector point)
        {
            var inside = false;
            foreach (var ring in rings)
            {
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    var a = ring[i];
                    var b = ring[j];
                    if ((a.Y > point.Y) != (b.Y > point.Y)
                        && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Whether the window touches the filled region: crosses an edge or lies within the fill.</summary>
        public bool Touches(SelectionWindow window)
        {
            if (!window.Overlaps(Bounds))
                return false;
            foreach (var ring in rings)
            {
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    if (window.Intersects(ring[j], ring[i]))
                        return true;
                }
            }
            // No edge enters the window, so it lies wholly inside or wholly outside the fill.
            return Contains(new Vector((window.Left + window.Right) / 2, (window.Bottom + window.Top) / 2));
        }
    }
}
