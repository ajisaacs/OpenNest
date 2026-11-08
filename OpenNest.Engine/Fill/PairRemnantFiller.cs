using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Fills the leftover L-shaped area (top strip + right strip) around a pair grid
    /// with the same drawing. Each strip is tried with single parts at 0 and 90 degrees and,
    /// when a plate and best-fit pairs are given, with rows of pairs as the Row and Column
    /// strategies lay them: a narrow strip can hold twice as many parts as pairs.
    /// </summary>
    public class PairRemnantFiller
    {
        private readonly double partSpacing;
        private readonly IFillComparer comparer;
        private readonly Plate plate;

        /// <summary>
        /// Pair fills of a strip by strip size, normalized to the origin. Per filler, because
        /// they depend on the best-fit list it fills from.
        /// </summary>
        private readonly ConcurrentDictionary<(double Width, double Length), List<Part>> pairStrips =
            new();

        public PairRemnantFiller(double partSpacing, IFillComparer comparer)
        {
            this.partSpacing = partSpacing;
            this.comparer = comparer;
        }

        public PairRemnantFiller(Plate plate, IFillComparer comparer)
            : this(plate.PartSpacing, comparer)
        {
            this.plate = plate;
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
            CancellationToken token,
            List<BestFitResult> bestFits = null
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
                var parts = FillRemnantBox(drawing, topBox, bestFits, token);
                if (parts != null && parts.Count > (bestRemnant?.Count ?? 0))
                    bestRemnant = parts;
            }

            // Try right remnant (full height, right of grid)
            var rightX = gridBox.Right + partSpacing;
            var rightWidth = workArea.Right - rightX;
            if (rightWidth >= minDim)
            {
                var rightBox = new Box(rightX, workArea.Y, rightWidth, workArea.Width);
                var parts = FillRemnantBox(drawing, rightBox, bestFits, token);
                if (parts != null && parts.Count > (bestRemnant?.Count ?? 0))
                    bestRemnant = parts;
            }

            return bestRemnant;
        }

        private List<Part> FillRemnantBox(
            Drawing drawing,
            Box remnantBox,
            List<BestFitResult> bestFits,
            CancellationToken token
        )
        {
            var singles = FillSingles(drawing, remnantBox, token);
            if (bestFits == null || plate == null)
                return singles;

            // No layout holds more parts than the strip has room for by area.
            var singleCount = singles?.Count ?? 0;
            if (drawing.Area > 0 && singleCount + 1 > remnantBox.Area() / drawing.Area)
                return singles;

            // Pairs replace the singles only when they place more; a tie keeps the singles.
            var pairs = FillPairs(drawing, remnantBox, bestFits, token);
            return pairs != null && pairs.Count > (singles?.Count ?? 0) ? pairs : singles;
        }

        private List<Part> FillSingles(Drawing drawing, Box remnantBox, CancellationToken token)
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

        /// <summary>
        /// The strip filled with rows of pairs, or null when none fit. Computed once per strip
        /// size: the layout depends only on the size, so it is laid out at the origin and moved.
        /// </summary>
        private List<Part> FillPairs(
            Drawing drawing,
            Box remnantBox,
            List<BestFitResult> bestFits,
            CancellationToken token
        )
        {
            var key = (
                System.Math.Round(remnantBox.Width, 6),
                System.Math.Round(remnantBox.Length, 6)
            );
            if (!pairStrips.TryGetValue(key, out var normalized))
            {
                var origin = new Box(0, 0, remnantBox.Length, remnantBox.Width);
                normalized = ComputePairs(drawing, origin, bestFits, token) ?? new List<Part>();
                pairStrips.TryAdd(key, normalized);
            }

            if (normalized.Count == 0)
                return null;

            var offset = remnantBox.Location;
            return normalized.Select(p => p.CloneAtOffset(offset)).ToList();
        }

        /// <summary>
        /// Fills the strip as the Row and Column strategies do: rows of pairs turned so a whole
        /// number of them spans the strip, then singles beyond them.
        /// </summary>
        private List<Part> ComputePairs(
            Drawing drawing,
            Box area,
            List<BestFitResult> bestFits,
            CancellationToken token
        )
        {
            var context = new FillContext
            {
                Item = new NestItem { Drawing = drawing },
                WorkArea = area,
                Plate = plate,
                Token = token,
                Policy = new FillPolicy(comparer),
            };
            context.SharedState["BestFits"] = bestFits;

            // The stripe filler lays rows along both axes itself; its axis only names it.
            var best = new StripeFiller(context, NestDirection.Horizontal).Fill();

            Debug.WriteLine(
                $"[PairFiller] Remnant pairs: {best?.Count ?? 0} parts in "
                    + $"{area.Width:F2}x{area.Length:F2}"
            );
            return best;
        }
    }
}
