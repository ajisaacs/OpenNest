using OpenNest.Geometry;
using OpenNest.RectanglePacking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Fill
{
    public enum ShrinkAxis { Width, Height }

    public class ShrinkResult
    {
        public List<Part> Parts { get; set; }
        public double Dimension { get; set; }
    }

    /// <summary>
    /// Fills a box then iteratively shrinks one axis by the spacing amount
    /// until the part count drops. Returns the tightest box that still fits
    /// the target number of parts.
    /// </summary>
    public static class ShrinkFiller
    {
        public static ShrinkResult Shrink(
            Func<NestItem, Box, List<Part>> fillFunc,
            NestItem item, Box box,
            double spacing,
            ShrinkAxis axis,
            CancellationToken token = default,
            int maxIterations = 20,
            int targetCount = 0)
        {
            // If a target count is specified, estimate a smaller starting box
            // to avoid an expensive full-area fill.
            var startBox = box;
            if (targetCount > 0)
                startBox = EstimateStartBox(item, box, spacing, axis, targetCount);

            var parts = fillFunc(item, startBox);

            // If estimate was too aggressive and we got fewer than target,
            // fall back to the full box.
            if (targetCount > 0 && startBox != box
                && (parts == null || parts.Count < targetCount))
            {
                parts = fillFunc(item, box);
            }

            if (parts == null || parts.Count == 0)
                return new ShrinkResult { Parts = parts ?? new List<Part>(), Dimension = 0 };

            // Shrink target: if a target count was given and we got at least that many,
            // shrink to fit targetCount (not the full count). This produces a tighter box.
            // If we got fewer than target, shrink to maintain what we have.
            var shrinkTarget = targetCount > 0
                ? System.Math.Min(targetCount, parts.Count)
                : parts.Count;

            var bestParts = parts;
            var bestDim = MeasureDimension(parts, box, axis);

            for (var i = 0; i < maxIterations; i++)
            {
                if (token.IsCancellationRequested)
                    break;

                var trialDim = bestDim - spacing;
                if (trialDim <= 0)
                    break;

                var trialBox = axis == ShrinkAxis.Width
                    ? new Box(box.X, box.Y, trialDim, box.Length)
                    : new Box(box.X, box.Y, box.Width, trialDim);

                var trialParts = fillFunc(item, trialBox);

                if (trialParts == null || trialParts.Count < shrinkTarget)
                    break;

                bestParts = trialParts;
                bestDim = MeasureDimension(trialParts, box, axis);
            }

            return new ShrinkResult { Parts = bestParts, Dimension = bestDim };
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

            var maxDim = axis == ShrinkAxis.Height ? box.Length : box.Width;

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

            return axis == ShrinkAxis.Height
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
    }
}
