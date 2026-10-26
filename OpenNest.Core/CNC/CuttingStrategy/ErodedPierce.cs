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
    /// <param name="Pierces">The pierce for each target in turn, up to the maximum asked for.</param>
    /// <param name="Clearance">The least any pierce keeps from the wall, at the erosion level that found it.</param>
    public sealed record ErodedPierces(IReadOnlyList<Vector> Pierces, double Clearance)
    {
        public static ErodedPierces None { get; } = new(System.Array.Empty<Vector>(), 0);
    }

    /// <summary>
    /// The pierce search for cutouts too small for their configured lead-in (text, slots, small
    /// holes): erode the cutout inward, as a laser kerf offset would, and pierce in what is left.
    /// The erosion goes as deep as the pierce clearance when the cutout has room for it, else
    /// almost to its pole of inaccessibility. Each target, an entry the planner ranked, gets the
    /// nearest point of the deepest erosion that still reaches the wall beside it, stepping down to
    /// half that depth: a letter stroke is pierced on its own centre line next to the entry, and
    /// only a target with no stroke beside it falls back to the deepest place. The lead then runs
    /// straight from the pierce to the wall.
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

        // Shallower erosion levels a target may fall back to, each a tenth of the deepest less.
        private const int LevelCount = 6;
        private const double LevelStep = 0.1;

        /// <summary>
        /// A pierce in the closed cutout <paramref name="hole"/> for each of the first
        /// <paramref name="max"/> <paramref name="targets"/>: the eroded point nearest it. A cutout
        /// too small to erode is pierced at its pole for every target. Nothing for an open or
        /// degenerate contour.
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
                return new(targets.Take(max).Select(_ => pole).ToArray(), inscribed);

            var polygon = ClipperBridge.Flatten(hole, tolerance, circumscribe: false);
            var path = new PathsD { ClipperBridge.ToPath(polygon, positive: true) };
            var levels = new List<PathD>[LevelCount];
            List<PathD> Level(int level) => levels[level] ??= Clipper.InflatePaths(path,
                -(Depth(level) + margin), JoinType.Round, EndType.Polygon, 2.0, ClipperBridge.Precision, tolerance)
                .Where(p => p.Count >= 3).ToList();
            double Depth(int level) => depth * (1 - level * LevelStep);
            if (Level(0).Count == 0)
                return new(targets.Take(max).Select(_ => pole).ToArray(), inscribed);

            // Each target gets the deepest level that reaches the wall beside it: a stroke
            // narrower than the cutout's widest place is still pierced on its own centre line, near
            // the entry, rather than only at the widest place.
            var pierces = new List<Vector>();
            var shallowest = depth;
            foreach (var target in targets)
            {
                var pierce = Nearest(Level(0), target);
                var reached = 0;
                for (var level = 0; level < LevelCount; level++)
                {
                    var region = Level(level);
                    if (region.Count == 0)
                        continue;
                    var nearest = Nearest(region, target);
                    if (nearest.DistanceTo(target) <= 2 * (Depth(level) + margin))
                    {
                        pierce = nearest;
                        reached = level;
                        break;
                    }
                }
                pierces.Add(pierce);
                shallowest = System.Math.Min(shallowest, Depth(reached));
                if (pierces.Count == max)
                    break;
            }
            return new(pierces, shallowest);
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
