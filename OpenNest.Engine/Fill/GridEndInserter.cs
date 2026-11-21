using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Completes a grid of pairs that leaves slack in its area. The grid is moved to each corner
    /// of the area, copies of its parts are slid in from the open sides, and the strip left
    /// beyond them is filled. A part whose pair no longer fits can still tuck beside the grid's
    /// last pair, offset across the row so it clears its neighbor: J-shaped bars stood on end
    /// in a 30.2" strip hold three pairs and one more bar dropped 2.3" beside them, with a pair
    /// lying under all seven.
    /// </summary>
    /// <remarks>
    /// Every insert is a <see cref="Compactor"/> push, so the parts keep the part spacing and
    /// stay in the area by construction; an insert is kept only when it stops inside the area.
    /// </remarks>
    internal static class GridEndInserter
    {
        /// <summary>Positions across the row a copy is first slid in from, per template.</summary>
        private const int CoarseSamples = 24;

        /// <summary>Most positions tried around the best coarse landing.</summary>
        private const int FineSamples = 24;

        /// <summary>Most copies slid in from one side.</summary>
        private const int MaxInserts = 4;

        /// <summary>
        /// The most parts found among the grid moved to each corner of <paramref name="area"/>,
        /// with and without copies slid in from its open sides, each with the strip left beyond
        /// them filled by <paramref name="fillStrip"/>; null when none places more than
        /// <paramref name="toBeat"/> parts or the grid alone. A strip is filled only when the
        /// parts it could hold by <paramref name="partArea"/> (the part's true area) could beat
        /// the best so far, since each fill lays out the strip with pairs.
        /// </summary>
        public static List<Part> Complete(
            List<Part> grid,
            Box area,
            double spacing,
            double partArea,
            int toBeat,
            Func<Box, List<Part>> fillStrip,
            CancellationToken token
        )
        {
            if (grid == null || grid.Count == 0 || !(partArea > 0))
                return null;

            // Nothing beyond the grid's own box can hold more parts than its area allows.
            if (grid.Count + (int)((area.Area() - grid.GetBoundingBox().Area()) / partArea) <= toBeat)
                return null;

            var gridBox = grid.GetBoundingBox();
            var slackX = area.Right - gridBox.Right;
            var slackY = area.Top - gridBox.Top;
            var baseX = area.Left - gridBox.Left;
            var baseY = area.Bottom - gridBox.Bottom;

            var templates = grid
                .GroupBy(p => (p.BaseDrawing, System.Math.Round(p.Rotation, 9)))
                .Select(g => g.First())
                .ToList();

            List<Part> best = null;
            var floor = System.Math.Max(toBeat, grid.Count);
            foreach (var toRight in new[] { false, true })
            {
                if (toRight && slackX <= Tolerance.Epsilon)
                    continue;

                foreach (var toTop in new[] { false, true })
                {
                    if (toTop && slackY <= Tolerance.Epsilon)
                        continue;

                    token.ThrowIfCancellationRequested();
                    var offset = new Vector(baseX + (toRight ? slackX : 0), baseY + (toTop ? slackY : 0));
                    var placed = grid.Select(p => p.CloneAtOffset(offset)).ToList();

                    // Moved to another corner without inserts, the grid leaves the same strips
                    // mirrored, which hold the same parts.
                    if (!toRight && !toTop)
                        Consider(WithStrip(placed, area, spacing, partArea, Floor(), fillStrip));

                    var inserted = new List<Part>(placed);
                    InsertMany(inserted, templates, area, spacing,
                        toRight ? PushDirection.Right : PushDirection.Left, token);
                    InsertMany(inserted, templates, area, spacing,
                        toTop ? PushDirection.Up : PushDirection.Down, token);
                    if (inserted.Count > placed.Count)
                        Consider(WithStrip(inserted, area, spacing, partArea, Floor(), fillStrip));
                }
            }

            return best;

            int Floor() => System.Math.Max(floor, best?.Count ?? 0);

            void Consider(List<Part> candidate)
            {
                if (candidate != null && candidate.Count > Floor())
                    best = candidate;
            }
        }

        /// <summary>
        /// <paramref name="parts"/> and the most parts <paramref name="fillStrip"/> places in one
        /// of the strips between their bounds and the area's edges, or <paramref name="parts"/>
        /// alone when no strip could lift the total above <paramref name="toBeat"/> by area.
        /// </summary>
        private static List<Part> WithStrip(
            List<Part> parts,
            Box area,
            double spacing,
            double partArea,
            int toBeat,
            Func<Box, List<Part>> fillStrip
        )
        {
            var box = parts.GetBoundingBox();
            var strips = new (double X, double Y, double Length, double Width)[]
            {
                (area.Left, box.Top + spacing, area.Length, area.Top - box.Top - spacing),
                (area.Left, area.Bottom, area.Length, box.Bottom - spacing - area.Bottom),
                (box.Right + spacing, area.Bottom, area.Right - box.Right - spacing, area.Width),
                (area.Left, area.Bottom, box.Left - spacing - area.Left, area.Width),
            };

            List<Part> bestStrip = null;
            foreach (var (x, y, length, width) in strips)
            {
                if (width <= Tolerance.Epsilon || length <= Tolerance.Epsilon)
                    continue;

                var room = (int)(length * width / partArea);
                if (parts.Count + room <= System.Math.Max(toBeat, parts.Count + (bestStrip?.Count ?? 0)))
                    continue;

                var filled = fillStrip(new Box(x, y, length, width));
                if (filled != null && filled.Count > (bestStrip?.Count ?? 0))
                    bestStrip = filled;
            }

            if (bestStrip == null)
                return parts;

            var result = new List<Part>(parts.Count + bestStrip.Count);
            result.AddRange(parts);
            result.AddRange(bestStrip);
            return result;
        }

        /// <summary>Slides copies in along <paramref name="slide"/> until none fits.</summary>
        private static void InsertMany(
            List<Part> parts,
            List<Part> templates,
            Box area,
            double spacing,
            PushDirection slide,
            CancellationToken token
        )
        {
            for (var i = 0; i < MaxInserts; i++)
            {
                token.ThrowIfCancellationRequested();
                var next = InsertOne(parts, templates, area, spacing, slide);
                if (next == null)
                    return;

                parts.Add(next);
            }
        }

        /// <summary>
        /// A copy slid in along <paramref name="slide"/> from beyond the area, at the position
        /// across the slide that strays least outside the parts' band (so the strip beyond stays
        /// whole), then travels deepest; null when no position leaves it inside the area. Each
        /// slide pushes against every part, so positions are swept coarsely first, then at half
        /// the spacing within one coarse step of the best coarse landing.
        /// </summary>
        private static Part InsertOne(
            List<Part> parts,
            List<Part> templates,
            Box area,
            double spacing,
            PushDirection slide
        )
        {
            var horizontal = slide == PushDirection.Left || slide == PushDirection.Right;
            var band = parts.GetBoundingBox();
            var direction = SpatialQuery.DirectionToOffset(slide, 1.0);
            var obstacles = new Compactor.ObstacleSet(parts, spacing);
            var moving = new List<Part>(1);
            var fine = System.Math.Max(spacing / 2, Tolerance.Epsilon);

            Part best = null;
            Part bestTemplate = null;
            var bestAcross = 0.0;
            var bestStray = double.MaxValue;
            var bestDepth = double.MaxValue;

            void Try(Part template, double across)
            {
                var copy = StartBeyond(template, area, spacing, slide, across, horizontal);
                moving.Clear();
                moving.Add(copy);
                if (Compactor.Push(moving, area, direction, obstacles) <= 0)
                    return;

                var landed = copy.BoundingBox;
                if (!Inside(landed, area))
                    return;

                var stray = horizontal
                    ? System.Math.Max(0, band.Bottom - landed.Bottom) + System.Math.Max(0, landed.Top - band.Top)
                    : System.Math.Max(0, band.Left - landed.Left) + System.Math.Max(0, landed.Right - band.Right);
                var depth = LeadingEdge(landed, slide);
                if (stray < bestStray - Tolerance.Epsilon
                    || (stray < bestStray + Tolerance.Epsilon && depth < bestDepth - Tolerance.Epsilon))
                {
                    bestStray = stray;
                    bestDepth = depth;
                    best = copy;
                    bestTemplate = template;
                    bestAcross = across;
                }
            }

            (double Lo, double Hi) Range(Part template)
            {
                var box = template.BoundingBox;
                var size = horizontal ? box.Width : box.Length;
                return (horizontal ? area.Bottom : area.Left, (horizontal ? area.Top : area.Right) - size);
            }

            var coarse = fine;
            foreach (var template in templates)
            {
                var (lo, hi) = Range(template);
                if (hi < lo - Tolerance.Epsilon)
                    continue;

                var samples = System.Math.Clamp((int)System.Math.Ceiling((hi - lo) / fine), 1, CoarseSamples);
                coarse = System.Math.Max(coarse, (hi - lo) / samples);
                for (var i = 0; i <= samples; i++)
                    Try(template, lo + (hi - lo) * i / samples);
            }

            if (bestTemplate != null && coarse > fine + Tolerance.Epsilon)
            {
                var (lo, hi) = Range(bestTemplate);
                var from = System.Math.Max(lo, bestAcross - coarse);
                var to = System.Math.Min(hi, bestAcross + coarse);
                var samples = System.Math.Clamp((int)System.Math.Ceiling((to - from) / fine), 1, FineSamples);
                for (var i = 0; i <= samples; i++)
                    Try(bestTemplate, from + (to - from) * i / samples);
            }

            return best;
        }

        /// <summary>
        /// A copy of <paramref name="template"/> just beyond the area edge the slide starts from,
        /// its lower bound across the slide at <paramref name="across"/>.
        /// </summary>
        private static Part StartBeyond(
            Part template,
            Box area,
            double spacing,
            PushDirection slide,
            double across,
            bool horizontal
        )
        {
            var box = template.BoundingBox;
            var gap = spacing + 1.0;
            double dx, dy;
            if (horizontal)
            {
                dy = across - box.Bottom;
                dx = slide == PushDirection.Left
                    ? area.Right + gap - box.Left
                    : area.Left - gap - box.Right;
            }
            else
            {
                dx = across - box.Left;
                dy = slide == PushDirection.Down
                    ? area.Top + gap - box.Bottom
                    : area.Bottom - gap - box.Top;
            }

            var copy = template.CloneAtOffset(new Vector(dx, dy));
            copy.UpdateBounds();
            return copy;
        }

        /// <summary>How far the copy got: smaller is deeper along the slide.</summary>
        private static double LeadingEdge(Box box, PushDirection slide) =>
            slide switch
            {
                PushDirection.Left => box.Left,
                PushDirection.Right => -box.Right,
                PushDirection.Down => box.Bottom,
                _ => -box.Top,
            };

        private static bool Inside(Box box, Box area) =>
            box.Left >= area.Left - Tolerance.Epsilon
            && box.Right <= area.Right + Tolerance.Epsilon
            && box.Bottom >= area.Bottom - Tolerance.Epsilon
            && box.Top <= area.Top + Tolerance.Epsilon;
    }
}
