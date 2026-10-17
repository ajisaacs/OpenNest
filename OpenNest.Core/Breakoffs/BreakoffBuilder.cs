using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Breakoffs
{
    /// <summary>
    /// Resolves and applies <see cref="Breakoff"/>s. The break line is cut as closed slots on the
    /// breakoff side rather than as open cuts, because the cutting planner only prepares closed
    /// contours: each slot's pierce and lead-in fall inside the slot, which is breakoff scrap.
    /// </summary>
    public static class BreakoffBuilder
    {
        /// <summary>How close the breakoff's ends must be to the outline.</summary>
        public const double JoinTolerance = 0.001;

        private const double ArcTolerance = 0.0005;

        /// <summary>
        /// <paramref name="entities"/> with every valid breakoff merged into its outline and its
        /// break line slotted. Inputs are not modified; generated entities are new. Breakoffs that
        /// cannot be resolved are skipped and described in <paramref name="errors"/>.
        /// </summary>
        public static List<Entity> Apply(IReadOnlyList<Entity> entities, IReadOnlyList<Breakoff> breakoffs,
            out List<string> errors)
        {
            errors = new List<string>();
            var working = entities.ToList();
            if (breakoffs == null || breakoffs.Count == 0)
                return working;

            var allEdges = breakoffs.SelectMany(b => b.EdgeIds).ToHashSet();
            for (var i = 0; i < breakoffs.Count; i++)
            {
                var layout = Layout(working, breakoffs[i], allEdges);
                if (!layout.IsValid)
                {
                    errors.Add($"Breakoff {i + 1}: {layout.Error}");
                    continue;
                }

                var replaced = new HashSet<Entity>(layout.ReplacedEntities, ReferenceEqualityComparer.Instance);
                working.RemoveAll(replaced.Contains);
                // Fresh IDs: the outline holds copies of this breakoff's edges, which later
                // breakoffs exclude by ID when they look for the outline.
                working.AddRange(layout.Outline.Select(e => e.Clone()));
                working.AddRange(layout.Slots.SelectMany(s => s).Select(e => e.Clone()));
            }
            return working;
        }

        /// <summary>
        /// Resolves <paramref name="breakoff"/> against <paramref name="entities"/> (the entities that
        /// will be cut). <paramref name="otherEdgeIds"/> are edges of other breakoffs on the same
        /// drawing, which are ignored while finding the outline.
        /// </summary>
        public static BreakoffLayout Layout(IReadOnlyList<Entity> entities, Breakoff breakoff,
            IEnumerable<Guid> otherEdgeIds = null)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(breakoff);
            if (!(breakoff.TabWidth > 0) || !double.IsFinite(breakoff.TabWidth)
                || !(breakoff.SlotWidth > 0) || !double.IsFinite(breakoff.SlotWidth))
                return Fail("Tab and slot widths must be greater than zero.");

            var edgeIds = breakoff.EdgeIds.ToHashSet();
            var edgeSources = entities.Where(e => edgeIds.Contains(e.Id)).ToList();
            if (edgeIds.Count == 0 || edgeSources.Count != edgeIds.Count)
                return Fail("Some of its edges are hidden or missing.");

            var edges = BreakoffPath.ChainOpen(edgeSources, JoinTolerance);
            if (edges == null)
                return Fail("Its edges must form one open chain.");

            // Measure tabs from a fixed end so positions survive re-chaining.
            var a = BreakoffPath.Start(edges[0]);
            var b = BreakoffPath.End(edges[^1]);
            if (b.X < a.X - JoinTolerance || (System.Math.Abs(b.X - a.X) <= JoinTolerance && b.Y < a.Y))
            {
                edges = BreakoffPath.Reversed(edges);
                (a, b) = (b, a);
            }

            var excluded = new HashSet<Guid>(otherEdgeIds ?? Enumerable.Empty<Guid>());
            excluded.UnionWith(edgeIds);
            var sources = new Dictionary<Entity, Entity>(ReferenceEqualityComparer.Instance);
            var candidates = new List<Entity>();
            foreach (var entity in entities)
            {
                if (excluded.Contains(entity.Id) || IsMark(entity) || entity is not (Line or Arc))
                    continue;
                var copy = BreakoffPath.Copy(entity);
                sources[copy] = entity;
                candidates.Add(copy);
            }

            Shape outline = null;
            var outlineArea = 0.0;
            foreach (var shape in ShapeBuilder.GetShapes(candidates))
            {
                if (shape.Entities.Count < 2 || !shape.IsClosed()
                    || shape.ClosestPointTo(a).DistanceTo(a) > JoinTolerance
                    || shape.ClosestPointTo(b).DistanceTo(b) > JoinTolerance)
                    continue;
                var area = System.Math.Abs(BreakoffPath.SignedArea(BreakoffPath.ToPoints(shape.Entities, ArcTolerance)));
                if (outline == null || area > outlineArea)
                {
                    outline = shape;
                    outlineArea = area;
                }
            }
            if (outline == null)
                return Fail("Both ends must touch a closed outline.");

            var loop = outline.Entities;
            var total = BreakoffPath.Length(loop);
            var atA = BreakoffPath.DistanceOf(loop, a);
            var rotated = BreakoffPath.Sub(loop, atA, total).Concat(BreakoffPath.Sub(loop, 0, atA)).ToList();
            var atB = BreakoffPath.DistanceOf(rotated, b);
            if (atB <= JoinTolerance || atB >= total - JoinTolerance)
                return Fail("Its ends must touch the outline at two different points.");
            var toB = BreakoffPath.Sub(rotated, 0, atB);
            var backToA = BreakoffPath.Sub(rotated, atB, total);
            // The planner closes contours far tighter than the join tolerance, so the edges must
            // end exactly where the outline was split.
            edges = SnapEnds(edges, BreakoffPath.Start(toB[0]), BreakoffPath.Start(backToA[0]));

            var viaBack = edges.Concat(backToA).ToList();
            var viaTo = edges.Concat(BreakoffPath.Reversed(toB)).ToList();
            var viaBackArea = System.Math.Abs(BreakoffPath.SignedArea(BreakoffPath.ToPoints(viaBack, ArcTolerance)));
            var viaToArea = System.Math.Abs(BreakoffPath.SignedArea(BreakoffPath.ToPoints(viaTo, ArcTolerance)));

            // The breakoff region is the smaller loop; the merged outline is the other one.
            List<Entity> region, merged, breakLine;
            double regionArea, mergedArea;
            if (viaBackArea <= viaToArea)
            {
                (region, regionArea, merged, mergedArea) = (viaBack, viaBackArea, viaTo, viaToArea);
                breakLine = BreakoffPath.Reversed(backToA);
            }
            else
            {
                (region, regionArea, merged, mergedArea) = (viaTo, viaToArea, viaBack, viaBackArea);
                breakLine = toB;
            }

            if (System.Math.Abs(mergedArea - (outlineArea + regionArea)) > 0.01 * mergedArea)
                return Fail("It must sit outside the part outline.");

            var length = BreakoffPath.Length(breakLine);
            var tabWidth = breakoff.TabWidth;
            var centers = breakoff.TabCenters.Count == 0
                ? EvenTabCenters(length, Breakoff.DefaultTabCount, tabWidth)
                : breakoff.TabCenters.OrderBy(c => c).ToList();
            if (centers.Count < Breakoff.MinTabCount || length <= tabWidth * centers.Count)
                return Fail("The break line is too short for its tabs.");
            if (centers[0] < tabWidth / 2 - JoinTolerance || centers[^1] > length - tabWidth / 2 + JoinTolerance)
                return Fail("A tab runs off the break line; space the tabs evenly again.");
            for (var i = 1; i < centers.Count; i++)
                if (centers[i] - centers[i - 1] <= tabWidth + JoinTolerance)
                    return Fail("Two tabs overlap; space the tabs evenly again.");

            var regionPoints = BreakoffPath.ToPoints(region, ArcTolerance);
            var side = SideOf(breakLine, length, regionPoints, System.Math.Min(breakoff.SlotWidth, length) * 0.05);
            if (side == 0)
                return Fail("Could not tell which side of the break line the breakoff is on.");

            var slots = new List<IReadOnlyList<Entity>>();
            for (var i = 0; i + 1 < centers.Count; i++)
            {
                var slot = BuildSlot(breakLine, centers[i] + tabWidth / 2, centers[i + 1] - tabWidth / 2,
                    side, breakoff.SlotWidth, tabWidth, edges, regionPoints);
                if (slot == null)
                    return Fail($"There is no room for slot {i + 1} inside the breakoff; "
                        + "use a narrower slot or move the tabs.");
                slots.Add(slot);
            }

            var replaced = outline.Entities.Select(e => sources[e]).ToList();
            replaced.AddRange(edgeSources);
            return new BreakoffLayout
            {
                BreakLine = breakLine,
                Length = length,
                Outline = merged,
                Slots = slots,
                TabCenters = centers,
                TabWidth = tabWidth,
                ReplacedEntities = replaced,
            };
        }

        /// <summary><paramref name="count"/> tab centers spaced evenly with the first and last at the ends.</summary>
        public static List<double> EvenTabCenters(double length, int count, double tabWidth)
        {
            count = System.Math.Max(count, Breakoff.MinTabCount);
            var half = tabWidth / 2;
            var span = length - tabWidth;
            return Enumerable.Range(0, count).Select(i => half + span * i / (count - 1)).ToList();
        }

        /// <summary>
        /// The open chain of visible lines and arcs through <paramref name="picked"/>, grown until it
        /// reaches a point where it does not continue as a single path (such as where it meets an
        /// outline). Empty when <paramref name="picked"/> cannot be a breakoff edge.
        /// </summary>
        public static List<Entity> FindEdgeChain(IReadOnlyList<Entity> entities, Entity picked)
        {
            var pool = entities.Where(e => e is Line or Arc && e.IsVisible && e.Layer?.IsVisible != false && !IsMark(e))
                .ToList();
            if (!pool.Contains(picked))
                return new List<Entity>();

            var chain = new List<Entity> { picked };
            Grow(BreakoffPath.End(picked));
            Grow(BreakoffPath.Start(picked));
            return chain;

            void Grow(Vector tip)
            {
                while (true)
                {
                    var touching = pool.Where(e => !chain.Contains(e)
                        && (BreakoffPath.Start(e).DistanceTo(tip) <= JoinTolerance
                            || BreakoffPath.End(e).DistanceTo(tip) <= JoinTolerance)).ToList();
                    if (touching.Count != 1)
                        return;
                    var next = touching[0];
                    chain.Add(next);
                    tip = BreakoffPath.Start(next).DistanceTo(tip) <= JoinTolerance
                        ? BreakoffPath.End(next)
                        : BreakoffPath.Start(next);
                }
            }
        }

        private static BreakoffLayout Fail(string error) => new(error);

        private static List<Entity> SnapEnds(List<Entity> edges, Vector start, Vector end)
        {
            var result = new List<Entity>(edges);
            var first = result[0];
            if (BreakoffPath.Start(first).DistanceTo(start) > 0)
            {
                if (first is Line line)
                    line.StartPoint = start;
                else
                    result.Insert(0, Joint(first, start, BreakoffPath.Start(first)));
            }
            var last = result[^1];
            if (BreakoffPath.End(last).DistanceTo(end) > 0)
            {
                if (last is Line line)
                    line.EndPoint = end;
                else
                    result.Add(Joint(last, BreakoffPath.End(last), end));
            }
            return result;
        }

        private static Line Joint(Entity style, Vector from, Vector to) =>
            new(from, to) { Layer = style.Layer, Color = style.Color, LineTypeName = style.LineTypeName };

        private static bool IsMark(Entity e) =>
            string.Equals(e.Layer?.Name, "ETCH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Layer?.Name, SpecialLayers.Scribe.Name, StringComparison.OrdinalIgnoreCase);

        private static Vector Normal(IReadOnlyList<Entity> path, double length, double distance)
        {
            const double nudge = 1e-6;
            var before = BreakoffPath.TangentAt(path, System.Math.Max(distance - nudge, 0));
            var after = BreakoffPath.TangentAt(path, System.Math.Min(distance + nudge, length));
            var t = new Vector(before.X + after.X, before.Y + after.Y);
            var size = System.Math.Sqrt(t.X * t.X + t.Y * t.Y);
            if (size <= Tolerance.Epsilon)
                t = after;
            else
                t = new Vector(t.X / size, t.Y / size);
            return new Vector(-t.Y, t.X);
        }

        // +1 when the breakoff lies left of the break line's travel, -1 when right, 0 when unclear.
        private static int SideOf(IReadOnlyList<Entity> breakLine, double length, IReadOnlyList<Vector> region, double probe)
        {
            var votes = 0;
            foreach (var fraction in new[] { 0.25, 0.5, 0.75 })
            {
                var d = length * fraction;
                var p = BreakoffPath.PointAt(breakLine, d);
                var n = Normal(breakLine, length, d);
                var left = BreakoffPath.Contains(region, p + n * probe);
                var right = BreakoffPath.Contains(region, p - n * probe);
                if (left != right)
                    votes += left ? 1 : -1;
            }
            return System.Math.Sign(votes);
        }

        // The slot's part side is the exact break line from 'from' to 'to'; its scrap side follows
        // the break line 'depth' into the breakoff, pulled back near the breakoff's edges so at least
        // 'clearance' of breakoff remains beyond it. Its ends move inward where there is no room.
        private static List<Entity> BuildSlot(IReadOnlyList<Entity> breakLine, double from, double to, int side,
            double depth, double clearance, IReadOnlyList<Entity> edges, IReadOnlyList<Vector> region)
        {
            var length = BreakoffPath.Length(breakLine);
            var minDepth = depth / 4;
            var step = System.Math.Max(depth / 8, (to - from) / 400);

            double Room(double d)
            {
                var p = BreakoffPath.PointAt(breakLine, d);
                var dir = Normal(breakLine, length, d) * side;
                var reach = depth + clearance;
                var ray = new Line(p, p + dir * reach);
                var nearest = reach;
                foreach (var edge in edges)
                {
                    if (!edge.Intersects(ray, out var hits))
                        continue;
                    foreach (var hit in hits)
                    {
                        var gap = hit.DistanceTo(p);
                        if (gap > Tolerance.Epsilon)
                            nearest = System.Math.Min(nearest, gap);
                    }
                }
                return System.Math.Min(depth, nearest - clearance);
            }

            var start = from;
            while (start < to && Room(start) < minDepth)
                start += step;
            var end = to;
            while (end > start && Room(end) < minDepth)
                end -= step;
            if (end - start < minDepth)
                return null;

            var stations = new SortedSet<double> { start, end };
            for (var d = start + depth / 4; d < end; d += depth / 4)
                stations.Add(d);
            var walked = 0.0;
            foreach (var e in breakLine)
            {
                walked += e.Length;
                if (walked > start && walked < end)
                    stations.Add(walked);
            }

            var far = new List<Vector>();
            foreach (var d in stations)
            {
                var room = Room(d);
                if (room < minDepth / 2)
                    return null;
                var point = BreakoffPath.PointAt(breakLine, d) + Normal(breakLine, length, d) * side * room;
                if (!BreakoffPath.Contains(region, point))
                    return null;
                far.Add(point);
            }
            far = DropCollinear(far);

            var near = BreakoffPath.Sub(breakLine, start, end);
            var style = near[0];
            var slot = new List<Entity>(near);
            var path = new List<Vector> { BreakoffPath.End(near[^1]) };
            for (var i = far.Count - 1; i >= 0; i--)
                path.Add(far[i]);
            path.Add(BreakoffPath.Start(near[0]));
            for (var i = 1; i < path.Count; i++)
            {
                if (path[i].DistanceTo(path[i - 1]) <= Tolerance.Epsilon)
                    continue;
                slot.Add(new Line(path[i - 1], path[i])
                {
                    Layer = style.Layer,
                    Color = style.Color,
                    LineTypeName = style.LineTypeName,
                });
            }
            return slot;
        }

        private static List<Vector> DropCollinear(List<Vector> points)
        {
            if (points.Count < 3)
                return points;
            var result = new List<Vector> { points[0] };
            for (var i = 1; i < points.Count - 1; i++)
            {
                var prev = result[^1];
                var u = points[i] - prev;
                var v = points[i + 1] - points[i];
                var cross = u.X * v.Y - u.Y * v.X;
                var scale = System.Math.Sqrt((u.X * u.X + u.Y * u.Y) * (v.X * v.X + v.Y * v.Y));
                var dot = u.X * v.X + u.Y * v.Y;
                if (System.Math.Abs(cross) <= 1e-9 * scale && dot > 0)
                    continue;
                result.Add(points[i]);
            }
            result.Add(points[^1]);
            return result;
        }
    }
}
