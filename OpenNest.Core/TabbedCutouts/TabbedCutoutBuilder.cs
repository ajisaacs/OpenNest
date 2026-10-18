using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Breakoffs;
using OpenNest.Geometry;

namespace OpenNest.TabbedCutouts
{
    /// <summary>
    /// Resolves and applies <see cref="TabbedCutout"/>s: the hole's contour is replaced by open
    /// pieces between tabs (on its own layer) and the tabs themselves (on the tab layer), in
    /// contour order, so the hole stays closed as material while only the pieces are cut.
    /// </summary>
    public static class TabbedCutoutBuilder
    {
        /// <summary>How close the hole's entities must meet to form one contour.</summary>
        public const double JoinTolerance = BreakoffBuilder.JoinTolerance;

        private const double ArcTolerance = 0.0005;

        /// <summary>
        /// <paramref name="entities"/> with every valid tabbed cutout applied. Inputs are not
        /// modified; generated entities are new. Cutouts that cannot be resolved are skipped and
        /// described in <paramref name="errors"/>.
        /// </summary>
        public static List<Entity> Apply(IReadOnlyList<Entity> entities, IReadOnlyList<TabbedCutout> cutouts,
            out List<string> errors)
        {
            errors = new List<string>();
            var working = entities.ToList();
            if (cutouts == null || cutouts.Count == 0)
                return working;

            for (var i = 0; i < cutouts.Count; i++)
            {
                var layout = Layout(working, cutouts[i]);
                if (!layout.IsValid)
                {
                    errors.Add($"Tabbed cutout {i + 1}: {layout.Error}");
                    continue;
                }

                var replaced = new HashSet<Entity>(layout.ReplacedEntities, ReferenceEqualityComparer.Instance);
                var at = working.FindIndex(replaced.Contains);
                working.RemoveAll(replaced.Contains);
                var generated = new List<Entity>();
                for (var p = 0; p < layout.Pieces.Count; p++)
                {
                    generated.AddRange(layout.Pieces[p].Select(e => e.Clone()));
                    generated.AddRange(layout.Tabs[p].Select(e => e.Clone()));
                }
                working.InsertRange(System.Math.Clamp(at, 0, working.Count), generated);
            }
            return working;
        }

        /// <summary>Resolves <paramref name="cutout"/> against <paramref name="entities"/> (the entities that will be cut).</summary>
        public static TabbedCutoutLayout Layout(IReadOnlyList<Entity> entities, TabbedCutout cutout)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(cutout);
            if (!(cutout.TabWidth > 0) || !double.IsFinite(cutout.TabWidth))
                return Fail("The tab width must be greater than zero.");

            var edgeIds = cutout.EdgeIds.ToHashSet();
            var sources = entities.Where(e => edgeIds.Contains(e.Id)).ToList();
            if (edgeIds.Count == 0 || sources.Count != edgeIds.Count)
                return Fail("Its contour is hidden or missing.");

            var chain = ChainClosed(sources);
            if (chain == null)
                return Fail("Its contour must be one closed loop of lines and arcs, or a circle.");

            var loop = BreakoffBuilder.CloseExactly(chain);
            var points = BreakoffPath.ToPoints(loop, ArcTolerance);
            if (BreakoffPath.SignedArea(points) < 0)
                loop = BreakoffPath.Reversed(loop);
            loop = StartAtLowestLeft(loop);
            points = BreakoffPath.ToPoints(loop, ArcTolerance);

            if (!InsideAnOutline(entities, edgeIds, points))
                return Fail("It must be a hole inside the part's outline.");

            var length = BreakoffPath.Length(loop);
            var tabWidth = cutout.TabWidth;
            var centers = cutout.TabCenters.Count == 0
                ? EvenTabCenters(length, TabbedCutout.DefaultTabCount, loop[0].Length / 2)
                : cutout.TabCenters.Select(c => Wrap(c, length)).OrderBy(c => c).ToList();
            if (centers.Count < TabbedCutout.MinTabCount || length <= 2 * tabWidth * centers.Count)
                return Fail("The contour is too short for its tabs.");
            if (centers.Any(c => !double.IsFinite(c)))
                return Fail("A tab is off the contour; space the tabs evenly again.");
            for (var i = 0; i < centers.Count; i++)
            {
                var next = i + 1 < centers.Count ? centers[i + 1] : centers[0] + length;
                if (centers.Count > 1 && next - centers[i] <= tabWidth + JoinTolerance)
                    return Fail("Two tabs overlap; space the tabs evenly again.");
            }

            // Walk a doubled loop from the end of the first tab, so every span is one increasing range.
            var doubled = loop.Concat(loop).ToList();
            var origin = centers[0] + tabWidth / 2;
            if (origin >= length)
                origin -= length;
            var pieces = new List<IReadOnlyList<Entity>>();
            var tabs = new List<IReadOnlyList<Entity>>();
            for (var i = 0; i < centers.Count; i++)
            {
                var nextCenter = i + 1 < centers.Count ? centers[i + 1] : centers[0] + length;
                var from = centers[i] + tabWidth / 2;
                var cutTo = nextCenter - tabWidth / 2;
                var tabTo = nextCenter + tabWidth / 2;
                pieces.Add(Span(doubled, Unwrap(from), Unwrap(cutTo), null));
                tabs.Add(Span(doubled, Unwrap(cutTo), Unwrap(tabTo), SpecialLayers.Tab));
            }

            return new TabbedCutoutLayout
            {
                Loop = loop,
                Length = length,
                Pieces = pieces,
                Tabs = tabs,
                TabCenters = centers,
                TabWidth = tabWidth,
                ReplacedEntities = sources,
            };

            // A distance at or after the walk's origin, in the doubled loop.
            double Unwrap(double distance)
            {
                while (distance < origin - 1e-12)
                    distance += length;
                return distance;
            }
        }

        /// <summary>
        /// <paramref name="count"/> tab centers spaced evenly around a contour of
        /// <paramref name="length"/>, one of them at <paramref name="firstCenter"/>. The builder
        /// centers one on the middle of the contour's first entity, so tabs avoid its start corner.
        /// </summary>
        public static List<double> EvenTabCenters(double length, int count, double firstCenter)
        {
            count = System.Math.Max(count, TabbedCutout.MinTabCount);
            var spacing = length / count;
            var start = Wrap(firstCenter, spacing);
            return Enumerable.Range(0, count).Select(i => start + spacing * i).ToList();
        }

        /// <summary>
        /// The visible lines and arcs forming the closed contour through <paramref name="picked"/>,
        /// or the picked circle alone. Empty when <paramref name="picked"/> is on no closed contour
        /// that continues as a single path.
        /// </summary>
        public static List<Entity> FindHole(IReadOnlyList<Entity> entities, Entity picked)
        {
            var pool = entities.Where(e => e is Line or Arc or Circle && e.IsVisible && e.Layer?.IsVisible != false
                && !BreakoffBuilder.IsMark(e) && e.Layer != SpecialLayers.Break).ToList();
            if (!pool.Contains(picked))
                return new List<Entity>();
            if (picked is Circle)
                return new List<Entity> { picked };

            var lines = pool.Where(e => e is Line or Arc).ToList();
            var chain = new List<Entity> { picked };
            var start = BreakoffPath.Start(picked);
            var tip = BreakoffPath.End(picked);
            while (tip.DistanceTo(start) > JoinTolerance || chain.Count == 1)
            {
                var touching = lines.Where(e => !chain.Contains(e)
                    && (BreakoffPath.Start(e).DistanceTo(tip) <= JoinTolerance
                        || BreakoffPath.End(e).DistanceTo(tip) <= JoinTolerance)).ToList();
                if (touching.Count != 1)
                    return new List<Entity>();
                var next = touching[0];
                chain.Add(next);
                tip = BreakoffPath.Start(next).DistanceTo(tip) <= JoinTolerance
                    ? BreakoffPath.End(next)
                    : BreakoffPath.Start(next);
            }
            return chain;
        }

        private static TabbedCutoutLayout Fail(string error) => new(error);

        private static double Wrap(double distance, double length)
        {
            var wrapped = distance % length;
            return wrapped < 0 ? wrapped + length : wrapped;
        }

        // Copies of the span from `from` to `to`, with new IDs and, for tabs, the tab layer.
        private static List<Entity> Span(IReadOnlyList<Entity> path, double from, double to, Layer layer)
        {
            var span = BreakoffPath.Sub(path, from, to);
            foreach (var e in span)
            {
                e.Id = Guid.NewGuid();
                if (layer != null)
                    e.Layer = layer;
            }
            return span;
        }

        /// <summary>
        /// <paramref name="edges"/> copied into one closed chain in travel order, a circle as two
        /// half arcs; null when they are not exactly one closed loop.
        /// </summary>
        private static List<Entity> ChainClosed(IReadOnlyList<Entity> edges)
        {
            if (edges.Count == 1 && edges[0] is Circle circle)
            {
                var reversed = circle.Rotation == RotationType.CW;
                Arc Half(double from, double to) => new(circle.Center, circle.Radius, from, to, reversed)
                {
                    Layer = circle.Layer,
                    Color = circle.Color,
                    LineTypeName = circle.LineTypeName,
                };
                return new List<Entity> { Half(System.Math.PI, 0), Half(0, System.Math.PI) };
            }
            if (edges.Count == 0 || edges.Any(e => e is not (Line or Arc)))
                return null;

            var remaining = edges.ToList();
            var chain = new List<Entity> { BreakoffPath.Copy(remaining[0]) };
            remaining.RemoveAt(0);
            while (remaining.Count > 0)
            {
                var tip = BreakoffPath.End(chain[^1]);
                var matches = remaining.Where(e => BreakoffPath.Start(e).DistanceTo(tip) <= JoinTolerance
                    || BreakoffPath.End(e).DistanceTo(tip) <= JoinTolerance).ToList();
                if (matches.Count != 1)
                    return null;
                var next = matches[0];
                var copy = BreakoffPath.Copy(next);
                if (BreakoffPath.Start(next).DistanceTo(tip) > JoinTolerance)
                    copy.Reverse();
                remaining.Remove(next);
                chain.Add(copy);
            }
            return BreakoffPath.Start(chain[0]).DistanceTo(BreakoffPath.End(chain[^1])) <= JoinTolerance ? chain : null;
        }

        // Rotates a closed loop to start at its vertex with the smallest X (then Y), so tab
        // positions survive re-chaining.
        private static List<Entity> StartAtLowestLeft(List<Entity> loop)
        {
            var best = 0;
            for (var i = 1; i < loop.Count; i++)
            {
                var p = BreakoffPath.Start(loop[i]);
                var q = BreakoffPath.Start(loop[best]);
                if (p.X < q.X - JoinTolerance || (System.Math.Abs(p.X - q.X) <= JoinTolerance && p.Y < q.Y))
                    best = i;
            }
            return loop.Skip(best).Concat(loop.Take(best)).ToList();
        }

        // Whether the loop lies inside some other closed outline drawn with it.
        private static bool InsideAnOutline(IReadOnlyList<Entity> entities, HashSet<Guid> loopIds,
            IReadOnlyList<Vector> loop)
        {
            var others = entities.Where(e => !loopIds.Contains(e.Id) && e is Line or Arc or Circle
                && !BreakoffBuilder.IsMark(e) && e.Layer != SpecialLayers.Break && e.Layer != SpecialLayers.Tab).ToList();
            var loopArea = System.Math.Abs(BreakoffPath.SignedArea(loop));
            foreach (var shape in ShapeBuilder.GetShapes(others))
            {
                if (!shape.IsClosed())
                    continue;
                var outline = shape.ToPolygonWithTolerance(ArcTolerance).Vertices;
                if (System.Math.Abs(BreakoffPath.SignedArea(outline)) > loopArea
                    && loop.All(p => BreakoffPath.Contains(outline, p)))
                    return true;
            }
            return false;
        }
    }
}
