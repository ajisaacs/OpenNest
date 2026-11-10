using System.Collections.Generic;
using System.Linq;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// A part's native contours in world space, flattened at <see cref="ChordTolerance"/>, to
    /// confirm a part-spacing candidate that <see cref="SpacingOutline"/> found. The outline
    /// circumscribes arcs by up to its chord tolerance, more than <see cref="SpacingOutline.SpacingSlack"/>,
    /// so two convex arcs facing each other exactly at the spacing can overlap there; these
    /// contours measure that gap to within a tenth of the slack. Every spacing verdict (Auto
    /// Nest's layout check, the whole-job runner's candidate validation and the pre-post review)
    /// confirms with them, so none can disagree.
    /// </summary>
    public sealed class NativeContours
    {
        /// <summary>Chord tolerance of the confirming measurement. Circumscribed on both sides, it
        /// understates a gap by at most twice this, a tenth of <see cref="SpacingOutline.SpacingSlack"/>.</summary>
        public const double ChordTolerance = SpacingOutline.SpacingSlack / 20;

        private Polygon perimeter;
        private List<Polygon> holes;
        private List<Line> perimeterLines;
        private List<List<Line>> holeLines;

        private NativeContours() { }

        public Box Bounds { get; private set; }

        /// <summary>Flattens <paramref name="profile"/> (already rotated) at <paramref name="location"/>.</summary>
        public static NativeContours Of(ShapeProfile profile, Vector location) =>
            Of(profile, location, ChordTolerance);

        /// <summary>As <see cref="Of(ShapeProfile, Vector)"/> at a coarser
        /// <paramref name="chordTolerance"/>, for searches that confirm their answer at the fine one.</summary>
        public static NativeContours Of(ShapeProfile profile, Vector location, double chordTolerance)
        {
            var perimeter = World(profile.Perimeter, location, chordTolerance);
            var holes = profile.Cutouts.Select(cutout => World(cutout, location, chordTolerance)).ToList();
            return new NativeContours
            {
                perimeter = perimeter,
                holes = holes,
                perimeterLines = perimeter.ToLines(),
                holeLines = holes.Select(hole => hole.ToLines()).ToList(),
                Bounds = perimeter.BoundingBox,
            };
        }

        /// <summary>
        /// True when the two parts are closer than <paramref name="spacing"/> less
        /// <see cref="SpacingOutline.SpacingSlack"/>, or share material: a boundary within that
        /// gap, or one part wholly inside the other's material rather than in a cutout.
        /// </summary>
        public bool TooClose(NativeContours other, double spacing)
        {
            if (GapTo(other, spacing, out _) < spacing - SpacingOutline.SpacingSlack)
                return true;
            // No boundaries meet, so either part is wholly inside or outside the other's material.
            return Nested(other);
        }

        /// <summary>True when a vertex of either part lies in the other's material rather than in
        /// a cutout or outside: with no boundary crossing, one part is inside the other.</summary>
        public bool Nested(NativeContours other) =>
            other.InMaterial(perimeter.Vertices[0]) || InMaterial(other.perimeter.Vertices[0]);

        /// <summary>These contours moved by <paramref name="offset"/>, reusing the flattening.</summary>
        public NativeContours Translated(Vector offset) => Transformed(0, offset);

        /// <summary>These contours turned by <paramref name="angle"/> (radians) about the origin,
        /// then moved by <paramref name="offset"/>, reusing the flattening: a rigid turn keeps
        /// every chord within its tolerance.</summary>
        public NativeContours Transformed(double angle, Vector offset)
        {
            var moved = Moved(perimeter, angle, offset);
            var movedHoles = holes.Select(hole => Moved(hole, angle, offset)).ToList();
            return new NativeContours
            {
                perimeter = moved,
                holes = movedHoles,
                perimeterLines = moved.ToLines(),
                holeLines = movedHoles.Select(hole => hole.ToLines()).ToList(),
                Bounds = moved.BoundingBox,
            };
        }

        private static Polygon Moved(Polygon polygon, double angle, Vector offset)
        {
            var moved = new Polygon();
            if (angle == 0)
                moved.Vertices.AddRange(polygon.Vertices.Select(vertex => vertex + offset));
            else
            {
                var cos = PortableMath.Cos(angle);
                var sin = PortableMath.Sin(angle);
                moved.Vertices.AddRange(polygon.Vertices.Select(vertex =>
                    new Vector(vertex.X * cos - vertex.Y * sin + offset.X, vertex.X * sin + vertex.Y * cos + offset.Y)));
            }
            moved.UpdateBounds();
            return moved;
        }

        /// <summary>Closest boundary approach between two parts, including a part inside the other's
        /// cutout; <paramref name="marker"/> is the middle of that gap. Only edges within
        /// <paramref name="reach"/> of the other part's bounds can be closer than it, so the rest are
        /// skipped. Infinity when no edges are that near.</summary>
        public double GapTo(NativeContours other, double reach, out Vector marker)
        {
            var gap = GapTo(other, reach, out var onThis, out var onOther);
            marker = double.IsPositiveInfinity(gap) ? Bounds.Center : (onThis + onOther) * 0.5;
            return gap;
        }

        /// <summary>As <see cref="GapTo(NativeContours, double, out Vector)"/>, returning the closest
        /// points themselves: the gap runs from <paramref name="onOther"/> to <paramref name="onThis"/>.</summary>
        public double GapTo(NativeContours other, double reach, out Vector onThis, out Vector onOther)
        {
            var best = double.PositiveInfinity;
            var bestThis = Bounds.Center;
            var bestOther = Bounds.Center;
            var perimeterA = Near(perimeterLines, other.Bounds, reach);
            var perimeterB = Near(other.perimeterLines, Bounds, reach);
            Measure(perimeterA, perimeterB);
            foreach (var hole in holeLines)
                Measure(Near(hole, other.Bounds, reach), perimeterB);
            foreach (var hole in other.holeLines)
                Measure(perimeterA, Near(hole, Bounds, reach));
            onThis = bestThis;
            onOther = bestOther;
            return best;

            void Measure(List<Line> ringA, List<Line> ringB)
            {
                if (ringA.Count == 0 || ringB.Count == 0)
                    return;
                var distance = Clearance.ClosestPoints(ringA, ringB, out var onA, out var onB);
                if (distance < best)
                {
                    best = distance;
                    bestThis = onA;
                    bestOther = onB;
                }
            }
        }

        private bool InMaterial(Vector point) =>
            perimeter.ContainsPoint(point) && !holes.Any(hole => hole.ContainsPoint(point));

        private static List<Line> Near(List<Line> lines, Box other, double reach) =>
            lines.Where(line =>
                System.Math.Min(line.StartPoint.X, line.EndPoint.X) <= other.Right + reach
                && System.Math.Max(line.StartPoint.X, line.EndPoint.X) >= other.Left - reach
                && System.Math.Min(line.StartPoint.Y, line.EndPoint.Y) <= other.Top + reach
                && System.Math.Max(line.StartPoint.Y, line.EndPoint.Y) >= other.Bottom - reach).ToList();

        private static Polygon World(Shape shape, Vector location, double chordTolerance)
        {
            var polygon = ClipperBridge.Flatten(shape, chordTolerance, true);
            polygon.Offset(location);
            polygon.UpdateBounds();
            return polygon;
        }
    }
}
