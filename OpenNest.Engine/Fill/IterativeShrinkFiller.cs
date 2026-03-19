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
            CancellationToken token = default)
        {
            if (items == null || items.Count == 0)
                return new IterativeShrinkResult();

            // RemnantFiller.FillItems skips items with Quantity <= 0 (its localQty
            // check treats them as "done"). Convert unlimited items to an estimated
            // max capacity so they are actually processed.
            var workItems = new List<NestItem>(items.Count);
            var unlimitedDrawings = new HashSet<string>();

            foreach (var item in items)
            {
                if (item.Quantity <= 0)
                {
                    var bbox = item.Drawing.Program.BoundingBox();
                    var estimatedMax = bbox.Area() > 0
                        ? (int)(workArea.Area() / bbox.Area()) * 2
                        : 1000;

                    unlimitedDrawings.Add(item.Drawing.Name);
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

            Func<NestItem, Box, List<Part>> shrinkWrapper = (ni, box) =>
            {
                var heightResult = ShrinkFiller.Shrink(fillFunc, ni, box, spacing, ShrinkAxis.Height, token);
                var widthResult = ShrinkFiller.Shrink(fillFunc, ni, box, spacing, ShrinkAxis.Width, token);

                var heightScore = FillScore.Compute(heightResult.Parts, box);
                var widthScore = FillScore.Compute(widthResult.Parts, box);

                return widthScore > heightScore ? widthResult.Parts : heightResult.Parts;
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
