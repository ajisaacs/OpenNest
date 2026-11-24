using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// One moving boundary slid against one stationary boundary from many start offsets, as a
    /// best-fit batch does. <see cref="FirstBlocking"/> returns, bit for bit, what
    /// <see cref="SlideResolver.FirstBlocking"/> returns for an <see cref="EntitySlideEvents"/>
    /// (or <see cref="LineSlideEvents"/>) source at that offset: every event that can change
    /// the answer is computed by the same arithmetic on the same operands, reaches the same
    /// sinks in the same order and is classified by the same contact classifier.
    /// </summary>
    /// <remarks>
    /// Four things differ from running those sources once per offset.
    /// <list type="bullet">
    /// <item>Everything that does not depend on the offset is prepared once: the boundaries
    /// are read into packed arrays, the vertices and contact topology are built once, and the
    /// band indexes are built once per push axis instead of once per slide (they were most of
    /// a slide's time and nearly all of its allocation). A slide allocates nothing; its
    /// scratch lists belong to the thread.</item>
    /// <item>A band index only chooses which pairs get the exact <see cref="SlideBand"/> test,
    /// and that test still decides every pair. So a shared index need only return a superset
    /// of the pairs each slide's own index would, in ascending order: see
    /// <see cref="BuildAxis"/> for why it does.</item>
    /// <item>A vertex or curve of one boundary that lies outside the other boundary's whole
    /// extent across the push, by more than any band test reaches, is passed over without
    /// those tests: each of them would reject it. Most of a boundary is, when the two sit
    /// side by side.</item>
    /// <item>While the nearest event is being searched for, a pair is skipped when no event
    /// of it can be nearer than the nearest found so far: the sink would discard those
    /// events unchanged. See <see cref="PruneSlack"/>.</item>
    /// </list>
    /// The last two apply to pushes along an exact axis, which is every push of the pair
    /// search; other directions enumerate every pair.
    /// Call <see cref="Cover"/> for every offset and then <see cref="Index"/> before sliding;
    /// until the next <see cref="Cover"/> an instance is immutable and may be shared by
    /// concurrent slides. Further batches of slides of the same boundaries may follow, each
    /// covered and indexed before it starts and never while slides are running. The
    /// entities must not change while it is in use.
    /// </remarks>
    internal sealed class SlideBatch
    {
        private const double Eps = Tolerance.Epsilon;

        private const byte OtherKind = 0;
        private const byte LineKind = 1;
        private const byte ArcKind = 2;
        private const byte CircleKind = 3;

        // A translated coordinate is at most its untranslated magnitude plus the offset's;
        // this covers the last-place difference between that sum and how Measure adds a
        // radius to a translated center.
        private const double Headroom = 1 + 1e-12;

        // Extra reach, relative to the largest perpendicular magnitude, of an index over
        // untranslated moving extents that is queried at (value - offset). Shifting the query
        // instead of the extents moves each end by a few units in the last place of those
        // magnitudes (about 1.5e-15 of them in all); this is some 600,000 times that.
        private const double ShiftSlack = 1e-9;

        /// <summary>
        /// Margin, relative to 1 + the largest coordinate magnitude B of a slide, of the gap
        /// test that skips a pair.
        /// </summary>
        /// <remarks>
        /// For a push along an exact axis every event's distance is the travel t its kernel
        /// computes from one primitive of the moving boundary to one of the stationary
        /// boundary. Let the gap be the distance along the push from the front of the moving
        /// primitive's extent to the near side of the stationary primitive's (a vertex is a
        /// point, a line its two ends, a curve its center plus and minus its radius). Then
        /// the computed t is at least gap − margin:
        /// <list type="bullet">
        /// <item>A ray against a line (<see cref="SlideEvents.RayLine"/>) emits only for a
        /// segment parameter in [−ε, 1 + ε], ε = <see cref="Tolerance.Epsilon"/>, so its hit
        /// is at most ε of the segment's length (under 2εB) past an end; t itself is
        /// rounded by a few units in the last place of B.</item>
        /// <item>A ray against a circle (<see cref="SlideCurvePrimitives.SolveRayCircle"/>)
        /// hits the circle, so it is inside the curve's extent; its discriminant is rounded
        /// by about 2e-14·B², which moves a root by at most 2e-7·B. Curve tangency solves
        /// the same equation for a radius no larger than the two radii together.</item>
        /// <item>An arc's closest-approach point is on its circle, then a ray against a
        /// line.</item>
        /// </list>
        /// That is under 2.1e-5·B in all; the margin is nearly twice it. An event at or
        /// beyond the nearest distance found so far is discarded by
        /// <see cref="NearestSlideEvent.Add"/> without changing it, so a pair whose gap less
        /// the margin reaches that distance is skipped with the sink left exactly as it
        /// would have been. The test is false, and nothing is skipped, when no event has
        /// been found, when every event is wanted, when the direction is not an axis, or
        /// when any operand is not a number.
        /// </remarks>
        private const double PruneSlack = 4 * Eps;

        private readonly Boundary moving;
        private readonly Boundary stationary;
        private readonly bool banded;
        private readonly OffsetRange rangeY = new OffsetRange();
        private readonly OffsetRange rangeX = new OffsetRange();
        private AxisData perpendicularY;
        private AxisData perpendicularX;

        private SlideBatch(Boundary moving, Boundary stationary, bool banded)
        {
            this.moving = moving;
            this.stationary = stationary;
            this.banded = banded;
        }

        /// <summary>
        /// Slides of native Line/Arc/Circle boundaries, as <see cref="EntitySlideEvents"/>
        /// with arc-to-line events enumerates them.
        /// </summary>
        public static SlideBatch ForEntities(List<Entity> moving, List<Entity> stationary)
        {
            // EntitySlideEvents reads a stationary entity translated by zero, which turns a
            // negative zero coordinate into a positive one; keep that.
            return new SlideBatch(
                Pack(moving, SpatialQuery.ExtractEntityVertices(moving), false),
                Pack(stationary, SpatialQuery.ExtractEntityVertices(stationary), true),
                true
            );
        }

        /// <summary>
        /// Slides of line boundaries, as <see cref="LineSlideEvents"/> enumerates them: every
        /// vertex against every line, stationary lines read as stored.
        /// </summary>
        public static SlideBatch ForLines(List<Line> moving, List<Line> stationary)
        {
            return new SlideBatch(
                Pack(new List<Entity>(moving), Vertices(moving), false),
                Pack(new List<Entity>(stationary), Vertices(stationary), false),
                false
            );
        }

        private static Vector[] Vertices(List<Line> lines) =>
            lines.SelectMany(line => new[] { line.StartPoint, line.EndPoint }).Distinct().ToArray();

        /// <summary>Notes a slide the batch will be asked for, so its indexes cover it.</summary>
        public void Cover(double dx, double dy, double dirX, double dirY)
        {
            if (!banded)
                return;

            var band = SlideBand.For(dirX, dirY);
            if (band == SlideBand.Axis.None)
                return;

            var range = band == SlideBand.Axis.PerpendicularY ? rangeY : rangeX;
            range.Used = true;
            range.Along = Larger(range.Along, System.Math.Abs(SlideBand.Para(band, dx, dy)));
            range.Perpendicular = Larger(
                range.Perpendicular,
                System.Math.Abs(SlideBand.Perp(band, dx, dy))
            );
        }

        /// <summary>
        /// Prepares each push axis for the covered slides. Call before sliding, and again
        /// after covering more slides: an axis already sized for them is kept.
        /// </summary>
        public void Index()
        {
            if (!Holds(perpendicularY, rangeY))
                perpendicularY = BuildAxis(SlideBand.Axis.PerpendicularY, rangeY);
            if (!Holds(perpendicularX, rangeX))
                perpendicularX = BuildAxis(SlideBand.Axis.PerpendicularX, rangeX);
        }

        private static bool Holds(AxisData data, OffsetRange range) =>
            data != null && range.Along <= data.Along && range.Perpendicular <= data.Perpendicular;

        /// <summary>
        /// Travel along the unit direction (dirX, dirY) from the moving boundary's start at
        /// (dx, dy) to the first contact that blocks, or double.MaxValue.
        /// </summary>
        public double FirstBlocking(double dx, double dy, double dirX, double dirY)
        {
            var scratch = SlideScratch.Current;

            var first = new NearestSink();
            Enumerate(ref first, dx, dy, dirX, dirY, scratch);
            var nearest = first.Nearest;

            if (!nearest.Found)
                return double.MaxValue;

            var origin = new Vector(dx, dy);
            if (Blocks(nearest.MovingPoint, nearest.StationaryPoint, origin, dirX, dirY))
                return nearest.Distance;

            var all = new EverySink { Events = scratch.Events };
            all.Events.Clear();
            Enumerate(ref all, dx, dy, dirX, dirY, scratch);
            all.Events.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            foreach (var (distance, movingPoint, stationaryPoint) in all.Events)
            {
                if (Blocks(movingPoint, stationaryPoint, origin, dirX, dirY))
                    return distance;
            }

            return double.MaxValue;
        }

        // SlideContactClassifier.Blocks for a classifier placed at (origin, zero).
        private bool Blocks(
            Vector movingPoint,
            Vector stationaryPoint,
            Vector origin,
            double dirX,
            double dirY
        ) =>
            SlideContact.Blocks(
                moving.Contact,
                movingPoint - origin,
                stationary.Contact,
                stationaryPoint - Vector.Zero,
                dirX,
                dirY
            );

        /// <summary>
        /// The events of <see cref="EntitySlideEvents.Enumerate"/>, in its order, less those
        /// that cannot change the sink (see <see cref="Skip"/>). Without bands (line
        /// boundaries) the curve phases have nothing to visit and this is
        /// <see cref="LineSlideEvents.Enumerate"/>.
        /// </summary>
        private void Enumerate<TSink>(
            ref TSink sink,
            double dx,
            double dy,
            double dirX,
            double dirY,
            SlideScratch scratch
        )
            where TSink : struct, ILimitedSink
        {
            var band = banded ? SlideBand.For(dirX, dirY) : SlideBand.Axis.None;
            var axis = AxisFor(band, dx, dy);
            var alongX = band == SlideBand.Axis.PerpendicularY;

            var slide = new Slide
            {
                Dx = dx,
                Dy = dy,
                DirX = dirX,
                DirY = dirY,
                Band = band,
                Axis = axis,
                AlongX = alongX,
                Forward = SlideBand.Para(band, dirX, dirY) > 0,
                Shift = SlideBand.Perp(band, dx, dy),
                Travel = SlideBand.Para(band, dx, dy),
                Margin = axis != null ? axis.Margin : double.NaN,
            };
            slide.Prune = !double.IsNaN(slide.Margin);
            slide.StationaryMin = alongX ? stationary.MinX : stationary.MinY;
            slide.StationaryMax = alongX ? stationary.MaxX : stationary.MaxY;
            slide.MovingMin = (alongX ? moving.MinX : moving.MinY) + slide.Travel;
            slide.MovingMax = (alongX ? moving.MaxX : moving.MaxY) + slide.Travel;

            // Each boundary's extent across the push (the acceptance extents of its entities,
            // see Pack), widened by the farthest any band test reaches beyond an entity's
            // own (see BuildAxis): a vertex or curve of one boundary outside the other's is
            // rejected by every band test of that boundary, so it is passed over without
            // them. NaN without an axis, which passes over nothing.
            var across = axis != null ? axis.Reach : double.NaN;
            var movingLow = (alongX ? moving.MinY : moving.MinX) + slide.Shift;
            var movingHigh = (alongX ? moving.MaxY : moving.MaxX) + slide.Shift;
            slide.StationaryLow = (alongX ? stationary.MinY : stationary.MinX) - across;
            slide.StationaryHigh = (alongX ? stationary.MaxY : stationary.MaxX) + across;
            slide.MovingLow = movingLow - across;
            slide.MovingHigh = movingHigh + across;

            // The boundaries are nowhere level across the push: no pair can emit.
            if (movingLow > slide.StationaryHigh || movingHigh < slide.StationaryLow)
                return;

            // Phase 1: moving vertices along the push against stationary entities.
            var vertices = moving.Vertices;
            var count = vertices.Length;
            for (var v = 0; v < count; v++)
            {
                var level = SlideBand.Perp(band, vertices[v].X + dx, vertices[v].Y + dy);
                if (level < slide.StationaryLow || level > slide.StationaryHigh)
                    continue;

                MovingVertex(ref sink, in slide, v);
                if (sink.IsDone)
                    return;
            }

            // Phase 2: stationary vertices against the push onto moving entities.
            vertices = stationary.Vertices;
            count = vertices.Length;
            for (var v = 0; v < count; v++)
            {
                var level = SlideBand.Perp(band, vertices[v].X, vertices[v].Y);
                if (level < slide.MovingLow || level > slide.MovingHigh)
                    continue;

                StationaryVertex(ref sink, in slide, v);
                if (sink.IsDone)
                    return;
            }

            // Phase 3: arc-to-line closest points, which vertex sampling can miss.
            if (moving.Curves.Length > 0 && stationary.Lines.Length > 0)
            {
                ArcToLine(
                    ref sink, in slide, moving, dx, dy, stationary, 0, 0, dirX, dirY, true,
                    axis?.Stationary, 0, scratch, slide.StationaryMin, slide.StationaryMax,
                    slide.StationaryLow, slide.StationaryHigh
                );
                if (sink.IsDone)
                    return;
            }

            if (stationary.Curves.Length > 0 && moving.Lines.Length > 0)
            {
                ArcToLine(
                    ref sink, in slide, stationary, 0, 0, moving, dx, dy, -dirX, -dirY, false,
                    axis?.Moving, slide.Shift, scratch, slide.MovingMin, slide.MovingMax,
                    slide.MovingLow, slide.MovingHigh
                );
                if (sink.IsDone)
                    return;
            }

            // Phase 4: native curve tangency, including a convex corner inside a concave arc.
            var movingItems = moving.Items;
            var stationaryItems = stationary.Items;
            var movingCurves = moving.Curves;
            var stationaryCurves = stationary.Curves;
            if (stationaryCurves.Length == 0)
                return;

            var curveIndex = axis?.Stationary;
            var candidates = scratch.Candidates;
            for (var a = 0; a < movingCurves.Length; a++)
            {
                var i = movingCurves[a];
                var mcx = movingItems[i].X1;
                var mcy = movingItems[i].Y1;
                var mr = movingItems[i].X2;
                var along = SlideBand.Para(band, mcx + dx, mcy + dy);
                var radius = System.Math.Abs(mr);

                if (Skip(in slide, ref sink, slide.Forward ? slide.StationaryMin - (along + radius) : along - radius - slide.StationaryMax))
                    continue;

                var c = SlideBand.Perp(band, mcx + dx, mcy + dy);
                var reach = SlideBandIndex.CurveReach(mr);
                if (c + reach < slide.StationaryLow || c - reach > slide.StationaryHigh)
                    continue;

                var indexed = false;
                if (curveIndex != null)
                {
                    indexed = curveIndex.TryRange(c - reach, c + reach, candidates);
                }

                var visits = indexed ? candidates.Count : stationaryCurves.Length;
                for (var n = 0; n < visits; n++)
                {
                    var j = indexed ? candidates[n] : stationaryCurves[n];
                    ref readonly var curve = ref stationaryItems[j];
                    if (curve.Kind != ArcKind && curve.Kind != CircleKind)
                        continue;

                    if (Skip(in slide, ref sink, slide.Forward ? Lo(in curve, alongX) - (along + radius) : along - radius - Hi(in curve, alongX)))
                        continue;

                    if (
                        band != SlideBand.Axis.None
                        && !SlideBand.CirclesMayTouch(
                            band, mcx + dx, mcy + dy, mr, curve.RawX, curve.RawY, curve.X2
                        )
                    )
                        continue;

                    SlideEvents.CurveTangency(
                        ref sink,
                        mcx + dx,
                        mcy + dy,
                        mr,
                        moving.Arcs[i],
                        curve.RawX,
                        curve.RawY,
                        curve.X2,
                        stationary.Arcs[j],
                        dirX,
                        dirY
                    );
                    if (sink.IsDone)
                        return;
                }
            }
        }

        /// <summary>
        /// One moving vertex of phase 1: its ray along the push against the stationary
        /// entities.
        /// </summary>
        private void MovingVertex<TSink>(ref TSink sink, in Slide slide, int v)
            where TSink : struct, ILimitedSink
        {
            var band = slide.Band;
            var alongX = slide.AlongX;
            var vx = moving.Vertices[v].X + slide.Dx;
            var vy = moving.Vertices[v].Y + slide.Dy;
            var along = SlideBand.Para(band, vx, vy);

            if (Skip(in slide, ref sink, slide.Forward ? slide.StationaryMin - along : along - slide.StationaryMax))
                return;

            // SlideBand.RayMayHit keeps every entity for a vertex that is not bounded.
            var test = band != SlideBand.Axis.None && SlideBand.Bounded(vx) && SlideBand.Bounded(vy);
            var items = stationary.Items;
            var index = slide.Axis?.Stationary;

            if (
                index != null
                && index.TryPoint(SlideBand.Perp(band, vx, vy), out var first, out var last)
            )
            {
                for (var k = first; k < last; k++)
                {
                    ref readonly var item = ref items[index.Item(k)];
                    if (Skip(in slide, ref sink, slide.Forward ? Lo(in item, alongX) - along : along - Hi(in item, alongX)))
                        continue;
                    if (test && !MayHit(band, vx, vy, in item, item.X1, item.Y1, item.X2, item.Y2))
                        continue;
                    Ray(ref sink, vx, vy, in item, item.X1, item.Y1, item.X2, item.Y2, slide.DirX, slide.DirY, true);
                    if (sink.IsDone)
                        return;
                }
                return;
            }

            for (var j = 0; j < items.Length; j++)
            {
                ref readonly var item = ref items[j];
                if (Skip(in slide, ref sink, slide.Forward ? Lo(in item, alongX) - along : along - Hi(in item, alongX)))
                    continue;
                if (test && !MayHit(band, vx, vy, in item, item.X1, item.Y1, item.X2, item.Y2))
                    continue;
                Ray(ref sink, vx, vy, in item, item.X1, item.Y1, item.X2, item.Y2, slide.DirX, slide.DirY, true);
                if (sink.IsDone)
                    return;
            }
        }

        /// <summary>
        /// One stationary vertex of phase 2: its ray against the push onto the moving
        /// entities.
        /// </summary>
        private void StationaryVertex<TSink>(ref TSink sink, in Slide slide, int v)
            where TSink : struct, ILimitedSink
        {
            var band = slide.Band;
            var alongX = slide.AlongX;
            var dx = slide.Dx;
            var dy = slide.Dy;
            var travel = slide.Travel;
            var vx = stationary.Vertices[v].X;
            var vy = stationary.Vertices[v].Y;
            var along = SlideBand.Para(band, vx, vy);

            if (Skip(in slide, ref sink, slide.Forward ? along - slide.MovingMax : slide.MovingMin - along))
                return;

            var test = band != SlideBand.Axis.None && SlideBand.Bounded(vx) && SlideBand.Bounded(vy);
            var items = moving.Items;
            var index = slide.Axis?.Moving;

            // The moving index holds untranslated extents: ask it where the vertex is in
            // the moving boundary's own frame.
            if (
                index != null
                && index.TryPoint(SlideBand.Perp(band, vx, vy) - slide.Shift, out var first, out var last)
            )
            {
                for (var k = first; k < last; k++)
                {
                    ref readonly var item = ref items[index.Item(k)];
                    if (Skip(in slide, ref sink, slide.Forward ? along - (Hi(in item, alongX) + travel) : Lo(in item, alongX) + travel - along))
                        continue;
                    var ax = item.X1 + dx;
                    var ay = item.Y1 + dy;
                    var bx = item.X2 + dx;
                    var by = item.Y2 + dy;
                    if (test && !MayHit(band, vx, vy, in item, ax, ay, bx, by))
                        continue;
                    Ray(ref sink, vx, vy, in item, ax, ay, bx, by, -slide.DirX, -slide.DirY, false);
                    if (sink.IsDone)
                        return;
                }
                return;
            }

            for (var j = 0; j < items.Length; j++)
            {
                ref readonly var item = ref items[j];
                if (Skip(in slide, ref sink, slide.Forward ? along - (Hi(in item, alongX) + travel) : Lo(in item, alongX) + travel - along))
                    continue;
                var ax = item.X1 + dx;
                var ay = item.Y1 + dy;
                var bx = item.X2 + dx;
                var by = item.Y2 + dy;
                if (test && !MayHit(band, vx, vy, in item, ax, ay, bx, by))
                    continue;
                Ray(ref sink, vx, vy, in item, ax, ay, bx, by, -slide.DirX, -slide.DirY, false);
                if (sink.IsDone)
                    return;
            }
        }

        /// <summary>
        /// True when no event of a pair whose gap along the push is <paramref name="gap"/>
        /// can change the sink (see <see cref="PruneSlack"/>): its distance would be at or
        /// beyond what the sink already discards. False whenever an operand is not a
        /// number.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Skip<TSink>(in Slide slide, ref TSink sink, double gap)
            where TSink : struct, ILimitedSink
        {
            return slide.Prune && gap - slide.Margin >= sink.Limit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Lo(in Item item, bool alongX) => alongX ? item.LoX : item.LoY;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Hi(in Item item, bool alongX) => alongX ? item.HiX : item.HiY;

        /// <summary>
        /// <see cref="SlideBand.RayMayHit"/> after its vertex check, for an entity whose
        /// translated coordinates are (ax, ay)-(bx, by).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MayHit(
            SlideBand.Axis band,
            double vx,
            double vy,
            in Item item,
            double ax,
            double ay,
            double bx,
            double by
        )
        {
            switch (item.Kind)
            {
                case LineKind:
                    if (
                        !SlideBand.Bounded(ax) || !SlideBand.Bounded(ay)
                        || !SlideBand.Bounded(bx) || !SlideBand.Bounded(by)
                    )
                        return true;
                    return SlideBand.PointMayReachSegment(
                        SlideBand.Perp(band, vx, vy),
                        SlideBand.Perp(band, ax, ay),
                        SlideBand.Perp(band, bx, by)
                    );
                case ArcKind:
                case CircleKind:
                    return SlideBand.PointMayReachCircle(band, vx, vy, ax, ay, item.X2);
                default:
                    return true;
            }
        }

        /// <summary><see cref="SlideEvents.Ray"/> for an entity translated to (ax, ay)-(bx, by).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Ray<TSink>(
            ref TSink sink,
            double vx,
            double vy,
            in Item item,
            double ax,
            double ay,
            double bx,
            double by,
            double rayX,
            double rayY,
            bool vertexMoves
        )
            where TSink : struct, ISlideEventSink
        {
            switch (item.Kind)
            {
                case LineKind:
                    SlideEvents.RayLine(ref sink, vx, vy, ax, ay, bx, by, rayX, rayY, vertexMoves);
                    break;
                case ArcKind:
                    RayArc(ref sink, vx, vy, in item, ax, ay, rayX, rayY, vertexMoves);
                    break;
                case CircleKind:
                    RayCircle(ref sink, vx, vy, ax, ay, item.X2, rayX, rayY, vertexMoves);
                    break;
            }
        }

        private static void RayArc<TSink>(
            ref TSink sink,
            double vx,
            double vy,
            in Item arc,
            double cx,
            double cy,
            double rayX,
            double rayY,
            bool vertexMoves
        )
            where TSink : struct, ISlideEventSink
        {
            if (!SlideCurvePrimitives.SolveRayCircle(vx, vy, cx, cy, arc.X2, rayX, rayY, out var t1, out var t2))
                return;

            for (var k = 0; k < 2; k++)
            {
                var t = k == 0 ? t1 : t2;
                if (t <= -Eps)
                    continue;

                var hitAngle = Normalize(PortableMath.Atan2(vy + t * rayY - cy, vx + t * rayX - cx));
                if (!Contains(in arc, hitAngle))
                    continue;

                SlideEvents.Emit(ref sink, vx, vy, t, rayX, rayY, vertexMoves);
                if (sink.IsDone)
                    return;
            }
        }

        private static void RayCircle<TSink>(
            ref TSink sink,
            double vx,
            double vy,
            double cx,
            double cy,
            double r,
            double rayX,
            double rayY,
            bool vertexMoves
        )
            where TSink : struct, ISlideEventSink
        {
            if (!SlideCurvePrimitives.SolveRayCircle(vx, vy, cx, cy, r, rayX, rayY, out var t1, out var t2))
                return;

            for (var k = 0; k < 2; k++)
            {
                var t = k == 0 ? t1 : t2;
                if (t < -Eps)
                    continue;

                SlideEvents.Emit(ref sink, vx, vy, t, rayX, rayY, vertexMoves);
                if (sink.IsDone)
                    return;
            }
        }

        /// <summary>
        /// <see cref="SlideEvents.ArcToLine"/> for the curves of one boundary against the
        /// lines of the other. The directions of a line's two closest-approach points and
        /// their cosines and sines depend only on the line's direction, so they are looked up
        /// (see <see cref="SlideScratch.Normal"/>) instead of being recomputed for every arc.
        /// </summary>
        private static void ArcToLine<TSink>(
            ref TSink sink,
            in Slide slide,
            Boundary arcs,
            double arcDx,
            double arcDy,
            Boundary lines,
            double lineDx,
            double lineDy,
            double rayX,
            double rayY,
            bool arcMoves,
            SlideBandIndex lineIndex,
            double lineShift,
            SlideScratch scratch,
            double linesMin,
            double linesMax,
            double linesLow,
            double linesHigh
        )
            where TSink : struct, ILimitedSink
        {
            var band = slide.Band;
            var arcItems = arcs.Items;
            var lineItems = lines.Items;
            var curves = arcs.Curves;
            var allLines = lines.Lines;
            var candidates = scratch.Candidates;

            // The gap test: the arc is ahead of the line along the push when it moves with
            // the push or stands still against a push the other way.
            var alongX = slide.AlongX;
            var ahead = arcMoves == slide.Forward;
            var lineSlide = SlideBand.Para(band, lineDx, lineDy);

            for (var a = 0; a < curves.Length; a++)
            {
                ref readonly var arc = ref arcItems[curves[a]];
                var r = arc.X2;
                var isArc = arc.Kind == ArcKind;
                var cx = arc.X1 + arcDx;
                var cy = arc.Y1 + arcDy;
                var radius = System.Math.Abs(r);
                var along = SlideBand.Para(band, cx, cy);

                if (Skip(in slide, ref sink, ahead ? linesMin - (along + radius) : along - radius - linesMax))
                    continue;

                var c = SlideBand.Perp(band, cx, cy);
                if (c + radius < linesLow || c - radius > linesHigh)
                    continue;

                // With an index, visit only the entities whose extents the circle's meets,
                // ascending; the lines among them are those CircleMayReachLine can accept.
                var indexed = false;
                if (lineIndex != null)
                {
                    indexed = lineIndex.TryRange(c - radius - lineShift, c + radius - lineShift, candidates);
                }

                var count = indexed ? candidates.Count : allLines.Length;
                for (var n = 0; n < count; n++)
                {
                    ref readonly var line = ref lineItems[indexed ? candidates[n] : allLines[n]];
                    if (line.Kind != LineKind)
                        continue;

                    if (
                        Skip(
                            in slide,
                            ref sink,
                            ahead
                                ? Lo(in line, alongX) + lineSlide - (along + radius)
                                : along - radius - (Hi(in line, alongX) + lineSlide)
                        )
                    )
                        continue;

                    var p1x = line.X1 + lineDx;
                    var p1y = line.Y1 + lineDy;
                    var p2x = line.X2 + lineDx;
                    var p2y = line.Y2 + lineDy;

                    if (
                        band != SlideBand.Axis.None
                        && !SlideBand.CircleMayReachLine(band, cx, cy, r, p1x, p1y, p2x, p2y)
                    )
                        continue;

                    var ex = p2x - p1x;
                    var ey = p2y - p1y;

                    var det = ex * rayY - ey * rayX;
                    if (System.Math.Abs(det) < Eps)
                        continue;

                    ref readonly var normal = ref scratch.Normal(ex, ey);

                    for (var k = 0; k < 2; k++)
                    {
                        if (isArc && !Contains(in arc, k == 0 ? normal.Theta1 : normal.Theta2))
                            continue;

                        var qx = cx + r * (k == 0 ? normal.Cos1 : normal.Cos2);
                        var qy = cy + r * (k == 0 ? normal.Sin1 : normal.Sin2);

                        SlideEvents.RayLine(ref sink, qx, qy, p1x, p1y, p2x, p2y, rayX, rayY, arcMoves);
                        if (sink.IsDone)
                            return;
                    }
                }
            }
        }

        /// <summary>
        /// <see cref="Angle.NormalizeRad"/>. An angle within one turn of zero is its own
        /// remainder, so the division is skipped for it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double Normalize(double angle)
        {
            if (angle >= 0)
            {
                if (angle < Angle.TwoPI)
                    return angle;
            }
            else if (angle > -Angle.TwoPI)
            {
                return Angle.TwoPI + angle;
            }

            return Angle.NormalizeRad(angle);
        }

        /// <summary>
        /// <see cref="Angle.IsBetweenRad"/> for the arc's angles, with the parts that depend
        /// only on the arc (the swap for a reversed arc, the sweep and the full-circle test)
        /// taken from the packed item.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Contains(in Item arc, double angle)
        {
            if (arc.Full)
                return true;

            return arc.Sweep >= Normalize(angle - arc.From) - Eps
                || arc.Sweep >= Normalize(arc.To - angle) - Eps;
        }

        private static double Larger(double max, double value) =>
            value > max || double.IsNaN(value) ? value : max;

        /// <summary>
        /// What was prepared for an axis, or null when the slide is not along one or starts
        /// outside the offsets it was sized for (the band tests then scan everything and
        /// nothing is skipped, as without it).
        /// </summary>
        private AxisData AxisFor(SlideBand.Axis band, double dx, double dy)
        {
            if (band == SlideBand.Axis.None)
                return null;

            var axis = band == SlideBand.Axis.PerpendicularY ? perpendicularY : perpendicularX;
            if (axis == null)
                return null;

            return
                System.Math.Abs(SlideBand.Para(band, dx, dy)) <= axis.Along
                && System.Math.Abs(SlideBand.Perp(band, dx, dy)) <= axis.Perpendicular
                ? axis
                : null;
        }

        /// <summary>
        /// The gap-test margin and the band indexes shared by every covered slide along one
        /// axis.
        /// </summary>
        /// <remarks>
        /// A slide's own indexes (<see cref="SlideBandIndexes.Build"/>) hold each entity's
        /// exact acceptance extent widened by a reach sized from that slide's coordinates. A
        /// pair the band test accepts has its query value inside that extent, and a value
        /// inside an extent is always found, whatever the bucket layout. The shared indexes
        /// keep that property for every covered slide:
        /// <list type="bullet">
        /// <item>Stationary extents do not depend on the offset. The shared reach is
        /// <see cref="SlideBandIndex.Reach"/> of bounds no smaller than any covered slide's,
        /// and Reach never decreases as its arguments grow, so each shared extent contains
        /// the slide's own.</item>
        /// <item>Moving extents move with the offset. They are indexed untranslated and
        /// queried at the value minus the offset, which is the same containment test up to
        /// rounding of the translation; <see cref="ShiftSlack"/> widens the reach by far
        /// more than that rounding.</item>
        /// </list>
        /// Queries return ascending entity indices without repeats, so the surviving pairs
        /// keep their order. An index is left out exactly where the per-slide code leaves it
        /// out or where no bound is usable; the band tests then scan every entity.
        /// </remarks>
        private AxisData BuildAxis(SlideBand.Axis axis, OffsetRange range)
        {
            if (!range.Used)
                return null;

            double along = 0,
                perpendicular = 0;
            SlideBandIndex.Measure(
                axis, stationary.Entities, stationary.Vertices, 0, 0, ref along, ref perpendicular
            );
            double movingAlong = 0,
                movingPerpendicular = 0;
            SlideBandIndex.Measure(
                axis, moving.Entities, moving.Vertices, 0, 0, ref movingAlong, ref movingPerpendicular
            );

            along = Larger(along, (movingAlong + range.Along) * Headroom);
            perpendicular = Larger(perpendicular, (movingPerpendicular + range.Perpendicular) * Headroom);

            // The perpendicular bound includes every radius, so the sum bounds every
            // coordinate and radius of every covered slide.
            var size = along + perpendicular;
            var reach = SlideBandIndex.Reach(along, perpendicular);
            var movingReach = reach + ShiftSlack * (1 + perpendicular + reach);
            var data = new AxisData
            {
                Along = range.Along,
                Perpendicular = range.Perpendicular,
                Margin = size <= SlideBand.CoordinateLimit ? PruneSlack * (1 + size) : double.NaN,
                Reach = double.IsFinite(movingReach) ? movingReach : double.NaN,
            };

            if (
                stationary.Items.Length < SlideBandIndex.MinimumEntities
                && moving.Items.Length < SlideBandIndex.MinimumEntities
            )
                return data;

            if (!double.IsFinite(reach))
                return data;

            // One index of every entity per boundary. The per-slide code keeps separate
            // line and curve indexes for the curve phases; an entity's extent is the same in
            // each, so the one index returns their entities among others, which those
            // phases pass over by kind.
            var all = SlideBandIndex.Kind.All;
            data.Stationary = SlideBandIndex.Build(axis, stationary.Entities, 0, 0, reach, all);
            data.Moving = SlideBandIndex.Build(axis, moving.Entities, 0, 0, movingReach, all);
            return data;
        }

        private static Boundary Pack(List<Entity> entities, Vector[] vertices, bool translateByZero)
        {
            var count = entities.Count;
            var items = new Item[count];
            var arcs = new Arc[count];
            var lineCount = 0;
            var curveCount = 0;

            // Math.Min and Math.Max keep a NaN, which then fails every gap test.
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;

            // Translating by zero is what the kernels do to a stationary entity; it is not a
            // no-op for a negative zero.
            double zero = 0;

            for (var i = 0; i < count; i++)
            {
                ref var item = ref items[i];
                switch (entities[i])
                {
                    case Line line:
                        item.Kind = LineKind;
                        item.X1 = translateByZero ? line.pt1.X + zero : line.pt1.X;
                        item.Y1 = translateByZero ? line.pt1.Y + zero : line.pt1.Y;
                        item.X2 = translateByZero ? line.pt2.X + zero : line.pt2.X;
                        item.Y2 = translateByZero ? line.pt2.Y + zero : line.pt2.Y;
                        item.LoX = System.Math.Min(item.X1, item.X2);
                        item.HiX = System.Math.Max(item.X1, item.X2);
                        item.LoY = System.Math.Min(item.Y1, item.Y2);
                        item.HiY = System.Math.Max(item.Y1, item.Y2);
                        lineCount++;
                        break;

                    case Arc arc:
                        {
                            item.Kind = ArcKind;
                            var center = arc.Center;
                            item.RawX = center.X;
                            item.RawY = center.Y;
                            item.X1 = translateByZero ? center.X + zero : center.X;
                            item.Y1 = translateByZero ? center.Y + zero : center.Y;
                            item.X2 = arc.Radius;
                            CircleExtent(ref item);

                            // Angle.IsBetweenRad(angle, StartAngle, EndAngle, IsReversed).
                            var a1 = arc.StartAngle;
                            var a2 = arc.EndAngle;
                            if (arc.IsReversed)
                                Generic.Swap(ref a1, ref a2);
                            item.From = a1;
                            item.To = a2;
                            item.Sweep = Angle.NormalizeRad(a2 - a1);
                            item.Full = a2.IsEqualTo(a1);
                            arcs[i] = arc;
                            curveCount++;
                            break;
                        }

                    case Circle circle:
                        {
                            item.Kind = CircleKind;
                            var center = circle.Center;
                            item.RawX = center.X;
                            item.RawY = center.Y;
                            item.X1 = translateByZero ? center.X + zero : center.X;
                            item.Y1 = translateByZero ? center.Y + zero : center.Y;
                            item.X2 = circle.Radius;
                            CircleExtent(ref item);
                            curveCount++;
                            break;
                        }

                    default:
                        // No kernel emits for it; a NaN extent is never skipped.
                        item.Kind = OtherKind;
                        item.LoX = item.HiX = item.LoY = item.HiY = double.NaN;
                        continue;
                }

                // The band tests accept a ray up to SpanSlack of a line's span past its
                // ends (SlideBand.PointMayReachSegment), a curve's within its circle.
                var slackX = item.Kind == LineKind ? SlideBand.SpanSlack * (item.HiX - item.LoX) : 0;
                var slackY = item.Kind == LineKind ? SlideBand.SpanSlack * (item.HiY - item.LoY) : 0;
                minX = System.Math.Min(minX, item.LoX - slackX);
                minY = System.Math.Min(minY, item.LoY - slackY);
                maxX = System.Math.Max(maxX, item.HiX + slackX);
                maxY = System.Math.Max(maxY, item.HiY + slackY);
            }

            // Vertices are points of the entities, give or take the rounding of an arc's ends.
            for (var i = 0; i < vertices.Length; i++)
            {
                minX = System.Math.Min(minX, vertices[i].X);
                minY = System.Math.Min(minY, vertices[i].Y);
                maxX = System.Math.Max(maxX, vertices[i].X);
                maxY = System.Math.Max(maxY, vertices[i].Y);
            }

            var lineIndices = new int[lineCount];
            var curveIndices = new int[curveCount];
            lineCount = curveCount = 0;
            for (var i = 0; i < count; i++)
            {
                if (items[i].Kind == LineKind)
                    lineIndices[lineCount++] = i;
                else if (items[i].Kind != OtherKind)
                    curveIndices[curveCount++] = i;
            }

            return new Boundary
            {
                Entities = entities,
                Items = items,
                Arcs = arcs,
                Lines = lineIndices,
                Curves = curveIndices,
                Vertices = vertices,
                Contact = SlideContactGeometry.Prepare(entities),
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
            };
        }

        private static void CircleExtent(ref Item item)
        {
            var radius = System.Math.Abs(item.X2);
            item.LoX = item.X1 - radius;
            item.HiX = item.X1 + radius;
            item.LoY = item.Y1 - radius;
            item.HiY = item.Y1 + radius;
        }

        /// <summary>A sink that can say which events it would discard unchanged.</summary>
        private interface ILimitedSink : ISlideEventSink
        {
            /// <summary>
            /// An event at or beyond this distance leaves the sink as it is. NaN while every
            /// event counts.
            /// </summary>
            double Limit { get; }
        }

        /// <summary>The values of one slide the phases share.</summary>
        private struct Slide
        {
            public double Dx;
            public double Dy;
            public double DirX;
            public double DirY;
            public SlideBand.Axis Band;
            public AxisData Axis;

            /// <summary>The push is along X.</summary>
            public bool AlongX;

            /// <summary>The push is toward the positive end of its axis.</summary>
            public bool Forward;

            /// <summary>The offset's components across and along the push.</summary>
            public double Shift;
            public double Travel;

            /// <summary>The gap test is usable (see PruneSlack), with this margin.</summary>
            public bool Prune;
            public double Margin;

            /// <summary>Extents along the push, the moving one translated.</summary>
            public double StationaryMin;
            public double StationaryMax;
            public double MovingMin;
            public double MovingMax;

            /// <summary>
            /// Extents across the push, the moving one translated, each widened by the
            /// reach of the band tests.
            /// </summary>
            public double StationaryLow;
            public double StationaryHigh;
            public double MovingLow;
            public double MovingHigh;
        }

        /// <summary><see cref="NearestSlideEvent"/>, which discards anything not nearer.</summary>
        private struct NearestSink : ILimitedSink
        {
            public NearestSlideEvent Nearest;

            public bool IsDone => Nearest.IsDone;

            public double Limit => Nearest.Found ? Nearest.Distance : double.NaN;

            public void Add(double distance, Vector movingPoint, Vector stationaryPoint) =>
                Nearest.Add(distance, movingPoint, stationaryPoint);
        }

        /// <summary><see cref="SlideEventList"/>: keeps every event.</summary>
        private struct EverySink : ILimitedSink
        {
            public List<(double Distance, Vector MovingPoint, Vector StationaryPoint)> Events;

            public bool IsDone => false;

            public double Limit => double.NaN;

            public void Add(double distance, Vector movingPoint, Vector stationaryPoint) =>
                Events.Add((distance, movingPoint, stationaryPoint));
        }

        private struct Item
        {
            // The entity's extent: a line's ends, a curve's center plus and minus its radius.
            public double LoX;
            public double HiX;
            public double LoY;
            public double HiY;

            // A line's ends, or a curve's center (X1, Y1) and radius (X2), as the kernels
            // read them.
            public double X1;
            public double Y1;
            public double X2;
            public double Y2;

            // A curve's center as stored: the tangency phase reads a stationary curve
            // without translating it.
            public double RawX;
            public double RawY;

            // An arc's angles in the order Angle.IsBetweenRad tests them, the sweep between
            // them and whether it reads them as a full circle.
            public double From;
            public double To;
            public double Sweep;
            public bool Full;

            public byte Kind;
        }

        private sealed class Boundary
        {
            public List<Entity> Entities;
            public Item[] Items;

            /// <summary>Per entity, the arc or null.</summary>
            public Arc[] Arcs;

            /// <summary>Indices of the lines, ascending.</summary>
            public int[] Lines;

            /// <summary>Indices of the arcs and circles, ascending.</summary>
            public int[] Curves;

            public Vector[] Vertices;
            public SlideContactGeometry Contact;

            // The extent of every vertex and of every entity as the band tests accept it: a
            // line with their slack past its ends, a curve as its whole circle.
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
        }

        private sealed class OffsetRange
        {
            public bool Used;
            public double Along;
            public double Perpendicular;
        }

        private sealed class AxisData
        {
            /// <summary>Largest |along| and |perpendicular| offset this was sized for.</summary>
            public double Along;
            public double Perpendicular;

            /// <summary>The gap-test margin, or NaN when the coordinates are not usable.</summary>
            public double Margin;

            /// <summary>
            /// How far across the push any band test reaches beyond an entity's extent, with
            /// the slack of a shifted query; NaN when the coordinates are not usable.
            /// </summary>
            public double Reach;

            public SlideBandIndex Stationary;
            public SlideBandIndex Moving;
        }
    }

    /// <summary>Per-thread working storage of <see cref="SlideBatch"/> slides.</summary>
    internal sealed class SlideScratch
    {
        // A power of two. Larger than the line directions of two boundaries in a few frames.
        private const int NormalSlots = 2048;

        [System.ThreadStatic]
        private static SlideScratch current;

        private readonly LineNormal[] normals = new LineNormal[NormalSlots];

        public static SlideScratch Current => current ??= new SlideScratch();

        /// <summary>Scratch list for range queries.</summary>
        public List<int> Candidates { get; } = new List<int>();

        /// <summary>Every event of a slide whose nearest contact did not block.</summary>
        public List<(double Distance, Vector MovingPoint, Vector StationaryPoint)> Events { get; } =
            new List<(double Distance, Vector MovingPoint, Vector StationaryPoint)>();

        /// <summary>
        /// The two arc angles at which the directional distance to a line of direction
        /// (ex, ey) is stationary, as <see cref="SlideEvents.ArcToLine"/> computes them, with
        /// their cosines and sines. A pure function of the exact bits of (ex, ey), so a value
        /// remembered from any earlier slide is the value computing it again would give.
        /// </summary>
        public ref readonly LineNormal Normal(double ex, double ey)
        {
            var exBits = System.BitConverter.DoubleToInt64Bits(ex);
            var eyBits = System.BitConverter.DoubleToInt64Bits(ey);
            var hash = (ulong)exBits * 0x9E3779B97F4A7C15UL + (ulong)eyBits * 0xC2B2AE3D27D4EB4FUL;
            ref var slot = ref normals[(int)(hash >> 40) & (NormalSlots - 1)];

            if (!slot.Set || slot.ExBits != exBits || slot.EyBits != eyBits)
            {
                var theta1 = Angle.NormalizeRad(PortableMath.Atan2(-ex, ey));
                var theta2 = Angle.NormalizeRad(theta1 + System.Math.PI);
                slot.ExBits = exBits;
                slot.EyBits = eyBits;
                slot.Theta1 = theta1;
                slot.Theta2 = theta2;
                slot.Cos1 = PortableMath.Cos(theta1);
                slot.Sin1 = PortableMath.Sin(theta1);
                slot.Cos2 = PortableMath.Cos(theta2);
                slot.Sin2 = PortableMath.Sin(theta2);
                slot.Set = true;
            }

            return ref slot;
        }

        internal struct LineNormal
        {
            public long ExBits;
            public long EyBits;
            public double Theta1;
            public double Theta2;
            public double Cos1;
            public double Sin1;
            public double Cos2;
            public double Sin2;
            public bool Set;
        }
    }
}
