using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    /// <summary>
    /// Pierces found by eroding a cutout: the part of it that keeps a clearance from its wall
    /// (see <see cref="ErodedPierce.Find"/>).
    /// </summary>
    /// <param name="Pierces">Distinct pierces, the one nearest each target in turn.</param>
    /// <param name="Clearance">How far the eroded region keeps every pierce from the wall.</param>
    public sealed record ErodedPierces(IReadOnlyList<Vector> Pierces, double Clearance)
    {
        public static ErodedPierces None { get; } = new(System.Array.Empty<Vector>(), 0);
    }

    /// <summary>
    /// The pierce search for cutouts too small for their configured lead-in (text, slots, small
    /// holes): erode the cutout inward, as a laser kerf offset would, and pierce in what is left.
    /// The erosion goes as deep as the pierce clearance when the cutout has room for it, else
    /// almost to its pole of inaccessibility, so a slot or letter stroke is pierced on its
    /// centre line, where the most clearance is. Each target, an entry the planner ranked, gets
    /// the eroded point nearest it; the lead then runs straight from there to the wall.
    /// </summary>
    public static class ErodedPierce
    {
        /// <summary>
        /// The share of a cutout's inscribed radius the erosion keeps when the full clearance does
        /// not fit: deep enough to stay near the centre line, with room for every place the
        /// cutout is about as wide as its widest.
        /// </summary>
        public const double DepthFraction = 0.9;

        private const int PoleSearchCells = 4096;

        /// <summary>
        /// Up to <paramref name="max"/> distinct pierces in the closed cutout <paramref name="hole"/>,
        /// the eroded point nearest each of <paramref name="targets"/> in turn. A cutout too small to
        /// erode is pierced at its pole alone. Nothing for an open or degenerate contour.
        /// </summary>
        public static ErodedPierces Find(Shape hole, double clearance, IReadOnlyList<Vector> targets, int max)
        {
            if (hole == null || !hole.IsClosed() || targets == null || targets.Count == 0 || max <= 0
                || !double.IsFinite(clearance))
                return ErodedPierces.None;

            // The inscribed radius, from the true wall: the deepest any erosion can go.
            var coarse = ClipperBridge.Flatten(hole, Tolerance(hole.BoundingBox.Width, hole.BoundingBox.Length), false);
            if (coarse.Vertices.Count < 3)
                return ErodedPierces.None;
            var pole = PolyLabel.Find(coarse, null, Tolerance(coarse.BoundingBox.Width, coarse.BoundingBox.Length) / 10,
                PoleSearchCells);
            if (!double.IsFinite(pole.X) || !double.IsFinite(pole.Y) || !coarse.ContainsPoint(pole))
                return ErodedPierces.None;
            var inscribed = hole.ClosestPointTo(pole, out _).DistanceTo(pole);
            if (!(inscribed > 0))
                return ErodedPierces.None;

            // Inscribed flattening can stand off a concave arc, and round joins cut their arcs,
            // each by the tolerance; Clipper rounds to its precision. Erode past both.
            var depth = System.Math.Min(System.Math.Max(clearance, 0), inscribed * DepthFraction);
            var tolerance = System.Math.Max(depth / 100, System.Math.Pow(10, -ClipperBridge.Precision));
            var margin = 2 * tolerance + System.Math.Pow(10, -ClipperBridge.Precision);
            if (depth + margin >= inscribed - tolerance)
                return new(new[] { pole }, inscribed);

            var polygon = ClipperBridge.Flatten(hole, tolerance, circumscribe: false);
            var region = Clipper.InflatePaths(new PathsD { ClipperBridge.ToPath(polygon, positive: true) },
                -(depth + margin), JoinType.Round, EndType.Polygon, 2.0, ClipperBridge.Precision, tolerance)
                .Where(p => p.Count >= 3).ToList();
            if (region.Count == 0)
                return new(new[] { pole }, inscribed);

            var pierces = new List<Vector>();
            foreach (var target in targets)
            {
                var pierce = Nearest(region, target);
                if (pierces.All(p => p.DistanceTo(pierce) > tolerance))
                    pierces.Add(pierce);
                if (pierces.Count == max)
                    break;
            }
            return new(pierces, depth);
        }

        // A hundredth of the smaller side: fine enough to find a thin cutout's pole.
        private static double Tolerance(double width, double length) =>
            System.Math.Max(System.Math.Min(width, length) / 100, System.Math.Pow(10, -ClipperBridge.Precision));

        // The closest point of the region's boundary; targets lie on the wall, outside it.
        private static Vector Nearest(List<PathD> paths, Vector target)
        {
            var best = default(Vector);
            var bestDistance = double.PositiveInfinity;
            foreach (var path in paths)
                for (var i = 0; i < path.Count; i++)
                {
                    var a = new Vector(path[i].x, path[i].y);
                    var b = new Vector(path[(i + 1) % path.Count].x, path[(i + 1) % path.Count].y);
                    var point = new Line(a, b).ClosestPointTo(target);
                    var distance = point.DistanceTo(target);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = point;
                    }
                }
            return best;
        }
    }
}
