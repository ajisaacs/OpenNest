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
        private const int MaxShrinkIterations = 20;

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

            // Initial fill using DefaultNestEngine (composition, not inheritance).
            var inner = new DefaultNestEngine(Plate);
            var stripParts = inner.Fill(
                new NestItem { Drawing = stripItem.Drawing, Quantity = stripItem.Quantity },
                stripBox, progress, token);

            if (stripParts == null || stripParts.Count == 0)
                return result;

            // Measure actual strip dimension from placed parts.
            var placedBox = stripParts.Cast<IBoundable>().GetBoundingBox();
            var actualDim = direction == StripDirection.Bottom
                ? placedBox.Top - workArea.Y
                : placedBox.Right - workArea.X;

            var bestParts = stripParts;
            var bestDim = actualDim;
            var targetCount = stripParts.Count;

            // Shrink loop: reduce strip dimension by PartSpacing until count drops.
            for (var i = 0; i < MaxShrinkIterations; i++)
            {
                if (token.IsCancellationRequested)
                    break;

                var trialDim = bestDim - Plate.PartSpacing;
                if (trialDim <= 0)
                    break;

                var trialBox = direction == StripDirection.Bottom
                    ? new Box(workArea.X, workArea.Y, workArea.Width, trialDim)
                    : new Box(workArea.X, workArea.Y, trialDim, workArea.Length);

                var trialInner = new DefaultNestEngine(Plate);
                var trialParts = trialInner.Fill(
                    new NestItem { Drawing = stripItem.Drawing, Quantity = stripItem.Quantity },
                    trialBox, progress, token);

                if (trialParts == null || trialParts.Count < targetCount)
                    break;

                // Same count in a tighter strip — keep going.
                bestParts = trialParts;
                var trialPlacedBox = trialParts.Cast<IBoundable>().GetBoundingBox();
                bestDim = direction == StripDirection.Bottom
                    ? trialPlacedBox.Top - workArea.Y
                    : trialPlacedBox.Right - workArea.X;
            }

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

            // Fill remnant areas iteratively using RemnantFinder.
            // After each fill, re-discover all free rectangles and try again
            // until no more items can be placed.
            if (remnantBox.Width > 0 && remnantBox.Length > 0)
            {
                var remnantProgress = progress != null
                    ? new AccumulatingProgress(progress, allParts)
                    : null;

                var obstacles = allParts.Select(p => p.BoundingBox.Offset(spacing)).ToList();
                var finder = new RemnantFinder(workArea, obstacles);
                var madeProgress = true;

                // Track quantities locally so we don't mutate the shared NestItem objects.
                // TryOrientation is called twice (bottom, left) with the same items.
                var localQty = new Dictionary<string, int>();
                foreach (var item in effectiveRemainder)
                    localQty[item.Drawing.Name] = item.Quantity;

                while (madeProgress && !token.IsCancellationRequested)
                {
                    madeProgress = false;
                    var freeBoxes = finder.FindRemnants(spacing);

                    if (freeBoxes.Count == 0)
                        break;

                    foreach (var item in effectiveRemainder)
                    {
                        if (token.IsCancellationRequested)
                            break;

                        var qty = localQty[item.Drawing.Name];
                        if (qty == 0)
                            continue;

                        var itemBbox = item.Drawing.Program.BoundingBox();
                        var minItemDim = System.Math.Min(itemBbox.Width, itemBbox.Length);

                        foreach (var box in freeBoxes)
                        {
                            if (System.Math.Min(box.Width, box.Length) < minItemDim)
                                continue;

                            var remnantParts = ShrinkFill(
                                new NestItem { Drawing = item.Drawing, Quantity = qty },
                                box, remnantProgress, token);

                            if (remnantParts != null && remnantParts.Count > 0)
                            {
                                allParts.AddRange(remnantParts);
                                localQty[item.Drawing.Name] = System.Math.Max(0, qty - remnantParts.Count);

                                // Update obstacles and re-discover remnants
                                foreach (var p in remnantParts)
                                    finder.AddObstacle(p.BoundingBox.Offset(spacing));

                                madeProgress = true;
                                break; // Re-discover free boxes with updated obstacles
                            }
                        }

                        if (madeProgress)
                            break; // Restart the outer loop to re-discover remnants
                    }
                }
            }

            result.Parts = allParts;
            result.StripBox = direction == StripDirection.Bottom
                ? new Box(workArea.X, workArea.Y, workArea.Width, bestDim)
                : new Box(workArea.X, workArea.Y, bestDim, workArea.Length);
            result.Score = FillScore.Compute(allParts, workArea);

            return result;
        }

        /// <summary>
        /// Fill a box and then shrink it to the tightest area that still fits
        /// the same number of parts. This maximizes leftover space for subsequent fills.
        /// </summary>
        private List<Part> ShrinkFill(NestItem item, Box box,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            var parts = inner.Fill(item, box, progress, token);

            if (parts == null || parts.Count < 2)
                return parts;

            var targetCount = parts.Count;
            var placedBox = parts.Cast<IBoundable>().GetBoundingBox();

            // Try shrinking horizontally
            var bestParts = parts;
            var shrunkWidth = placedBox.Right - box.X;
            var shrunkHeight = placedBox.Top - box.Y;

            for (var i = 0; i < MaxShrinkIterations; i++)
            {
                if (token.IsCancellationRequested)
                    break;

                var trialWidth = shrunkWidth - Plate.PartSpacing;
                if (trialWidth <= 0)
                    break;

                var trialBox = new Box(box.X, box.Y, trialWidth, box.Length);
                var trialInner = new DefaultNestEngine(Plate);
                var trialParts = trialInner.Fill(item, trialBox, null, token);

                if (trialParts == null || trialParts.Count < targetCount)
                    break;

                bestParts = trialParts;
                var trialPlacedBox = trialParts.Cast<IBoundable>().GetBoundingBox();
                shrunkWidth = trialPlacedBox.Right - box.X;
            }

            // Try shrinking vertically
            for (var i = 0; i < MaxShrinkIterations; i++)
            {
                if (token.IsCancellationRequested)
                    break;

                var trialHeight = shrunkHeight - Plate.PartSpacing;
                if (trialHeight <= 0)
                    break;

                var trialBox = new Box(box.X, box.Y, box.Width, trialHeight);
                var trialInner = new DefaultNestEngine(Plate);
                var trialParts = trialInner.Fill(item, trialBox, null, token);

                if (trialParts == null || trialParts.Count < targetCount)
                    break;

                bestParts = trialParts;
                var trialPlacedBox = trialParts.Cast<IBoundable>().GetBoundingBox();
                shrunkHeight = trialPlacedBox.Top - box.Y;
            }

            return bestParts;
        }

        /// <summary>
        /// Wraps an IProgress to prepend previously placed parts to each report,
        /// so the UI shows the full picture (strip + remnant) during remnant fills.
        /// </summary>
        private class AccumulatingProgress : IProgress<NestProgress>
        {
            private readonly IProgress<NestProgress> inner;
            private readonly List<Part> previousParts;

            public AccumulatingProgress(IProgress<NestProgress> inner, List<Part> previousParts)
            {
                this.inner = inner;
                this.previousParts = previousParts;
            }

            public void Report(NestProgress value)
            {
                if (value.BestParts != null && previousParts.Count > 0)
                {
                    var combined = new List<Part>(previousParts.Count + value.BestParts.Count);
                    combined.AddRange(previousParts);
                    combined.AddRange(value.BestParts);
                    value.BestParts = combined;
                    value.BestPartCount = combined.Count;
                }

                inner.Report(value);
            }
        }
    }
}
