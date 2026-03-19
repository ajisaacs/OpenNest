using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Result returned by <see cref="IterativeShrinkFiller.Fill"/>.
    /// </summary>
    public class IterativeShrinkResult
    {
        public List<Part> Parts { get; set; } = new List<Part>();
        public List<NestItem> Leftovers { get; set; } = new List<NestItem>();
    }

    /// <summary>
    /// Composes <see cref="RemnantFiller"/> and <see cref="ShrinkFiller"/> with
    /// dual-direction shrink selection. Wraps the caller's fill function in a
    /// closure that tries both <see cref="ShrinkAxis.Height"/> and
    /// <see cref="ShrinkAxis.Width"/>, picks the better <see cref="FillScore"/>,
    /// and passes the wrapper to <see cref="RemnantFiller.FillItems"/>.
    /// </summary>
    public static class IterativeShrinkFiller
    {
        public static IterativeShrinkResult Fill(
            List<NestItem> items,
            Box workArea,
            Func<NestItem, Box, List<Part>> fillFunc,
            double spacing,
            CancellationToken token = default,
            IProgress<NestProgress> progress = null,
            int plateNumber = 0)
        {
            if (items == null || items.Count == 0)
                return new IterativeShrinkResult();

            // RemnantFiller.FillItems skips items with Quantity == 0 (its localQty
            // check treats them as done). Convert unlimited items (Quantity <= 0)
            // to an estimated max capacity so they are actually processed.
            var workItems = new List<NestItem>(items.Count);

            foreach (var item in items)
            {
                if (item.Quantity <= 0)
                {
                    var bbox = item.Drawing.Program.BoundingBox();
                    var estimatedMax = bbox.Area() > 0
                        ? (int)(workArea.Area() / bbox.Area()) * 2
                        : 1000;

                    workItems.Add(new NestItem
                    {
                        Drawing = item.Drawing,
                        Quantity = System.Math.Max(1, estimatedMax),
                        Priority = item.Priority,
                        StepAngle = item.StepAngle,
                        RotationStart = item.RotationStart,
                        RotationEnd = item.RotationEnd
                    });
                }
                else
                {
                    workItems.Add(item);
                }
            }

            var filler = new RemnantFiller(workArea, spacing);

            // Track parts placed by previous items so ShrinkFiller can
            // include them in progress reports.
            var placedSoFar = new List<Part>();

            Func<NestItem, Box, List<Part>> shrinkWrapper = (ni, box) =>
            {
                var target = ni.Quantity > 0 ? ni.Quantity : 0;
                var heightResult = ShrinkFiller.Shrink(fillFunc, ni, box, spacing, ShrinkAxis.Height, token,
                    targetCount: target, progress: progress, plateNumber: plateNumber, placedParts: placedSoFar);
                var widthResult = ShrinkFiller.Shrink(fillFunc, ni, box, spacing, ShrinkAxis.Width, token,
                    targetCount: target, progress: progress, plateNumber: plateNumber, placedParts: placedSoFar);

                var heightScore = FillScore.Compute(heightResult.Parts, box);
                var widthScore = FillScore.Compute(widthResult.Parts, box);

                var best = widthScore > heightScore ? widthResult.Parts : heightResult.Parts;

                // Report the winner as overall best so the UI shows it as settled.
                if (progress != null && best != null && best.Count > 0)
                {
                    var allParts = new List<Part>(placedSoFar.Count + best.Count);
                    allParts.AddRange(placedSoFar);
                    allParts.AddRange(best);
                    NestEngineBase.ReportProgress(progress, NestPhase.Custom, plateNumber,
                        allParts, box, $"Shrink: {best.Count} parts placed", isOverallBest: true);
                }

                // Accumulate for the next item's progress reports.
                placedSoFar.AddRange(best);

                return best;
            };

            var placed = filler.FillItems(workItems, shrinkWrapper, token);

            // Build leftovers: compare placed count to original quantities.
            // RemnantFiller.FillItems does NOT mutate NestItem.Quantity.
            var leftovers = new List<NestItem>();
            foreach (var item in items)
            {
                var placedCount = 0;
                foreach (var p in placed)
                {
                    if (p.BaseDrawing.Name == item.Drawing.Name)
                        placedCount++;
                }

                if (item.Quantity <= 0)
                    continue; // unlimited items are always "satisfied" — no leftover

                var remaining = item.Quantity - placedCount;
                if (remaining > 0)
                {
                    leftovers.Add(new NestItem
                    {
                        Drawing = item.Drawing,
                        Quantity = remaining,
                        Priority = item.Priority,
                        StepAngle = item.StepAngle,
                        RotationStart = item.RotationStart,
                        RotationEnd = item.RotationEnd
                    });
                }
            }

            return new IterativeShrinkResult { Parts = placed, Leftovers = leftovers };
        }
    }
}
