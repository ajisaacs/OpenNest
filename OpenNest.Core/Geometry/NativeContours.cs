using System.Collections.Generic;
using System.Linq;

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
        public static NativeContours Of(ShapeProfile profile, Vector location)
        {
            var perimeter = World(profile.Perimeter, location);
            var holes = profile.Cutouts.Select(cutout => World(cutout, location)).ToList();
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
            return other.InMaterial(perimeter.Vertices[0]) || InMaterial(other.perimeter.Vertices[0]);
        }

        /// <summary>Closest boundary approach between two parts, including a part inside the other's
        /// cutout; <paramref name="marker"/> is the middle of that gap. Only edges within
        /// <paramref name="reach"/> of the other part's bounds can be closer than it, so the rest are
        /// skipped. Infinity when no edges are that near.</summary>
        public double GapTo(NativeContours other, double reach, out Vector marker)
        {
            var best = double.PositiveInfinity;
            var middle = Bounds.Center;
            var perimeterA = Near(perimeterLines, other.Bounds, reach);
            var perimeterB = Near(other.perimeterLines, Bounds, reach);
            Measure(perimeterA, perimeterB);
            foreach (var hole in holeLines)
                Measure(Near(hole, other.Bounds, reach), perimeterB);
            foreach (var hole in other.holeLines)
                Measure(perimeterA, Near(hole, Bounds, reach));
            marker = middle;
            return best;

            void Measure(List<Line> ringA, List<Line> ringB)
            {
                if (ringA.Count == 0 || ringB.Count == 0)
                    return;
                var distance = Clearance.ClosestPoints(ringA, ringB, out var onA, out var onB);
                if (distance < best)
                {
                    best = distance;
                    middle = (onA + onB) * 0.5;
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

        private static Polygon World(Shape shape, Vector location)
        {
            var polygon = ClipperBridge.Flatten(shape, ChordTolerance, true);
            polygon.Offset(location);
            polygon.UpdateBounds();
            return polygon;
        }
    }
}
