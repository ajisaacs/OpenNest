using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public class StripNestEngine : NestEngineBase
    {
        public StripNestEngine(Plate plate) : base(plate)
        {
        }

        public override string Name => "Strip";

        public override string Description => "Strip-based nesting for mixed-drawing layouts";

        /// <summary>
        /// Single-item fill delegates to DefaultNestEngine.
        /// The strip strategy adds value for multi-drawing nesting, not single-item fills.
        /// </summary>
        public override List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.Fill(item, workArea, progress, token);
        }

        /// <summary>
        /// Group-parts fill delegates to DefaultNestEngine.
        /// </summary>
        public override List<Part> Fill(List<Part> groupParts, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.Fill(groupParts, workArea, progress, token);
        }

        /// <summary>
        /// Pack delegates to DefaultNestEngine.
        /// </summary>
        public override List<Part> PackArea(Box box, List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.PackArea(box, items, progress, token);
        }

        /// <summary>
        /// Selects the item that consumes the most plate area (bounding box area x quantity).
        /// Returns the index into the items list.
        /// </summary>
        private static int SelectStripItemIndex(List<NestItem> items, Box workArea)
        {
            var bestIndex = 0;
            var bestArea = 0.0;

            for (var i = 0; i < items.Count; i++)
            {
                var bbox = items[i].Drawing.Program.BoundingBox();
                var qty = items[i].Quantity > 0
                    ? items[i].Quantity
                    : (int)(workArea.Area() / bbox.Area());
                var totalArea = bbox.Area() * qty;

                if (totalArea > bestArea)
                {
                    bestArea = totalArea;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// Estimates the strip dimension (height for bottom, width for left) needed
        /// to fit the target quantity. Tries 0 deg and 90 deg rotations and picks the shorter.
        /// This is only an estimate for the shrink loop starting point — the actual fill
        /// uses DefaultNestEngine.Fill which tries many rotation angles internally.
        /// </summary>
        private static double EstimateStripDimension(NestItem item, double stripLength, double maxDimension)
        {
            var bbox = item.Drawing.Program.BoundingBox();
            var qty = item.Quantity > 0
                ? item.Quantity
                : System.Math.Max(1, (int)(stripLength * maxDimension / bbox.Area()));

            // At 0 deg: parts per row along strip length, strip dimension is bbox.Length
            var perRow0 = (int)(stripLength / bbox.Width);
            var rows0 = perRow0 > 0 ? (int)System.Math.Ceiling((double)qty / perRow0) : int.MaxValue;
            var dim0 = rows0 * bbox.Length;

            // At 90 deg: rotated bounding box (Width and Length swap)
            var perRow90 = (int)(stripLength / bbox.Length);
            var rows90 = perRow90 > 0 ? (int)System.Math.Ceiling((double)qty / perRow90) : int.MaxValue;
            var dim90 = rows90 * bbox.Width;

            var estimate = System.Math.Min(dim0, dim90);

            // Clamp to available dimension
            return System.Math.Min(estimate, maxDimension);
        }

        /// <summary>
        /// Multi-drawing strip nesting strategy.
        /// Picks the largest-area drawing for strip treatment, finds the tightest strip
        /// in both bottom and left orientations, fills remnants with remaining drawings,
        /// and returns the denser result.
        /// </summary>
        public override List<Part> Nest(List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            if (items == null || items.Count == 0)
                return new List<Part>();

            var workArea = Plate.WorkArea();

            // Select which item gets the strip treatment.
            var stripIndex = SelectStripItemIndex(items, workArea);
            var stripItem = items[stripIndex];
            var remainderItems = items.Where((_, i) => i != stripIndex).ToList();

            // Try both orientations.
            var bottomResult = TryOrientation(StripDirection.Bottom, stripItem, remainderItems, workArea, progress, token);
            var leftResult = TryOrientation(StripDirection.Left, stripItem, remainderItems, workArea, progress, token);

            // Pick the better result.
            var winner = bottomResult.Score >= leftResult.Score
                ? bottomResult.Parts
                : leftResult.Parts;

            // Deduct placed quantities from the original items.
            foreach (var item in items)
            {
                if (item.Quantity <= 0)
                    continue;

                var placed = winner.Count(p => p.BaseDrawing.Name == item.Drawing.Name);
                item.Quantity = System.Math.Max(0, item.Quantity - placed);
            }

            return winner;
        }

        private StripNestResult TryOrientation(StripDirection direction, NestItem stripItem,
            List<NestItem> remainderItems, Box workArea, IProgress<NestProgress> progress, CancellationToken token)
        {
            var result = new StripNestResult { Direction = direction };

            if (token.IsCancellationRequested)
                return result;

            // Estimate initial strip dimension.
            var stripLength = direction == StripDirection.Bottom ? workArea.Width : workArea.Length;
            var maxDimension = direction == StripDirection.Bottom ? workArea.Length : workArea.Width;
            var estimatedDim = EstimateStripDimension(stripItem, stripLength, maxDimension);

            // Create the initial strip box.
            var stripBox = direction == StripDirection.Bottom
                ? new Box(workArea.X, workArea.Y, workArea.Width, estimatedDim)
                : new Box(workArea.X, workArea.Y, estimatedDim, workArea.Length);

            // Shrink to tightest strip.
            var shrinkAxis = direction == StripDirection.Bottom
                ? ShrinkAxis.Height : ShrinkAxis.Width;

            Func<NestItem, Box, List<Part>> stripFill = (ni, b) =>
            {
                var trialInner = new DefaultNestEngine(Plate);
                return trialInner.Fill(ni, b, progress, token);
            };

            var shrinkResult = ShrinkFiller.Shrink(stripFill,
                new NestItem { Drawing = stripItem.Drawing, Quantity = stripItem.Quantity },
                stripBox, Plate.PartSpacing, shrinkAxis, token);

            if (shrinkResult.Parts == null || shrinkResult.Parts.Count == 0)
                return result;

            var bestParts = shrinkResult.Parts;
            var bestDim = shrinkResult.Dimension;

            // TODO: Compact strip parts individually to close geometry-based gaps.
            // Disabled pending investigation — remnant finder picks up gaps created
            // by compaction and scatters parts into them.
            // Compactor.CompactIndividual(bestParts, workArea, Plate.PartSpacing);
            //
            // var compactedBox = bestParts.Cast<IBoundable>().GetBoundingBox();
            // bestDim = direction == StripDirection.Bottom
            //     ? compactedBox.Top - workArea.Y
            //     : compactedBox.Right - workArea.X;

            // Build remnant box with spacing gap.
            var spacing = Plate.PartSpacing;
            var remnantBox = direction == StripDirection.Bottom
                ? new Box(workArea.X, workArea.Y + bestDim + spacing,
                    workArea.Width, workArea.Length - bestDim - spacing)
                : new Box(workArea.X + bestDim + spacing, workArea.Y,
                    workArea.Width - bestDim - spacing, workArea.Length);

            // Collect all parts.
            var allParts = new List<Part>(bestParts);

            // If strip item was only partially placed, add leftovers to remainder.
            var placed = bestParts.Count;
            var leftover = stripItem.Quantity > 0 ? stripItem.Quantity - placed : 0;
            var effectiveRemainder = new List<NestItem>(remainderItems);

            if (leftover > 0)
            {
                effectiveRemainder.Add(new NestItem
                {
                    Drawing = stripItem.Drawing,
                    Quantity = leftover
                });
            }

            // Sort remainder by descending bounding box area x quantity.
            effectiveRemainder = effectiveRemainder
                .OrderByDescending(i =>
                {
                    var bb = i.Drawing.Program.BoundingBox();
                    return bb.Area() * (i.Quantity > 0 ? i.Quantity : 1);
                })
                .ToList();

            // Fill remnants
            if (remnantBox.Width > 0 && remnantBox.Length > 0)
            {
                var remnantProgress = progress != null
                    ? new AccumulatingProgress(progress, allParts)
                    : (IProgress<NestProgress>)null;

                var remnantFiller = new RemnantFiller(workArea, spacing);
                remnantFiller.AddObstacles(allParts);

                Func<NestItem, Box, List<Part>> remnantFillFunc = (ni, b) =>
                    ShrinkFill(ni, b, remnantProgress, token);

                var additional = remnantFiller.FillItems(effectiveRemainder,
                    remnantFillFunc, token, remnantProgress);

                allParts.AddRange(additional);
            }

            result.Parts = allParts;
            result.StripBox = direction == StripDirection.Bottom
                ? new Box(workArea.X, workArea.Y, workArea.Width, bestDim)
                : new Box(workArea.X, workArea.Y, bestDim, workArea.Length);
            result.Score = FillScore.Compute(allParts, workArea);

            return result;
        }

        private List<Part> ShrinkFill(NestItem item, Box box,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
            {
                var inner = new DefaultNestEngine(Plate);
                return inner.Fill(ni, b, null, token);
            };

            var heightResult = ShrinkFiller.Shrink(fillFunc, item, box,
                Plate.PartSpacing, ShrinkAxis.Height, token);

            return heightResult.Parts;
        }

    }
}
