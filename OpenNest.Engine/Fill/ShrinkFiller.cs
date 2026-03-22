using OpenNest.Geometry;
using OpenNest.RectanglePacking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Fill
{
    public enum ShrinkAxis { Width, Length }

    public class ShrinkResult
    {
        public List<Part> Parts { get; set; }
        public double Dimension { get; set; }
    }

    /// <summary>
    /// Fills a box and trims excess parts by removing those farthest from
    /// the origin along the shrink axis.
    /// </summary>
    public static class ShrinkFiller
    {
        public static ShrinkResult Shrink(
            Func<NestItem, Box, List<Part>> fillFunc,
            NestItem item, Box box,
            double spacing,
            ShrinkAxis axis,
            CancellationToken token = default,
            int targetCount = 0,
            IProgress<NestProgress> progress = null,
            int plateNumber = 0,
            List<Part> placedParts = null)
        {
            var startBox = box;
            if (targetCount > 0)
                startBox = EstimateStartBox(item, box, spacing, axis, targetCount);

            var parts = fillFunc(item, startBox);

            if (targetCount > 0 && startBox != box
                && (parts == null || parts.Count < targetCount))
            {
                parts = fillFunc(item, box);
            }

            if (parts == null || parts.Count == 0)
                return new ShrinkResult { Parts = parts ?? new List<Part>(), Dimension = 0 };

            var shrinkTarget = targetCount > 0
                ? System.Math.Min(targetCount, parts.Count)
                : parts.Count;

            if (parts.Count > shrinkTarget)
                parts = TrimToCount(parts, shrinkTarget, axis);

            var dim = MeasureDimension(parts, box, axis);

            ReportShrinkProgress(progress, plateNumber, placedParts, parts, box, axis, dim);

            return new ShrinkResult { Parts = parts, Dimension = dim };
        }

        private static void ReportShrinkProgress(
            IProgress<NestProgress> progress, int plateNumber,
            List<Part> placedParts, List<Part> bestParts,
            Box workArea, ShrinkAxis axis, double dim)
        {
            if (progress == null)
                return;

            var allParts = placedParts != null && placedParts.Count > 0
                ? new List<Part>(placedParts.Count + bestParts.Count)
                : new List<Part>(bestParts.Count);

            if (placedParts != null && placedParts.Count > 0)
                allParts.AddRange(placedParts);
            allParts.AddRange(bestParts);

            var desc = $"Shrink {axis}: {bestParts.Count} parts, dim={dim:F1}";

            NestEngineBase.ReportProgress(progress, new ProgressReport
            {
                Phase = NestPhase.Custom,
                PlateNumber = plateNumber,
                Parts = allParts,
                WorkArea = workArea,
                Description = desc,
            });
        }

        /// <summary>
        /// Uses FillBestFit (fast rectangle packing) to estimate a starting box
        /// that fits roughly the target count. Scales the shrink axis proportionally
        /// from the full-area count down to the target, with margin.
        /// </summary>
        private static Box EstimateStartBox(NestItem item, Box box,
            double spacing, ShrinkAxis axis, int targetCount)
        {
            var bbox = item.Drawing.Program.BoundingBox();
            if (bbox.Width <= 0 || bbox.Length <= 0)
                return box;

            var maxDim = axis == ShrinkAxis.Length ? box.Length : box.Width;

            // Use FillBestFit for a fast, accurate rectangle count on the full box.
            var bin = new Bin { Size = new Size(box.Width, box.Length) };
            var packItem = new Item { Size = new Size(bbox.Width + spacing, bbox.Length + spacing) };
            var packer = new FillBestFit(bin);
            packer.Fill(packItem);
            var fullCount = bin.Items.Count;

            if (fullCount <= 0 || fullCount <= targetCount)
                return box;

            // Scale dimension proportionally: target/full * maxDim, with margin.
            var ratio = (double)targetCount / fullCount;
            var estimate = maxDim * ratio * 1.3;
            estimate = System.Math.Min(estimate, maxDim);

            if (estimate <= 0 || estimate >= maxDim)
                return box;

            return axis == ShrinkAxis.Length
                ? new Box(box.X, box.Y, box.Width, estimate)
                : new Box(box.X, box.Y, estimate, box.Length);
        }

        private static double MeasureDimension(List<Part> parts, Box box, ShrinkAxis axis)
        {
            var placedBox = parts.Cast<IBoundable>().GetBoundingBox();

            return axis == ShrinkAxis.Width
                ? placedBox.Right - box.X
                : placedBox.Top - box.Y;
        }

        /// <summary>
        /// Keeps the <paramref name="targetCount"/> parts nearest to the origin
        /// along the given axis, discarding parts farthest from the origin.
        /// Returns the input list unchanged if count is already at or below target.
        /// </summary>
        internal static List<Part> TrimToCount(List<Part> parts, int targetCount, ShrinkAxis axis)
        {
            if (parts == null || parts.Count <= targetCount)
                return parts;

            return axis == ShrinkAxis.Width
                ? parts.OrderBy(p => p.BoundingBox.Right).Take(targetCount).ToList()
                : parts.OrderBy(p => p.BoundingBox.Top).Take(targetCount).ToList();
        }
    }
}
