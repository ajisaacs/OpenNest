using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest
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
    /// the same number of parts.
    /// </summary>
    public static class ShrinkFiller
    {
        public static ShrinkResult Shrink(
            Func<NestItem, Box, List<Part>> fillFunc,
            NestItem item, Box box,
            double spacing,
            ShrinkAxis axis,
            CancellationToken token = default,
            int maxIterations = 20)
        {
            var parts = fillFunc(item, box);

            if (parts == null || parts.Count == 0)
                return new ShrinkResult { Parts = parts ?? new List<Part>(), Dimension = 0 };

            var targetCount = parts.Count;
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

                if (trialParts == null || trialParts.Count < targetCount)
                    break;

                bestParts = trialParts;
                bestDim = MeasureDimension(trialParts, box, axis);
            }

            return new ShrinkResult { Parts = bestParts, Dimension = bestDim };
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
