using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Breakoffs
{
    /// <summary>
    /// Resolves and applies <see cref="Breakoff"/>s: the joining line becomes open cuts on the
    /// break layer between tabs, each traveling with the scrap on its left so the cutting
    /// strategies pierce and lead in from the scrap.
    /// </summary>
    public static class BreakoffBuilder
    {
        /// <summary>How close the joining line's ends must be to the outline.</summary>
        public const double JoinTolerance = 0.001;

        private const double ArcTolerance = 0.0005;

        /// <summary>
        /// <paramref name="entities"/> with every valid breakoff applied: its joining line replaced
        /// by break cuts and its outline's joints made exact. Inputs are not modified; generated
        /// entities are new. Breakoffs that cannot be resolved are skipped and described in
        /// <paramref name="errors"/>.
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
                working.AddRange(layout.Outline.Select(e => e.Clone()));
                working.AddRange(layout.Cuts.SelectMany(c => c).Select(e => e.Clone()));
            }
            return working;
        }

        /// <summary>
        /// Resolves <paramref name="breakoff"/> against <paramref name="entities"/> (the entities that
        /// will be cut). <paramref name="otherEdgeIds"/> are joining lines of other breakoffs on the
        /// same drawing, which are ignored while finding the outline.
        /// </summary>
        public static BreakoffLayout Layout(IReadOnlyList<Entity> entities, Breakoff breakoff,
            IEnumerable<Guid> otherEdgeIds = null)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(breakoff);
            if (!(breakoff.TabWidth > 0) || !double.IsFinite(breakoff.TabWidth))
                return Fail("The tab width must be greater than zero.");

            var edgeIds = breakoff.EdgeIds.ToHashSet();
            var edgeSources = entities.Where(e => edgeIds.Contains(e.Id)).ToList();
            if (edgeIds.Count == 0 || edgeSources.Count != edgeIds.Count)
                return Fail("Its joining line is hidden or missing.");

            var line = BreakoffPath.ChainOpen(edgeSources, JoinTolerance);
            if (line == null)
                return Fail("Its joining line must be one open chain.");

            // Measure tabs from a fixed end so positions survive re-chaining.
            var a = BreakoffPath.Start(line[0]);
            var b = BreakoffPath.End(line[^1]);
            if (b.X < a.X - JoinTolerance || (System.Math.Abs(b.X - a.X) <= JoinTolerance && b.Y < a.Y))
            {
                line = BreakoffPath.Reversed(line);
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
            foreach (var shape in ShapeBuilder.GetShapes(WithoutDanglingEnds(candidates)))
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
                return Fail("Both ends of the joining line must touch a closed outline.");

            var loop = CloseExactly(outline.Entities);
            var total = BreakoffPath.Length(loop);
            var atA = BreakoffPath.DistanceOf(loop, a);
            var rotated = BreakoffPath.Sub(loop, atA, total).Concat(BreakoffPath.Sub(loop, 0, atA)).ToList();
            var atB = BreakoffPath.DistanceOf(rotated, b);
            if (atB <= JoinTolerance || atB >= total - JoinTolerance)
                return Fail("The joining line must touch the outline at two different points.");
            var toB = BreakoffPath.Sub(rotated, 0, atB);
            var backToA = BreakoffPath.Sub(rotated, atB, total);

            // The two sides: the outline from A to B closed back along the line, and the line
            // closed back along the rest of the outline.
            var sideTo = toB.Concat(BreakoffPath.Reversed(line)).ToList();
            var sideBack = line.Concat(backToA).ToList();
            var sideToArea = System.Math.Abs(BreakoffPath.SignedArea(BreakoffPath.ToPoints(sideTo, ArcTolerance)));
            var sideBackArea = System.Math.Abs(BreakoffPath.SignedArea(BreakoffPath.ToPoints(sideBack, ArcTolerance)));
            if (System.Math.Abs(sideToArea + sideBackArea - outlineArea) > 0.01 * outlineArea)
                return Fail("The joining line must run inside the outline.");

            var scrap = (sideToArea <= sideBackArea) != breakoff.Flip ? sideTo : sideBack;
            var scrapPoints = BreakoffPath.ToPoints(scrap, ArcTolerance);

            var length = BreakoffPath.Length(line);
            var tabWidth = breakoff.TabWidth;
            var centers = breakoff.TabCenters.Count == 0
                ? EvenTabCenters(length, Breakoff.DefaultTabCount, tabWidth)
                : breakoff.TabCenters.OrderBy(c => c).ToList();
            if (centers.Count < Breakoff.MinTabCount || length <= tabWidth * centers.Count)
                return Fail("The joining line is too short for its tabs.");
            if (centers[0] < tabWidth / 2 - JoinTolerance || centers[^1] > length - tabWidth / 2 + JoinTolerance)
                return Fail("A tab runs off the joining line; space the tabs evenly again.");
            for (var i = 1; i < centers.Count; i++)
                if (centers[i] - centers[i - 1] <= tabWidth + JoinTolerance)
                    return Fail("Two tabs overlap; space the tabs evenly again.");

            var side = SideOf(line, length, scrapPoints);
            if (side == 0)
                return Fail("Could not tell which side of the joining line is scrap.");

            var cuts = new List<IReadOnlyList<Entity>>();
            for (var i = 0; i + 1 < centers.Count; i++)
            {
                var cut = BreakoffPath.Sub(line, centers[i] + tabWidth / 2, centers[i + 1] - tabWidth / 2);
                if (side < 0)
                    cut = BreakoffPath.Reversed(cut);
                foreach (var e in cut)
                {
                    e.Id = Guid.NewGuid();
                    e.Layer = SpecialLayers.Break;
                }
                cuts.Add(cut);
            }

            var replaced = outline.Entities.Select(e => sources[e]).ToList();
            replaced.AddRange(edgeSources);
            return new BreakoffLayout
            {
                BreakLine = line,
                Length = length,
                Outline = loop,
                Scrap = scrap,
                Cuts = cuts,
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
        /// The chain of visible lines and arcs through <paramref name="picked"/>, grown until it
        /// reaches a point where it does not continue as a single path (such as where the joining
        /// line meets the outline). Empty when <paramref name="picked"/> cannot be a joining line.
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

        /// <summary>
        /// Splits each visible line or arc of <paramref name="entities"/> where another one ends on
        /// its interior (a T-junction), so a joining line can end exactly where it meets other
        /// geometry. The first piece keeps the entity's ID. Returns the IDs of every split entity's
        /// pieces, keyed by its original ID.
        /// </summary>
        public static Dictionary<Guid, List<Guid>> SplitAtJunctions(List<Entity> entities)
        {
            var pool = entities.Where(e => e is Line or Arc && e.IsVisible && e.Layer?.IsVisible != false && !IsMark(e))
                .ToList();
            var ends = pool.SelectMany(e => new[] { BreakoffPath.Start(e), BreakoffPath.End(e) }).ToList();
            var split = new Dictionary<Guid, List<Guid>>();
            foreach (var host in pool)
            {
                var box = host.BoundingBox;
                var cuts = new List<double>();
                foreach (var point in ends)
                {
                    if (point.X < box.Left - JoinTolerance || point.X > box.Right + JoinTolerance
                        || point.Y < box.Bottom - JoinTolerance || point.Y > box.Top + JoinTolerance)
                        continue;
                    if (point.DistanceTo(BreakoffPath.Start(host)) <= JoinTolerance
                        || point.DistanceTo(BreakoffPath.End(host)) <= JoinTolerance
                        || host.ClosestPointTo(point).DistanceTo(point) > JoinTolerance)
                        continue;
                    var at = BreakoffPath.DistanceOf(new[] { host }, point);
                    if (cuts.All(c => System.Math.Abs(c - at) > JoinTolerance))
                        cuts.Add(at);
                }
                if (cuts.Count == 0)
                    continue;

                cuts.Sort();
                var bounds = new List<double> { 0 };
                bounds.AddRange(cuts);
                bounds.Add(host.Length);
                var pieces = new List<Entity>();
                for (var i = 0; i + 1 < bounds.Count; i++)
                    pieces.AddRange(BreakoffPath.Sub(new[] { host }, bounds[i], bounds[i + 1]));
                pieces[0].Id = host.Id;
                foreach (var piece in pieces)
                    piece.IsVisible = host.IsVisible;
                var index = entities.IndexOf(host);
                entities.RemoveAt(index);
                entities.InsertRange(index, pieces);
                split[host.Id] = pieces.Select(p => p.Id).ToList();
            }
            return split;
        }

        private static BreakoffLayout Fail(string error) => new(error);

        // A stray open line ending on the outline (an unmarked etch line, say) would otherwise be
        // chained into it and keep the loop from closing. Nothing with a free end can be part of a
        // closed outline, so such entities are dropped, repeatedly, before chaining.
        private static List<Entity> WithoutDanglingEnds(List<Entity> entities)
        {
            // Cluster endpoints that meet within the chain tolerance, using a grid of that size.
            const double cell = Tolerance.ChainTolerance;
            var grid = new Dictionary<(long, long), List<(Vector Point, int Cluster)>>();
            var clusterOf = new int[entities.Count, 2];
            var sizes = new List<int>();
            for (var i = 0; i < entities.Count; i++)
                for (var side = 0; side < 2; side++)
                {
                    var point = side == 0 ? BreakoffPath.Start(entities[i]) : BreakoffPath.End(entities[i]);
                    var gx = (long)System.Math.Floor(point.X / cell);
                    var gy = (long)System.Math.Floor(point.Y / cell);
                    var cluster = -1;
                    for (var dx = -1; dx <= 1 && cluster < 0; dx++)
                        for (var dy = -1; dy <= 1 && cluster < 0; dy++)
                            if (grid.TryGetValue((gx + dx, gy + dy), out var near))
                                foreach (var (other, id) in near)
                                    if (other.DistanceTo(point) <= cell)
                                    {
                                        cluster = id;
                                        break;
                                    }
                    if (cluster < 0)
                    {
                        cluster = sizes.Count;
                        sizes.Add(0);
                    }
                    if (!grid.TryGetValue((gx, gy), out var bucket))
                        grid[(gx, gy)] = bucket = new List<(Vector, int)>();
                    bucket.Add((point, cluster));
                    sizes[cluster]++;
                    clusterOf[i, side] = cluster;
                }

            // Peel entities with a free end until none is left; closed loops never peel.
            var members = new List<int>[sizes.Count];
            for (var i = 0; i < entities.Count; i++)
                for (var side = 0; side < 2; side++)
                    (members[clusterOf[i, side]] ??= new List<int>()).Add(i);
            var removed = new bool[entities.Count];
            var queue = new Queue<int>(Enumerable.Range(0, entities.Count)
                .Where(i => sizes[clusterOf[i, 0]] < 2 || sizes[clusterOf[i, 1]] < 2));
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                if (removed[i])
                    continue;
                removed[i] = true;
                for (var side = 0; side < 2; side++)
                {
                    var cluster = clusterOf[i, side];
                    if (--sizes[cluster] == 1)
                        foreach (var other in members[cluster])
                            if (!removed[other])
                                queue.Enqueue(other);
                }
            }
            return entities.Where((_, i) => !removed[i]).ToList();
        }

        internal static bool IsMark(Entity e) =>
            string.Equals(e.Layer?.Name, "ETCH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Layer?.Name, SpecialLayers.Scribe.Name, StringComparison.OrdinalIgnoreCase);

        // The planner closes contours far tighter than the import chain tolerance, and the outline
        // is re-chained from any entity when the program is built, so every joint must be exact.
        internal static List<Entity> CloseExactly(IReadOnlyList<Entity> loop)
        {
            var result = new List<Entity>();
            for (var i = 0; i < loop.Count; i++)
            {
                var current = loop[i];
                var next = loop[(i + 1) % loop.Count];
                result.Add(current);
                var end = BreakoffPath.End(current);
                var start = BreakoffPath.Start(next);
                if (end.DistanceTo(start) == 0)
                    continue;
                if (next is Line nextLine)
                    nextLine.StartPoint = end;
                else if (current is Line currentLine)
                    currentLine.EndPoint = start;
                else if (end.DistanceTo(start) > 1e-8)
                    result.Add(new Line(end, start) { Layer = current.Layer, Color = current.Color, LineTypeName = current.LineTypeName });
            }
            return result;
        }

        private static Vector Normal(IReadOnlyList<Entity> path, double length, double distance)
        {
            const double nudge = 1e-6;
            var before = BreakoffPath.TangentAt(path, System.Math.Max(distance - nudge, 0));
            var after = BreakoffPath.TangentAt(path, System.Math.Min(distance + nudge, length));
            var t = new Vector(before.X + after.X, before.Y + after.Y);
            var size = System.Math.Sqrt(t.X * t.X + t.Y * t.Y);
            t = size <= Tolerance.Epsilon ? after : new Vector(t.X / size, t.Y / size);
            return new Vector(-t.Y, t.X);
        }

        // +1 when the scrap lies left of the line's travel, -1 when right, 0 when unclear.
        private static int SideOf(IReadOnlyList<Entity> line, double length, IReadOnlyList<Vector> scrap)
        {
            var probe = System.Math.Min(0.01, length * 0.01);
            var votes = 0;
            foreach (var fraction in new[] { 0.25, 0.5, 0.75 })
            {
                var d = length * fraction;
                var p = BreakoffPath.PointAt(line, d);
                var n = Normal(line, length, d);
                var left = BreakoffPath.Contains(scrap, p + n * probe);
                var right = BreakoffPath.Contains(scrap, p - n * probe);
                if (left != right)
                    votes += left ? 1 : -1;
            }
            return System.Math.Sign(votes);
        }
    }
}
