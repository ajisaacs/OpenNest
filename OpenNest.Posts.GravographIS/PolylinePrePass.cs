using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Posts.GravographIS
{
    /// <summary>
    /// Geometry pre-pass for the Gravograph IS8000 backend. The machine is a dumb
    /// executor — it never reorders geometry and always lifts between separate
    /// entities — so we stitch shared-endpoint polylines together and reorder by
    /// nearest-neighbor before encoding.
    /// </summary>
    public static class PolylinePrePass
    {
        public const double DefaultStitchTolerance = 1e-6;

        /// <summary>
        /// Joins polylines whose endpoints coincide (within <paramref name="tolerance"/>)
        /// into single continuous polylines. Polylines with fewer than two points are
        /// dropped. Direction is reversed as needed to make a join. Each input polyline
        /// is copied — the inputs are not mutated.
        /// </summary>
        public static List<List<Vector>> Stitch(
            IEnumerable<IReadOnlyList<Vector>> polylines,
            double tolerance = DefaultStitchTolerance)
        {
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));

            var segs = new List<List<Vector>>();
            foreach (var p in polylines)
            {
                if (p == null || p.Count < 2)
                    continue;
                segs.Add(new List<Vector>(p));
            }

            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < segs.Count; i++)
                {
                    var a = segs[i];

                    for (int j = 0; j < segs.Count; j++)
                    {
                        if (i == j) continue;
                        var b = segs[j];

                        // a-end ↔ b-start: append b to a (skip duplicated joint)
                        if (Near(a[a.Count - 1], b[0], tolerance))
                        {
                            for (int k = 1; k < b.Count; k++) a.Add(b[k]);
                            segs.RemoveAt(j);
                            if (j < i) i--;
                            changed = true;
                            break;
                        }

                        // a-end ↔ b-end: append reversed b to a
                        if (Near(a[a.Count - 1], b[b.Count - 1], tolerance))
                        {
                            for (int k = b.Count - 2; k >= 0; k--) a.Add(b[k]);
                            segs.RemoveAt(j);
                            if (j < i) i--;
                            changed = true;
                            break;
                        }

                        // a-start ↔ b-end: prepend b to a
                        if (Near(a[0], b[b.Count - 1], tolerance))
                        {
                            var combined = new List<Vector>(b.Count + a.Count - 1);
                            combined.AddRange(b);
                            for (int k = 1; k < a.Count; k++) combined.Add(a[k]);
                            segs[i] = combined;
                            segs.RemoveAt(j);
                            if (j < i) i--;
                            changed = true;
                            break;
                        }

                        // a-start ↔ b-start: prepend reversed b to a
                        if (Near(a[0], b[0], tolerance))
                        {
                            var combined = new List<Vector>(b.Count + a.Count - 1);
                            for (int k = b.Count - 1; k >= 0; k--) combined.Add(b[k]);
                            for (int k = 1; k < a.Count; k++) combined.Add(a[k]);
                            segs[i] = combined;
                            segs.RemoveAt(j);
                            if (j < i) i--;
                            changed = true;
                            break;
                        }
                    }

                    if (changed) break;
                }
            }
            while (changed);

            return segs;
        }

        /// <summary>
        /// Greedy nearest-neighbor ordering of polylines starting from
        /// <paramref name="origin"/> (defaults to 0,0 = the work origin = the first
        /// polyline's first point on the wire). When <paramref name="allowReverse"/>
        /// is true a polyline may be reversed if its tail is closer than its head.
        /// </summary>
        public static List<List<Vector>> Reorder(
            IEnumerable<IReadOnlyList<Vector>> polylines,
            bool allowReverse = true,
            Vector? origin = null)
        {
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));

            var pool = new List<List<Vector>>();
            foreach (var p in polylines)
            {
                if (p == null || p.Count < 2)
                    continue;
                pool.Add(new List<Vector>(p));
            }

            var ordered = new List<List<Vector>>(pool.Count);
            var current = origin ?? new Vector(0, 0);

            while (pool.Count > 0)
            {
                var bestIdx = -1;
                var bestReverse = false;
                var bestDistSq = double.PositiveInfinity;

                for (int i = 0; i < pool.Count; i++)
                {
                    var p = pool[i];
                    var dHead = SquaredDistance(current, p[0]);
                    if (dHead < bestDistSq)
                    {
                        bestDistSq = dHead;
                        bestIdx = i;
                        bestReverse = false;
                    }

                    if (allowReverse)
                    {
                        var dTail = SquaredDistance(current, p[p.Count - 1]);
                        if (dTail < bestDistSq)
                        {
                            bestDistSq = dTail;
                            bestIdx = i;
                            bestReverse = true;
                        }
                    }
                }

                var pick = pool[bestIdx];
                pool.RemoveAt(bestIdx);
                if (bestReverse)
                    pick.Reverse();
                ordered.Add(pick);
                current = pick[pick.Count - 1];
            }

            return ordered;
        }

        /// <summary>
        /// Convenience: stitch then reorder.
        /// </summary>
        public static List<List<Vector>> Prepare(
            IEnumerable<IReadOnlyList<Vector>> polylines,
            double stitchTolerance = DefaultStitchTolerance,
            bool allowReverse = true,
            Vector? origin = null)
        {
            var stitched = Stitch(polylines, stitchTolerance);
            return Reorder(stitched, allowReverse, origin);
        }

        private static bool Near(Vector a, Vector b, double tol)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (dx * dx + dy * dy) <= tol * tol;
        }

        private static double SquaredDistance(Vector a, Vector b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
