using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Tries to fit more parts at the end of a fill's outermost row. A lattice usually leaves
    /// slack between its last row and the work-area edge and a gap at the end of each row that
    /// is too short for one more copy. This pass pushes the outer row into that slack, slides
    /// it toward one end of the row, then slides copies of the row's end part in from the freed
    /// end. Lifting staggered hexagons straight over the row below, for instance, opens a notch
    /// beside the last pair where one more hexagon nests.
    /// </summary>
    /// <remarks>
    /// Every move is a <see cref="Compactor"/> push, so the parts keep the part spacing from
    /// each other and stay in the work area by construction; an inserted copy is kept only when
    /// it stops inside the work area. The caller's comparer decides whether the result wins.
    /// </remarks>
    public static class EndRowRefiller
    {
        /// <summary>A row is the parts whose outer bounds lie this close to the outermost.</summary>
        private const double RowTolerance = 1e-3;

        /// <summary>Most positions across the row a copy is slid in from.</summary>
        private const int MaxSamples = 96;

        private static readonly (PushDirection Outward, PushDirection[] Along)[] Sides =
        {
            (PushDirection.Up, new[] { PushDirection.Left, PushDirection.Right }),
            (PushDirection.Right, new[] { PushDirection.Down, PushDirection.Up }),
            (PushDirection.Down, new[] { PushDirection.Left, PushDirection.Right }),
            (PushDirection.Left, new[] { PushDirection.Down, PushDirection.Up }),
        };

        /// <summary>
        /// The best layout <paramref name="comparer"/> finds among <paramref name="parts"/> and
        /// its end-row refills, or <paramref name="parts"/> itself when none beats it.
        /// <paramref name="maxQuantity"/> caps the part count when positive.
        /// </summary>
        public static List<Part> Improve(
            List<Part> parts,
            Box workArea,
            double partSpacing,
            IFillComparer comparer,
            int maxQuantity,
            CancellationToken token
        )
        {
            if (parts == null || parts.Count < 2)
                return parts;
            if (maxQuantity > 0 && parts.Count >= maxQuantity)
                return parts;

            var best = parts;
            foreach (var (outward, along) in Sides)
            {
                foreach (var slide in along)
                {
                    token.ThrowIfCancellationRequested();
                    var candidate = TrySide(parts, workArea, partSpacing, outward, slide, maxQuantity);
                    if (candidate != null && comparer.IsBetter(candidate, best, workArea))
                        best = candidate;
                }
            }

            return best;
        }

        private static List<Part> TrySide(
            List<Part> parts,
            Box workArea,
            double spacing,
            PushDirection outward,
            PushDirection slide,
            int maxQuantity
        )
        {
            var outerEdge = parts.Max(p => OuterEdge(p.BoundingBox, outward));
            var row = new List<Part>();
            var rest = new List<Part>();
            foreach (var part in parts)
            {
                if (OuterEdge(part.BoundingBox, outward) >= outerEdge - RowTolerance)
                    row.Add(part.CloneAtOffset(Vector.Zero));
                else
                    rest.Add(part);
            }

            if (row.Count < 2)
                return null;

            // Into the edge slack first, so the row can pass over the one below it, then
            // along the row. The outward push is what frees the slide.
            var lifted = Compactor.Push(row, rest, workArea, spacing, outward);
            var slid = Compactor.Push(row, rest, workArea, spacing, slide);
            if (lifted <= 0 && slid <= 0)
                return null;

            var obstacles = new List<Part>(rest.Count + row.Count);
            obstacles.AddRange(rest);
            obstacles.AddRange(row);

            var templates = row
                .GroupBy(p => (p.BaseDrawing, System.Math.Round(p.Rotation, 9)))
                .Select(g => g.First())
                .ToList();

            var inserted = new List<Part>();
            var limit = maxQuantity > 0 ? maxQuantity - parts.Count : row.Count;
            while (inserted.Count < limit)
            {
                var next = InsertOne(templates, obstacles, row, workArea, spacing, outward, slide);
                if (next == null)
                    break;
                inserted.Add(next);
                obstacles.Add(next);
            }

            if (inserted.Count == 0)
                return null;

            var result = new List<Part>(rest.Count + row.Count + inserted.Count);
            result.AddRange(rest);
            result.AddRange(row);
            result.AddRange(inserted);
            return result;
        }

        /// <summary>
        /// A copy slid in along <paramref name="slide"/> from beyond the work area at the
        /// position across the row band that lets it travel deepest, or null when no position
        /// leaves it inside the work area.
        /// </summary>
        private static Part InsertOne(
            List<Part> templates,
            List<Part> obstacles,
            List<Part> row,
            Box workArea,
            double spacing,
            PushDirection outward,
            PushDirection slide
        )
        {
            var vertical = outward == PushDirection.Up || outward == PushDirection.Down;
            var rowBox = row.GetBoundingBox();
            var direction = SpatialQuery.DirectionToOffset(slide, 1.0);

            // One set for the whole sweep, so each obstacle's offset contour is built once.
            var obstacleSet = new Compactor.ObstacleSet(obstacles, spacing);
            var moving = new List<Part>(1);

            Part best = null;
            var bestDepth = double.MaxValue;

            foreach (var template in templates)
            {
                var box = template.BoundingBox;
                var size = vertical ? box.Width : box.Length;

                // Across the row: from one part size inside the row's inner edge to the
                // work-area edge, so a copy can drop into notches between the row and the
                // one below it.
                double lo, hi;
                if (vertical)
                {
                    lo = System.Math.Max(workArea.Bottom, rowBox.Bottom - size);
                    hi = System.Math.Min(workArea.Top - size, rowBox.Top);
                }
                else
                {
                    lo = System.Math.Max(workArea.Left, rowBox.Left - size);
                    hi = System.Math.Min(workArea.Right - size, rowBox.Right);
                }

                if (hi < lo)
                    continue;

                var step = System.Math.Max(spacing / 2, Tolerance.Epsilon);
                var samples = (int)System.Math.Ceiling((hi - lo) / step);
                samples = System.Math.Clamp(samples, 1, MaxSamples);

                for (var i = 0; i <= samples; i++)
                {
                    var across = lo + (hi - lo) * i / samples;
                    var copy = StartBeyond(template, workArea, spacing, slide, across, vertical);
                    moving.Clear();
                    moving.Add(copy);
                    var travel = Compactor.Push(moving, workArea, direction, obstacleSet);
                    if (travel <= 0)
                        continue;

                    var landed = copy.BoundingBox;
                    if (!Inside(landed, workArea))
                        continue;

                    var depth = LeadingEdge(landed, slide);
                    if (depth < bestDepth - Tolerance.Epsilon)
                    {
                        bestDepth = depth;
                        best = copy;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// A copy of <paramref name="template"/> just beyond the work-area edge the slide
        /// starts from, its inner bound at <paramref name="across"/> across the row.
        /// </summary>
        private static Part StartBeyond(
            Part template,
            Box workArea,
            double spacing,
            PushDirection slide,
            double across,
            bool vertical
        )
        {
            var box = template.BoundingBox;
            var gap = spacing + 1.0;
            double dx, dy;
            if (vertical)
            {
                dy = across - box.Bottom;
                dx = slide == PushDirection.Left
                    ? workArea.Right + gap - box.Left
                    : workArea.Left - gap - box.Right;
            }
            else
            {
                dx = across - box.Left;
                dy = slide == PushDirection.Down
                    ? workArea.Top + gap - box.Bottom
                    : workArea.Bottom - gap - box.Top;
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

        private static double OuterEdge(Box box, PushDirection outward) =>
            outward switch
            {
                PushDirection.Up => box.Top,
                PushDirection.Right => box.Right,
                PushDirection.Down => -box.Bottom,
                _ => -box.Left,
            };

        private static bool Inside(Box box, Box workArea) =>
            box.Left >= workArea.Left - Tolerance.Epsilon
            && box.Right <= workArea.Right + Tolerance.Epsilon
            && box.Bottom >= workArea.Bottom - Tolerance.Epsilon
            && box.Top <= workArea.Top + Tolerance.Epsilon;
    }
}
