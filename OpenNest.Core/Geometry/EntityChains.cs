using System.Collections.Generic;
using System.Linq;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Finds the geometry joined to a set of entities end to end, as the CAD Converter's
    /// Select Chain does.
    /// </summary>
    public static class EntityChains
    {
        /// <summary>
        /// Every entity of <paramref name="pool"/> reachable from <paramref name="seeds"/> through
        /// shared endpoints within <paramref name="tolerance"/> (default
        /// <see cref="Tolerance.ChainTolerance"/>, the tolerance shapes are built with), including
        /// the seeds themselves. Branches are followed, so a closed loop with a line running off it
        /// comes back whole. Circles have no endpoints and join nothing. Seeds outside the pool are
        /// returned but not grown from. Results keep the pool's order, seeds outside it last.
        /// </summary>
        public static List<Entity> Connected(IEnumerable<Entity> pool, IEnumerable<Entity> seeds,
            double tolerance = Tolerance.ChainTolerance)
        {
            var candidates = pool.Where(e => e is Line or Arc).Distinct().ToList();
            var cellSize = System.Math.Max(tolerance, Tolerance.Epsilon) * 4;
            var grid = new Dictionary<(long, long), List<Entity>>();
            foreach (var entity in candidates)
            {
                foreach (var end in Ends(entity))
                {
                    var key = Cell(end, cellSize);
                    if (!grid.TryGetValue(key, out var bucket))
                        grid[key] = bucket = new List<Entity>();
                    bucket.Add(entity);
                }
            }

            var found = new HashSet<Entity>();
            var queue = new Queue<Entity>();
            var outside = new List<Entity>();
            foreach (var seed in seeds)
            {
                if (!found.Add(seed))
                    continue;
                if (seed is Line or Arc)
                    queue.Enqueue(seed);
                if (!candidates.Contains(seed))
                    outside.Add(seed);
            }

            while (queue.Count > 0)
            {
                foreach (var end in Ends(queue.Dequeue()))
                {
                    var (cx, cy) = Cell(end, cellSize);
                    for (var dx = -1L; dx <= 1; dx++)
                    {
                        for (var dy = -1L; dy <= 1; dy++)
                        {
                            if (!grid.TryGetValue((cx + dx, cy + dy), out var bucket))
                                continue;
                            foreach (var other in bucket)
                            {
                                if (found.Contains(other) || !Ends(other).Any(p => p.DistanceTo(end) <= tolerance))
                                    continue;
                                found.Add(other);
                                queue.Enqueue(other);
                            }
                        }
                    }
                }
            }

            return candidates.Where(found.Contains).Concat(outside).ToList();
        }

        private static Vector[] Ends(Entity entity) => entity switch
        {
            Line line => new[] { line.StartPoint, line.EndPoint },
            Arc arc => new[] { arc.StartPoint(), arc.EndPoint() },
            _ => System.Array.Empty<Vector>(),
        };

        private static (long, long) Cell(Vector point, double size) =>
            ((long)System.Math.Floor(point.X / size), (long)System.Math.Floor(point.Y / size));
    }
}
