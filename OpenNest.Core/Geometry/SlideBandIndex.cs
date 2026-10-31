#nullable enable
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Buckets entity indices by their perpendicular extent for one axis-aligned slide, so a
    /// band query returns, in ascending entity order, a superset of the entities the
    /// <see cref="SlideBand"/> tests accept. Callers still apply those exact tests; the index
    /// only avoids visiting entities that cannot pass them.
    /// </summary>
    /// <remarks>
    /// Each extent is the exact acceptance bound of the matching <see cref="SlideBand"/> test,
    /// widened by a reach that covers the quadratic slack of ray-circle and curve-curve tests
    /// (which grows with the along-axis distance) plus rounding. Bucket numbers are a
    /// monotone function of the coordinate, so a value inside an extent always lands in one of
    /// the extent's buckets.
    /// </remarks>
    internal sealed class SlideBandIndex
    {
        /// <summary>Entities of one kind an index holds.</summary>
        internal enum Kind
        {
            All,
            Lines,
            Curves,
        }

        // Below this many entities a plain scan is as cheap as building buckets.
        internal const int MinimumEntities = 24;

        private const int MaximumBuckets = 512;

        // Merging more sorted bucket lists than this by repeated minimum is slower than a sort.
        private const int MaximumMergedBuckets = 16;

        private readonly double min;
        private readonly double scale;
        private readonly int bucketCount;
        private readonly int[] starts;
        private readonly int[] items;

        private SlideBandIndex(double min, double scale, int bucketCount, int[] starts, int[] items)
        {
            this.min = min;
            this.scale = scale;
            this.bucketCount = bucketCount;
            this.starts = starts;
            this.items = items;
        }

        /// <summary>
        /// The widening every extent needs on top of its own exact bound: twice √ε times the
        /// largest along-axis separation (the quadratic slack), plus a relative rounding term.
        /// NaN when a coordinate is nonfinite or beyond <see cref="SlideBand.CoordinateLimit"/>,
        /// where the band tests keep every pair and an index could not stand in for them.
        /// </summary>
        public static double Reach(double maxAbsAlong, double maxAbsPerpendicular)
        {
            if (
                !(maxAbsAlong <= SlideBand.CoordinateLimit)
                || !(maxAbsPerpendicular <= SlideBand.CoordinateLimit)
            )
                return double.NaN;
            var separation = 2 * maxAbsAlong;
            // The second term is headroom for rounding of the extents and the curve tests'
            // 2ε·|r|; no test case needs it today, but it costs nothing measurable.
            return 2 * System.Math.Sqrt(SlideBand.QuadraticSlack) * (1 + separation)
                + 1e-12 * (1 + separation + maxAbsPerpendicular);
        }

        /// <summary>
        /// A curve's half-width before widening. The quadratic tests accept
        /// |oy| ≤ |r|·(1 + 2ε) + 2√ε·(1 + |ox|) with ε = QuadraticSlack; the reach holds both the
        /// second term and the first one's 2ε·|r|, since its rounding term is 1e-12 times a bound
        /// that includes every radius.
        /// </summary>
        public static double CurveReach(double r) => System.Math.Abs(r);

        /// <summary>
        /// Indexes the entities of <paramref name="kind"/> in <paramref name="entities"/>
        /// translated by (dx, dy), or returns null when the list is small or the geometry or
        /// reach is not usable.
        /// </summary>
        public static SlideBandIndex? Build(
            SlideBand.Axis axis,
            List<Entity> entities,
            double dx,
            double dy,
            double reach,
            Kind kind
        )
        {
            var count = entities.Count;
            if (count < MinimumEntities || !double.IsFinite(reach) || reach < 0)
                return null;

            var lo = new double[count];
            var hi = new double[count];
            var indexed = new bool[count];
            var members = 0;
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            for (var i = 0; i < count; i++)
            {
                if (!TryExtent(axis, entities[i], dx, dy, kind, out var a, out var b))
                    continue;
                a -= reach;
                b += reach;
                if (!double.IsFinite(a) || !double.IsFinite(b))
                    return null;
                lo[i] = a;
                hi[i] = b;
                indexed[i] = true;
                members++;
                if (a < min)
                    min = a;
                if (b > max)
                    max = b;
            }

            if (members == 0)
                return new SlideBandIndex(0, 0, 1, new int[2], System.Array.Empty<int>());

            var buckets = System.Math.Min(MaximumBuckets, members);
            var span = max - min;
            var scale = span > 0 ? buckets / span : 0;
            if (!double.IsFinite(scale) || scale <= 0)
            {
                scale = 0;
                buckets = 1;
            }

            var shape = new SlideBandIndex(
                min,
                scale,
                buckets,
                System.Array.Empty<int>(),
                System.Array.Empty<int>()
            );
            var next = new int[buckets + 1];
            for (var i = 0; i < count; i++)
            {
                if (!indexed[i])
                    continue;
                var last = shape.Bucket(hi[i]);
                for (var b = shape.Bucket(lo[i]); b <= last; b++)
                    next[b + 1]++;
            }

            for (var b = 0; b < buckets; b++)
                next[b + 1] += next[b];
            var starts = (int[])next.Clone();
            var items = new int[next[buckets]];
            for (var i = 0; i < count; i++)
            {
                if (!indexed[i])
                    continue;
                var last = shape.Bucket(hi[i]);
                for (var b = shape.Bucket(lo[i]); b <= last; b++)
                    items[next[b]++] = i;
            }

            return new SlideBandIndex(min, scale, buckets, starts, items);
        }

        /// <summary>
        /// Entities whose extent may contain <paramref name="value"/>, as the item positions
        /// [start, end), ascending. Empty when the value lies below every extent; false for a
        /// nonfinite value (scan everything).
        /// </summary>
        public bool TryPoint(double value, out int start, out int end)
        {
            start = end = 0;
            if (!double.IsFinite(value))
                return false;
            if (value < min)
                return true;
            var b = Bucket(value);
            start = starts[b];
            end = starts[b + 1];
            return true;
        }

        /// <summary>
        /// Entities whose extent may overlap [<paramref name="from"/>, <paramref name="to"/>],
        /// ascending and without repeats, written to <paramref name="result"/>. False for a
        /// nonfinite interval (scan everything).
        /// </summary>
        public bool TryRange(double from, double to, List<int> result)
        {
            result.Clear();
            if (!double.IsFinite(from) || !double.IsFinite(to))
                return false;
            if (to < min)
                return true;

            var first = Bucket(from);
            var last = Bucket(to);
            if (first == last)
            {
                for (var k = starts[first]; k < starts[first + 1]; k++)
                    result.Add(items[k]);
                return true;
            }

            var lists = last - first + 1;
            if (lists > MaximumMergedBuckets)
            {
                for (var k = starts[first]; k < starts[last + 1]; k++)
                    result.Add(items[k]);
                result.Sort();
                Deduplicate(result);
                return true;
            }

            // Each bucket list is ascending: merge them by repeated minimum, dropping repeats.
            System.Span<int> heads = stackalloc int[MaximumMergedBuckets];
            for (var b = 0; b < lists; b++)
                heads[b] = starts[first + b];
            var previous = -1;
            while (true)
            {
                var best = int.MaxValue;
                var source = -1;
                for (var b = 0; b < lists; b++)
                {
                    var head = heads[b];
                    if (head < starts[first + b + 1] && items[head] < best)
                    {
                        best = items[head];
                        source = b;
                    }
                }
                if (source < 0)
                    return true;
                heads[source]++;
                if (best != previous)
                    result.Add(best);
                previous = best;
            }
        }

        public int Item(int position) => items[position];

        private static void Deduplicate(List<int> sorted)
        {
            var write = 0;
            for (var k = 0; k < sorted.Count; k++)
            {
                if (write > 0 && sorted[write - 1] == sorted[k])
                    continue;
                sorted[write++] = sorted[k];
            }
            sorted.RemoveRange(write, sorted.Count - write);
        }

        /// <summary>Monotone in <paramref name="value"/>, clamped to the bucket range.</summary>
        private int Bucket(double value)
        {
            if (scale == 0)
                return 0;
            var b = (value - min) * scale;
            if (!(b >= 0))
                return 0;
            if (b >= bucketCount)
                return bucketCount - 1;
            return (int)b;
        }

        /// <summary>
        /// The exact acceptance bound of the entity's <see cref="SlideBand"/> tests before
        /// widening: a line's span with its RayLine slack, or a curve's circle with its radius
        /// slack. False for entity types no slide kernel emits for, or not of
        /// <paramref name="kind"/>.
        /// </summary>
        private static bool TryExtent(
            SlideBand.Axis axis,
            Entity entity,
            double dx,
            double dy,
            Kind kind,
            out double lo,
            out double hi
        )
        {
            switch (entity)
            {
                case Line line when kind != Kind.Curves:
                    {
                        var a = SlideBand.Perp(axis, line.pt1.X + dx, line.pt1.Y + dy);
                        var b = SlideBand.Perp(axis, line.pt2.X + dx, line.pt2.Y + dy);
                        var low = System.Math.Min(a, b);
                        var high = System.Math.Max(a, b);
                        var margin = SlideBand.SpanSlack * (high - low);
                        lo = low - margin;
                        hi = high + margin;
                        return true;
                    }
                case Arc arc when kind != Kind.Lines:
                    return CurveExtent(
                        axis,
                        arc.Center.X + dx,
                        arc.Center.Y + dy,
                        arc.Radius,
                        out lo,
                        out hi
                    );
                case Circle circle when kind != Kind.Lines:
                    return CurveExtent(
                        axis,
                        circle.Center.X + dx,
                        circle.Center.Y + dy,
                        circle.Radius,
                        out lo,
                        out hi
                    );
                default:
                    lo = hi = 0;
                    return false;
            }
        }

        private static bool CurveExtent(
            SlideBand.Axis axis,
            double cx,
            double cy,
            double r,
            out double lo,
            out double hi
        )
        {
            var c = SlideBand.Perp(axis, cx, cy);
            var radius = CurveReach(r);
            lo = c - radius;
            hi = c + radius;
            return true;
        }

        /// <summary>
        /// Raises the largest |along| and |perpendicular| coordinate (perpendicular plus radius
        /// for curves) of translated vertices, line ends and curve centres; used to size
        /// <see cref="Reach"/>. A NaN propagates. Returns the number of curves seen.
        /// </summary>
        public static int Measure(
            SlideBand.Axis axis,
            List<Entity> entities,
            Vector[] vertices,
            double dx,
            double dy,
            ref double maxAlong,
            ref double maxPerpendicular
        )
        {
            var curves = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                Accumulate(axis, v.X + dx, v.Y + dy, 0, ref maxAlong, ref maxPerpendicular);
            }
            for (var i = 0; i < entities.Count; i++)
            {
                switch (entities[i])
                {
                    case Line line:
                        Accumulate(
                            axis,
                            line.pt1.X + dx,
                            line.pt1.Y + dy,
                            0,
                            ref maxAlong,
                            ref maxPerpendicular
                        );
                        Accumulate(
                            axis,
                            line.pt2.X + dx,
                            line.pt2.Y + dy,
                            0,
                            ref maxAlong,
                            ref maxPerpendicular
                        );
                        break;
                    case Arc arc:
                        curves++;
                        Accumulate(
                            axis,
                            arc.Center.X + dx,
                            arc.Center.Y + dy,
                            arc.Radius,
                            ref maxAlong,
                            ref maxPerpendicular
                        );
                        break;
                    case Circle circle:
                        curves++;
                        Accumulate(
                            axis,
                            circle.Center.X + dx,
                            circle.Center.Y + dy,
                            circle.Radius,
                            ref maxAlong,
                            ref maxPerpendicular
                        );
                        break;
                }
            }
            return curves;
        }

        private static void Accumulate(
            SlideBand.Axis axis,
            double x,
            double y,
            double r,
            ref double maxAlong,
            ref double maxPerpendicular
        )
        {
            var along = System.Math.Abs(SlideBand.Para(axis, x, y));
            var perpendicular = System.Math.Abs(SlideBand.Perp(axis, x, y)) + System.Math.Abs(r);
            // Written so a NaN propagates instead of being ignored by Math.Max.
            maxAlong = along > maxAlong || double.IsNaN(along) ? along : maxAlong;
            maxPerpendicular = perpendicular > maxPerpendicular || double.IsNaN(perpendicular)
                ? perpendicular
                : maxPerpendicular;
        }
    }

    /// <summary>
    /// The band indexes one <see cref="EntitySlideEvents"/> enumeration uses, built only for
    /// the phases that have work: both sides for vertex rays, the line-only side an arc of the
    /// other side queries, and the stationary curves the moving curves query.
    /// </summary>
    internal sealed class SlideBandIndexes
    {
        public SlideBandIndex? Stationary { get; private init; }

        public SlideBandIndex? Moving { get; private init; }

        public SlideBandIndex? StationaryLines { get; private init; }

        public SlideBandIndex? MovingLines { get; private init; }

        public SlideBandIndex? StationaryCurves { get; private init; }

        /// <summary>Scratch list for range queries.</summary>
        public List<int> Candidates { get; } = new();

        /// <summary>
        /// Null when neither side is large enough to index or a coordinate is unusable.
        /// </summary>
        public static SlideBandIndexes? Build(
            SlideBand.Axis axis,
            List<Entity> moving,
            Vector[] movingVertices,
            double movingDx,
            double movingDy,
            List<Entity> stationary,
            Vector[] stationaryVertices,
            bool arcToLine
        )
        {
            if (
                stationary.Count < SlideBandIndex.MinimumEntities
                && moving.Count < SlideBandIndex.MinimumEntities
            )
                return null;

            double along = 0,
                perpendicular = 0;
            var stationaryCurves = SlideBandIndex.Measure(
                axis,
                stationary,
                stationaryVertices,
                0,
                0,
                ref along,
                ref perpendicular
            );
            var movingCurves = SlideBandIndex.Measure(
                axis,
                moving,
                movingVertices,
                movingDx,
                movingDy,
                ref along,
                ref perpendicular
            );
            var reach = SlideBandIndex.Reach(along, perpendicular);
            if (!double.IsFinite(reach))
                return null;

            var all = SlideBandIndex.Kind.All;
            var lines = SlideBandIndex.Kind.Lines;
            var curves = SlideBandIndex.Kind.Curves;
            return new SlideBandIndexes
            {
                Stationary = SlideBandIndex.Build(axis, stationary, 0, 0, reach, all),
                Moving = SlideBandIndex.Build(axis, moving, movingDx, movingDy, reach, all),
                StationaryLines = arcToLine && movingCurves > 0
                    ? SlideBandIndex.Build(axis, stationary, 0, 0, reach, lines)
                    : null,
                MovingLines = arcToLine && stationaryCurves > 0
                    ? SlideBandIndex.Build(axis, moving, movingDx, movingDy, reach, lines)
                    : null,
                StationaryCurves = movingCurves > 0 && stationaryCurves > 0
                    ? SlideBandIndex.Build(axis, stationary, 0, 0, reach, curves)
                    : null,
            };
        }
    }
}
