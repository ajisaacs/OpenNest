using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Fills the leftover L-shaped area (top strip + right strip) around a pair grid
    /// with the same drawing, caching results by drawing/box/spacing.
    /// </summary>
    public class PairRemnantFiller
    {
        private readonly double partSpacing;
        private readonly IFillComparer comparer;

        public PairRemnantFiller(double partSpacing, IFillComparer comparer)
        {
            this.partSpacing = partSpacing;
            this.comparer = comparer;
        }

        public int EstimateUpperBound(
            List<Part> gridParts,
            Box workArea,
            double maxUtilization,
            double partArea
        )
        {
            var gridBox = ((IEnumerable<IBoundable>)gridParts).GetBoundingBox();

            // L-shaped remnant: top strip (full width) + right strip (grid height only)
            var topHeight = System.Math.Max(0, workArea.Top - gridBox.Top);
            var rightWidth = System.Math.Max(0, workArea.Right - gridBox.Right);

            var topArea = workArea.Length * topHeight;
            var rightArea = rightWidth * System.Math.Min(gridBox.Top - workArea.Y, workArea.Width);
            var remnantArea = topArea + rightArea;

            return (int)(remnantArea * maxUtilization / partArea) + 1;
        }

        public List<Part> Fill(
            List<Part> gridParts,
            Drawing drawing,
            Box workArea,
            CancellationToken token
        )
        {
            var gridBox = ((IEnumerable<IBoundable>)gridParts).GetBoundingBox();
            var partBox = drawing.Program.BoundingBox();
            var minDim = System.Math.Min(partBox.Width, partBox.Length) + 2 * partSpacing;

            List<Part> bestRemnant = null;

            // Try top remnant (full width, above grid)
            var topY = gridBox.Top + partSpacing;
            var topLength = workArea.Top - topY;
            if (topLength >= minDim)
            {
                var topBox = new Box(workArea.X, topY, workArea.Length, topLength);
                var parts = FillRemnantBox(drawing, topBox, token);
                if (parts != null && parts.Count > (bestRemnant?.Count ?? 0))
                    bestRemnant = parts;
            }

            // Try right remnant (full height, right of grid)
            var rightX = gridBox.Right + partSpacing;
            var rightWidth = workArea.Right - rightX;
            if (rightWidth >= minDim)
            {
                var rightBox = new Box(rightX, workArea.Y, rightWidth, workArea.Width);
                var parts = FillRemnantBox(drawing, rightBox, token);
                if (parts != null && parts.Count > (bestRemnant?.Count ?? 0))
                    bestRemnant = parts;
            }

            return bestRemnant;
        }

        private List<Part> FillRemnantBox(Drawing drawing, Box remnantBox, CancellationToken token)
        {
            var cachedResult = FillResultCache.Get(drawing, remnantBox, partSpacing);
            if (cachedResult != null)
            {
                Debug.WriteLine($"[PairFiller] Remnant CACHE HIT: {cachedResult.Count} parts");
                return cachedResult;
            }

            var filler = new FillLinear(remnantBox, partSpacing) { Label = "Pairs-Remnant" };
            List<Part> parts = null;

            foreach (var angle in new[] { 0.0, Angle.HalfPI })
            {
                token.ThrowIfCancellationRequested();
                var result = FillHelpers.FillWithDirectionPreference(
                    dir => filler.Fill(drawing, angle, dir),
                    null,
                    comparer,
                    remnantBox
                );

                if (result != null && result.Count > (parts?.Count ?? 0))
                    parts = result;
            }

            Debug.WriteLine(
                $"[PairFiller] Remnant: {parts?.Count ?? 0} parts in "
                    + $"{remnantBox.Width:F2}x{remnantBox.Length:F2}"
            );

            if (parts != null && parts.Count > 0)
            {
                FillResultCache.Store(drawing, remnantBox, partSpacing, parts);
                return parts;
            }

            return null;
        }
    }
}
