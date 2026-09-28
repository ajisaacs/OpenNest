using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    internal enum ContactSide
    {
        /// <summary>The boundary could not be decomposed into closed loops.</summary>
        Unresolved,

        /// <summary>The point is not on the boundary: a tolerance near-miss, not a contact.</summary>
        Off,

        /// <summary>Several boundary runs meet here, or the corner is a cusp or spike.</summary>
        Ambiguous,

        /// <summary>The material sector is known.</summary>
        Sector,
    }

    /// <summary>
    /// Closed boundary loops of one entity list, prepared so a directional slide can tell
    /// which side of each boundary point is material. Immutable after
    /// <see cref="Prepare"/>, so one instance may be shared by concurrent queries.
    /// </summary>
    /// <remarks>
    /// Loops are recovered from contiguous runs whose end points chain back to their start
    /// (the order produced by <see cref="ShapeBuilder"/> and the offset helpers). Nesting
    /// depth decides holes: material is inside even-depth loops and outside odd-depth ones.
    /// When the list cannot be decomposed that way, every contact query is unresolved.
    /// </remarks>
    public sealed class SlideContactGeometry
    {
        // Contact points are computed from unsnapped ray parameters, so a genuine contact is
        // on both boundaries to floating-point accuracy. This also bounds the overlap sliver a
        // tangential classification can admit, so keep it far below spacing tolerances.
        internal const double IncidenceTolerance = 1e-7;

        private readonly List<Entity> entities;
        private readonly int[] loopOf;
        private readonly int[] previous;
        private readonly int[] following;
        private readonly bool[] materialLeft;

        private SlideContactGeometry(
            List<Entity> entities,
            int[] loopOf,
            int[] previous,
            int[] following,
            bool[] materialLeft
        )
        {
            this.entities = entities;
            this.loopOf = loopOf;
            this.previous = previous;
            this.following = following;
            this.materialLeft = materialLeft;
        }

        /// <summary>True when every entity belongs to a closed loop with a known material side.</summary>
        public bool IsResolved => materialLeft != null;

        public static SlideContactGeometry Prepare(List<Entity> entities)
        {
            var count = entities.Count;
            var loopOf = new int[count];
            var previous = new int[count];
            var following = new int[count];
            var loops = new List<(int First, int Last)>();

            var i = 0;
            while (i < count)
            {
                var first = i;
                if (entities[i] is Circle)
                {
                    i++;
                }
                else
                {
                    if (!TryEndpoints(entities[i], out var start, out _))
                        return Unresolved(entities);

                    var closed = false;
                    while (i < count && TryEndpoints(entities[i], out _, out var end))
                    {
                        // A lone closed arc is a loop; a lone line cannot be, even when it
                        // has zero length and so ends where it starts.
                        if (Near(end, start) && (i > first || entities[i] is Arc))
                        {
                            closed = true;
                            i++;
                            break;
                        }

                        if (
                            i + 1 >= count
                            || !TryEndpoints(entities[i + 1], out var nextStart, out _)
                            || !Near(nextStart, end)
                        )
                            break;

                        i++;
                    }

                    if (!closed)
                        return Unresolved(entities);
                }

                var loop = loops.Count;
                loops.Add((first, i - 1));
                for (var k = first; k < i; k++)
                {
                    loopOf[k] = loop;
                    previous[k] = k == first ? i - 1 : k - 1;
                    following[k] = k == i - 1 ? first : k + 1;
                }
            }

            var materialLeft = new bool[loops.Count];

            for (var loop = 0; loop < loops.Count; loop++)
            {
                var area = SignedArea(entities, loops[loop].First, loops[loop].Last);
                if (System.Math.Abs(area) <= Tolerance.Epsilon)
                    return Unresolved(entities);

                var depth = 0;
                if (loops.Count > 1)
                {
                    var sample = SamplePoint(entities[loops[loop].First]);
                    for (var other = 0; other < loops.Count; other++)
                    {
                        if (other == loop)
                            continue;
                        if (Contains(entities, loops[other].First, loops[other].Last, sample))
                            depth++;
                    }
                }

                materialLeft[loop] = (area > 0) == (depth % 2 == 0);
            }

            return new SlideContactGeometry(entities, loopOf, previous, following, materialLeft);
        }

        private static SlideContactGeometry Unresolved(List<Entity> entities) =>
            new SlideContactGeometry(entities, null, null, null, null);

        /// <summary>
        /// Material directions at a boundary point: an angular sector starting at
        /// <paramref name="start"/> and sweeping CCW by <paramref name="width"/>.
        /// Concavity is recorded separately at each sector ray: only the supporting
        /// curve, not an unrelated curve at that corner, can block a tangential slide.
        /// Entities wholly inside the incidence tolerance are treated as part of the corner.
        /// </summary>
        internal ContactSide GetMaterialSector(
            Vector point,
            out double start,
            out double width,
            out bool startConcave,
            out bool endConcave
        )
        {
            start = width = 0;
            startConcave = endConcave = false;
            if (materialLeft == null)
                return ContactSide.Unresolved;

            var best = -1;
            var bestDistance = double.MaxValue;
            for (var i = 0; i < entities.Count; i++)
            {
                var distance = DistanceTo(entities[i], point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            if (best < 0 || bestDistance > IncidenceTolerance)
                return ContactSide.Off;

            // Walk to the entities that enter and leave the tolerance disc.
            var loopLength = LoopLength(best);
            var incoming = best;
            var steps = 0;
            var smoothLoop = loopLength == 1 && (entities[best] is Circle
                || entities[best] is Arc fullArc && fullArc.IsFullCircle());
            while (!smoothLoop && StartsNear(incoming, point))
            {
                incoming = previous[incoming];
                if (++steps >= loopLength)
                    return ContactSide.Ambiguous;
            }

            var outgoing = best;
            steps = 0;
            while (!smoothLoop && EndsNear(outgoing, point))
            {
                outgoing = following[outgoing];
                if (++steps >= loopLength)
                    return ContactSide.Ambiguous;
            }

            // Anything else touching this point (another loop, a spike, a self-crossing)
            // makes the local material side ambiguous.
            for (var i = 0; i < entities.Count; i++)
            {
                if (InRun(i, incoming, outgoing))
                    continue;
                if (DistanceTo(entities[i], point) <= IncidenceTolerance)
                    return ContactSide.Ambiguous;
            }

            var interior = incoming == best && outgoing == best && !EndsNear(best, point);
            var inTangent = interior ? TangentAt(entities[best], point) : EndTangent(entities[incoming]);
            var outTangent = interior
                ? inTangent
                : StartTangent(entities[outgoing]);

            // A circle has no endpoints, so its point is always interior.
            if (smoothLoop)
                inTangent = outTangent = TangentAt(entities[best], point);

            if (IsZero(inTangent) || IsZero(outTangent))
                return ContactSide.Ambiguous;

            var outAngle = System.Math.Atan2(outTangent.Y, outTangent.X);
            var inAngle = System.Math.Atan2(-inTangent.Y, -inTangent.X);
            var left = materialLeft[loopOf[best]];

            start = left ? outAngle : inAngle;
            width = Angle.NormalizeRad((left ? inAngle : outAngle) - start);

            startConcave = IsConcave(entities[left ? outgoing : incoming], left);
            endConcave = IsConcave(entities[left ? incoming : outgoing], left);

            return
                width > SlideContact.AngleTolerance
                && width < Angle.TwoPI - 2 * SlideContact.SplitOverlap
                ? ContactSide.Sector
                : ContactSide.Ambiguous;
        }

        private int LoopLength(int index)
        {
            var length = 1;
            for (var i = following[index]; i != index; i = following[i])
                length++;
            return length;
        }

        private bool StartsNear(int index, Vector point) =>
            TryEndpoints(entities[index], out var start, out _)
            && start.DistanceTo(point) <= IncidenceTolerance;

        private bool EndsNear(int index, Vector point) =>
            TryEndpoints(entities[index], out _, out var end)
            && end.DistanceTo(point) <= IncidenceTolerance;

        private bool InRun(int index, int first, int last)
        {
            for (var i = first; ; i = following[i])
            {
                if (i == index)
                    return true;
                if (i == last)
                    return false;
            }
        }

        private static bool IsZero(Vector v) => v.X == 0 && v.Y == 0;

        private static bool IsConcave(Entity entity, bool materialLeft)
        {
            // A CCW curve has its center on its left; that center is on the free side
            // (a concave boundary) exactly when material is on the right.
            return entity switch
            {
                Arc arc => materialLeft == arc.IsReversed,
                Circle circle => materialLeft == (circle.Rotation == RotationType.CW),
                _ => false,
            };
        }

        private static Vector StartTangent(Entity entity) =>
            entity switch
            {
                Line line => Direction(line.pt1, line.pt2),
                Arc arc => ArcTangent(arc.StartAngle, arc.IsReversed),
                _ => new Vector(),
            };

        private static Vector EndTangent(Entity entity) =>
            entity switch
            {
                Line line => Direction(line.pt1, line.pt2),
                Arc arc => ArcTangent(arc.EndAngle, arc.IsReversed),
                _ => new Vector(),
            };

        private static Vector TangentAt(Entity entity, Vector point) =>
            entity switch
            {
                Line line => Direction(line.pt1, line.pt2),
                Arc arc => ArcTangent(arc.Center.AngleTo(point), arc.IsReversed),
                Circle circle => ArcTangent(
                    circle.Center.AngleTo(point),
                    circle.Rotation == RotationType.CW
                ),
                _ => new Vector(),
            };

        private static Vector ArcTangent(double angle, bool clockwise)
        {
            var sign = clockwise ? -1.0 : 1.0;
            return new Vector(-System.Math.Sin(angle) * sign, System.Math.Cos(angle) * sign);
        }

        private static Vector Direction(Vector from, Vector to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var length = System.Math.Sqrt(dx * dx + dy * dy);
            return length > 0 ? new Vector(dx / length, dy / length) : new Vector();
        }

        private static double DistanceTo(Entity entity, Vector point)
        {
            switch (entity)
            {
                case Line line:
                    return point.DistanceTo(line.ClosestPointTo(point));
                case Arc arc:
                    {
                        var angle = arc.Center.AngleTo(point);
                        if (Angle.IsBetweenRad(angle, arc.StartAngle, arc.EndAngle, arc.IsReversed))
                            return System.Math.Abs(arc.Center.DistanceTo(point) - arc.Radius);
                        return System.Math.Min(
                            point.DistanceTo(arc.StartPoint()),
                            point.DistanceTo(arc.EndPoint())
                        );
                    }
                case Circle circle:
                    return System.Math.Abs(circle.Center.DistanceTo(point) - circle.Radius);
                default:
                    return double.MaxValue;
            }
        }

        private static bool TryEndpoints(Entity entity, out Vector start, out Vector end)
        {
            switch (entity)
            {
                case Line line:
                    start = line.pt1;
                    end = line.pt2;
                    return true;
                case Arc arc:
                    start = arc.StartPoint();
                    end = arc.EndPoint();
                    return true;
                default:
                    start = end = new Vector();
                    return false;
            }
        }

        private static bool Near(Vector a, Vector b) => a.DistanceTo(b) <= IncidenceTolerance;

        private static double SignedArea(List<Entity> entities, int first, int last)
        {
            var area = 0.0;
            for (var i = first; i <= last; i++)
            {
                switch (entities[i])
                {
                    case Circle circle:
                        var sign = circle.Rotation == RotationType.CW ? -1 : 1;
                        area += sign * System.Math.PI * circle.Radius * circle.Radius;
                        break;
                    case Line line:
                        area += Cross(line.pt1, line.pt2) / 2;
                        break;
                    case Arc arc:
                        var sweep = arc.IsReversed ? -arc.SweepAngle() : arc.SweepAngle();
                        var r = arc.Radius;
                        area += Cross(arc.StartPoint(), arc.EndPoint()) / 2;
                        area += r * r / 2 * (sweep - System.Math.Sin(sweep));
                        break;
                }
            }
            return area;
        }

        private static double Cross(Vector a, Vector b) => a.X * b.Y - b.X * a.Y;

        private static Vector SamplePoint(Entity entity) =>
            entity switch
            {
                Circle circle => new Vector(circle.Center.X + circle.Radius, circle.Center.Y),
                Arc arc => arc.StartPoint(),
                Line line => line.pt1,
                _ => new Vector(),
            };

        // Exact horizontal-ray parity. Split arcs at Y extrema so every piece is
        // monotone; the same half-open endpoint rule as lines avoids seam double counts.
        // A coarse inscribed polygon can misclassify thin rings as solid material.
        private static bool Contains(List<Entity> entities, int first, int last, Vector point)
        {
            var inside = false;
            for (var i = first; i <= last; i++)
            {
                if (entities[i] is Circle circle)
                    return circle.Center.DistanceTo(point) < circle.Radius;
                if (entities[i] is Line line)
                {
                    var a = line.pt1;
                    var b = line.pt2;
                    if ((a.Y > point.Y) != (b.Y > point.Y)
                        && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
                else if (entities[i] is Arc arc)
                {
                    var sweep = arc.SweepAngle();
                    var sign = arc.IsReversed ? -1.0 : 1.0;
                    var cuts = new List<double> { 0, sweep };
                    foreach (var extreme in new[] { Angle.HalfPI, 3 * Angle.HalfPI })
                    {
                        var t = Angle.NormalizeRad(sign * (extreme - arc.StartAngle));
                        if (t > 0 && t < sweep)
                            cuts.Add(t);
                    }
                    cuts.Sort();
                    for (var k = 1; k < cuts.Count; k++)
                    {
                        var a = arc.StartAngle + sign * cuts[k - 1];
                        var b = arc.StartAngle + sign * cuts[k];
                        var y1 = arc.Center.Y + arc.Radius * System.Math.Sin(a);
                        var y2 = arc.Center.Y + arc.Radius * System.Math.Sin(b);
                        if ((y1 > point.Y) == (y2 > point.Y))
                            continue;
                        var dy = point.Y - arc.Center.Y;
                        var dx = System.Math.Sqrt(System.Math.Max(0, arc.Radius * arc.Radius - dy * dy));
                        var x = arc.Center.X + (System.Math.Cos((a + b) / 2) >= 0 ? dx : -dx);
                        if (point.X < x)
                            inside = !inside;
                    }
                }
            }
            return inside;
        }
    }

    /// <summary>
    /// Contact classifier for one moving/stationary pair of boundaries. Geometry is prepared
    /// on first use, so a slide whose nearest contact is never classified pays nothing; call
    /// <see cref="Prepare"/> before sharing one instance across threads. Each boundary is
    /// given in its own frame; the origins place those frames in the world coordinates used
    /// by slide events.
    /// </summary>
    public sealed class SlideContactClassifier
    {
        private readonly System.Func<List<Entity>> movingSource;
        private readonly System.Func<List<Entity>> stationarySource;
        private SlideContactGeometry moving;
        private SlideContactGeometry stationary;

        public SlideContactClassifier(List<Entity> movingEntities, List<Entity> stationaryEntities)
            : this(movingEntities, Vector.Zero, stationaryEntities, Vector.Zero) { }

        public SlideContactClassifier(
            List<Entity> movingEntities,
            Vector movingOrigin,
            List<Entity> stationaryEntities,
            Vector stationaryOrigin
        )
            : this(() => movingEntities, movingOrigin, () => stationaryEntities, stationaryOrigin)
        { }

        public SlideContactClassifier(
            SlideContactGeometry moving,
            Vector movingOrigin,
            SlideContactGeometry stationary,
            Vector stationaryOrigin
        )
        {
            this.moving = moving;
            this.stationary = stationary;
            MovingOrigin = movingOrigin;
            StationaryOrigin = stationaryOrigin;
        }

        private SlideContactClassifier(
            System.Func<List<Entity>> movingSource,
            Vector movingOrigin,
            System.Func<List<Entity>> stationarySource,
            Vector stationaryOrigin
        )
        {
            this.movingSource = movingSource;
            this.stationarySource = stationarySource;
            MovingOrigin = movingOrigin;
            StationaryOrigin = stationaryOrigin;
        }

        public Vector MovingOrigin { get; }

        public Vector StationaryOrigin { get; }

        public static SlideContactClassifier FromLines(
            List<Line> movingLines,
            Vector movingOrigin,
            List<Line> stationaryLines,
            Vector stationaryOrigin
        ) =>
            new SlideContactClassifier(
                () => new List<Entity>(movingLines),
                movingOrigin,
                () => new List<Entity>(stationaryLines),
                stationaryOrigin
            );

        public static SlideContactClassifier FromEdges(
            (Vector start, Vector end)[] movingEdges,
            Vector movingOrigin,
            (Vector start, Vector end)[] stationaryEdges,
            Vector stationaryOrigin
        )
        {
            // The kernel sorts edge arrays in place, so snapshot the chain order now.
            var moving = ((Vector start, Vector end)[])movingEdges.Clone();
            var stationary = ((Vector start, Vector end)[])stationaryEdges.Clone();
            return new SlideContactClassifier(
                () => ToLines(moving),
                movingOrigin,
                () => ToLines(stationary),
                stationaryOrigin
            );
        }

        private static List<Entity> ToLines((Vector start, Vector end)[] edges)
        {
            var lines = new List<Entity>(edges.Length);
            foreach (var (start, end) in edges)
                lines.Add(new Line(start, end));
            // Public edge arrays are sorted in place by previous queries. Recover their
            // chains on private line objects; never reverse or reorder caller geometry.
            var ordered = new List<Entity>(lines.Count);
            foreach (var shape in ShapeBuilder.GetShapes(lines))
                ordered.AddRange(shape.Entities);
            return ordered;
        }

        public SlideContactClassifier Prepare()
        {
            moving ??= SlideContactGeometry.Prepare(movingSource?.Invoke() ?? new List<Entity>());
            stationary ??= SlideContactGeometry.Prepare(
                stationarySource?.Invoke() ?? new List<Entity>()
            );
            return this;
        }

        /// <summary>The same prepared boundaries placed at other origins.</summary>
        public SlideContactClassifier At(Vector movingOrigin, Vector stationaryOrigin)
        {
            Prepare();
            return new SlideContactClassifier(moving, movingOrigin, stationary, stationaryOrigin);
        }

        /// <summary>
        /// True when moving along (dirX, dirY) from this world-space contact would push
        /// material into material, or the contact cannot be classified.
        /// </summary>
        public bool Blocks(Vector movingPoint, Vector stationaryPoint, double dirX, double dirY)
        {
            Prepare();
            return SlideContact.Blocks(
                moving,
                movingPoint - MovingOrigin,
                stationary,
                stationaryPoint - StationaryOrigin,
                dirX,
                dirY
            );
        }
    }

    /// <summary>Receives candidate contact events from a directional slide query.</summary>
    public interface ISlideEventSink
    {
        /// <summary>True once further events cannot change this sink's result.</summary>
        bool IsDone { get; }

        /// <param name="distance">Travel to the contact, snapped to zero within Tolerance.Epsilon.</param>
        /// <param name="movingPoint">Contact on the moving boundary, at its start position.</param>
        /// <param name="stationaryPoint">Contact on the stationary boundary.</param>
        void Add(double distance, Vector movingPoint, Vector stationaryPoint);
    }

    /// <summary>
    /// Enumerates every candidate contact of one slide. Must yield the same events each
    /// time it is enumerated.
    /// </summary>
    public interface ISlideEventSource
    {
        void Enumerate<TSink>(ref TSink sink)
            where TSink : struct, ISlideEventSink;
    }

    /// <summary>Keeps the nearest event; stops at a contact that is already touching.</summary>
    public struct NearestSlideEvent : ISlideEventSink
    {
        public bool Found;
        public double Distance;
        public Vector MovingPoint;
        public Vector StationaryPoint;

        public bool IsDone => Found && Distance <= 0;

        public void Add(double distance, Vector movingPoint, Vector stationaryPoint)
        {
            if (Found && distance >= Distance)
                return;

            Found = true;
            Distance = distance;
            MovingPoint = movingPoint;
            StationaryPoint = stationaryPoint;
        }
    }

    internal struct SlideEventList : ISlideEventSink
    {
        public List<(double Distance, Vector MovingPoint, Vector StationaryPoint)> Events;

        public bool IsDone => false;

        public void Add(double distance, Vector movingPoint, Vector stationaryPoint) =>
            Events.Add((distance, movingPoint, stationaryPoint));
    }

    public static class SlideResolver
    {
        /// <summary>
        /// Travel to the first contact that blocks the slide, or double.MaxValue. When the
        /// nearest contact blocks (every contact, for unresolved boundaries), the result is
        /// exactly the nearest event distance and the events are enumerated once.
        /// </summary>
        public static double FirstBlocking<TSource>(
            ref TSource source,
            SlideContactClassifier contacts,
            double dirX,
            double dirY
        )
            where TSource : struct, ISlideEventSource
        {
            var nearest = new NearestSlideEvent();
            source.Enumerate(ref nearest);

            if (!nearest.Found)
                return double.MaxValue;

            if (contacts.Blocks(nearest.MovingPoint, nearest.StationaryPoint, dirX, dirY))
                return nearest.Distance;

            var all = new SlideEventList
            {
                Events = new List<(double, Vector, Vector)>(),
            };
            source.Enumerate(ref all);
            all.Events.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            foreach (var (distance, movingPoint, stationaryPoint) in all.Events)
            {
                if (contacts.Blocks(movingPoint, stationaryPoint, dirX, dirY))
                    return distance;
            }

            return double.MaxValue;
        }
    }

    /// <summary>
    /// Decides whether a first-contact event found by a directional slide stops the slide.
    /// </summary>
    /// <remarks>
    /// Parts that already touch may slide along each other or apart. Only a direction that
    /// would create positive-area overlap blocks: with S the stationary material sector and
    /// M the moving one at the contact point, that is the open Minkowski cone S ⊕ −M.
    /// A direction on that cone's boundary is a tangential slide; it blocks only when an
    /// incident curve is concave, because the second-order bend then closes the gap.
    /// Unresolved or ambiguous topology blocks, which is the previous behavior for every
    /// contact.
    /// </remarks>
    public static class SlideContact
    {
        internal const double AngleTolerance = 1e-7;

        // Reflex sectors are split into two overlapping convex halves; the overlap keeps
        // the split ray in the interior of the union.
        internal const double SplitOverlap = 1e-3;

        /// <summary>
        /// True when moving along (dirX, dirY) from this contact would push material into
        /// material, or when the contact cannot be classified. False for a near-miss whose
        /// point is not on both boundaries.
        /// </summary>
        /// <param name="movingPoint">Contact point in the moving entities' own frame.</param>
        /// <param name="stationaryPoint">The same contact in the stationary frame.</param>
        public static bool Blocks(
            SlideContactGeometry moving,
            Vector movingPoint,
            SlideContactGeometry stationary,
            Vector stationaryPoint,
            double dirX,
            double dirY
        )
        {
            if (moving == null || stationary == null)
                return true;

            var stationarySide = stationary.GetMaterialSector(
                stationaryPoint,
                out var stationaryStart,
                out var stationaryWidth,
                out var stationaryStartConcave,
                out var stationaryEndConcave
            );
            var movingSide = moving.GetMaterialSector(
                movingPoint,
                out var movingStart,
                out var movingWidth,
                out var movingStartConcave,
                out var movingEndConcave
            );

            if (stationarySide == ContactSide.Unresolved || movingSide == ContactSide.Unresolved)
                return true;

            // Ray tolerances report hits slightly beyond an entity's end; such a point is
            // not on the other boundary, so the parts pass without touching there.
            if (stationarySide == ContactSide.Off || movingSide == ContactSide.Off)
                return false;

            if (stationarySide == ContactSide.Ambiguous || movingSide == ContactSide.Ambiguous)
                return true;

            var direction = System.Math.Atan2(dirY, dirX);
            var stationaryPieces = Split(stationaryStart, stationaryWidth);
            var movingPieces = Split(movingStart + System.Math.PI, movingWidth);
            var onBoundary = false;

            foreach (var s in stationaryPieces)
            {
                foreach (var m in movingPieces)
                {
                    if (!TryHull(s, m, out var hullStart, out var hullWidth))
                        return true;

                    var offset = Angle.NormalizeRad(direction - hullStart);
                    if (offset > AngleTolerance && offset < hullWidth - AngleTolerance)
                        return true;

                    if (
                        offset <= AngleTolerance
                        || offset >= Angle.TwoPI - AngleTolerance
                        || System.Math.Abs(offset - hullWidth) <= AngleTolerance
                    )
                        onBoundary = true;
                }
            }

            return onBoundary && (
                stationaryStartConcave && SameRay(direction, stationaryStart)
                || stationaryEndConcave && SameRay(direction, stationaryStart + stationaryWidth)
                || movingStartConcave && SameRay(direction, movingStart + System.Math.PI)
                || movingEndConcave && SameRay(direction, movingStart + movingWidth + System.Math.PI));
        }

        private static bool SameRay(double a, double b)
        {
            var offset = Angle.NormalizeRad(a - b);
            return offset <= AngleTolerance || offset >= Angle.TwoPI - AngleTolerance;
        }

        private static (double Start, double Width)[] Split(double start, double width)
        {
            if (width <= System.Math.PI + AngleTolerance)
                return new[] { (start, width) };

            var half = width / 2;
            return new[]
            {
                (start, half + SplitOverlap),
                (start + half - SplitOverlap, half + SplitOverlap),
            };
        }

        /// <summary>
        /// Convex cone generated by two convex sectors. False when it is the whole plane.
        /// </summary>
        private static bool TryHull(
            (double Start, double Width) a,
            (double Start, double Width) b,
            out double start,
            out double width
        )
        {
            var fromA = System.Math.Max(a.Width, Angle.NormalizeRad(b.Start - a.Start) + b.Width);
            var fromB = System.Math.Max(b.Width, Angle.NormalizeRad(a.Start - b.Start) + a.Width);

            if (fromA <= fromB)
            {
                start = a.Start;
                width = fromA;
            }
            else
            {
                start = b.Start;
                width = fromB;
            }

            return width <= System.Math.PI + AngleTolerance;
        }
    }
}
