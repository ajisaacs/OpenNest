using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    /// closure that tries both <see cref="ShrinkAxis.Length"/> and
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
            int plateNumber = 0,
            Func<NestItem, Box, List<Part>> widthFillFunc = null)
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

            var wFillFunc = widthFillFunc ?? fillFunc;

            Func<NestItem, Box, List<Part>> shrinkWrapper = (ni, box) =>
            {
                var target = ni.Quantity > 0 ? ni.Quantity : 0;

                // Run height and width shrinks in parallel — they are independent
                // (same inputs, no shared mutable state).
                ShrinkResult heightResult = null;
                ShrinkResult widthResult = null;

                Parallel.Invoke(
                    () => heightResult = ShrinkFiller.Shrink(fillFunc, ni, box, spacing, ShrinkAxis.Length, token,
                        targetCount: target, progress: progress, plateNumber: plateNumber, placedParts: placedSoFar),
                    () => widthResult = ShrinkFiller.Shrink(wFillFunc, ni, box, spacing, ShrinkAxis.Width, token,
                        targetCount: target, progress: progress, plateNumber: plateNumber, placedParts: placedSoFar)
                );

                var heightScore = FillScore.Compute(heightResult.Parts, box);
                var widthScore = FillScore.Compute(widthResult.Parts, box);

                var best = widthScore > heightScore ? widthResult.Parts : heightResult.Parts;

                // Sort pair groups so shorter/narrower groups are closer to the origin,
                // creating a staircase profile that maximizes remnant area.
                // Height shrink → columns vary in height → sort columns.
                // Width shrink → rows vary in width → sort rows.
                if (widthScore > heightScore)
                    SortRowsByWidth(best, spacing);
                else
                    SortColumnsByHeight(best, spacing);

                // Report the winner as overall best so the UI shows it as settled.
                if (progress != null && best != null && best.Count > 0)
                {
                    var allParts = new List<Part>(placedSoFar.Count + best.Count);
                    allParts.AddRange(placedSoFar);
                    allParts.AddRange(best);
                    NestEngineBase.ReportProgress(progress, new ProgressReport
                    {
                        Phase = NestPhase.Custom,
                        PlateNumber = plateNumber,
                        Parts = allParts,
                        WorkArea = box,
                        Description = $"Shrink: {best.Count} parts placed",
                        IsOverallBest = true,
                    });
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

        /// <summary>
        /// Sorts pair columns by height (shortest first on the left) to create
        /// a staircase profile that maximizes usable remnant area.
        /// </summary>
        internal static void SortColumnsByHeight(List<Part> parts, double spacing) =>
            SortStrips(parts, spacing,
                primaryEdge: b => b.Left, extentEdge: b => b.Right,
                sortMetric: MaxTop, stripMin: MinLeft, stripMax: MaxRight,
                makeOffset: d => new Vector(d, 0));

        /// <summary>
        /// Sorts pair rows by width (narrowest first on the bottom) to create
        /// a staircase profile on the right side that maximizes usable remnant area.
        /// </summary>
        internal static void SortRowsByWidth(List<Part> parts, double spacing) =>
            SortStrips(parts, spacing,
                primaryEdge: b => b.Bottom, extentEdge: b => b.Top,
                sortMetric: MaxRight, stripMin: MinBottom, stripMax: MaxTop,
                makeOffset: d => new Vector(0, d));

        private static void SortStrips(
            List<Part> parts, double spacing,
            Func<Box, double> primaryEdge,
            Func<Box, double> extentEdge,
            Func<List<Part>, double> sortMetric,
            Func<List<Part>, double> stripMin,
            Func<List<Part>, double> stripMax,
            Func<double, Vector> makeOffset)
        {
            if (parts == null || parts.Count <= 1)
                return;

            parts.Sort((a, b) => primaryEdge(a.BoundingBox).CompareTo(primaryEdge(b.BoundingBox)));

            var strips = new List<List<Part>>();
            var strip = new List<Part> { parts[0] };
            var stripExtent = extentEdge(parts[0].BoundingBox);

            for (var i = 1; i < parts.Count; i++)
            {
                if (primaryEdge(parts[i].BoundingBox) > stripExtent + spacing / 2)
                {
                    strips.Add(strip);
                    strip = new List<Part> { parts[i] };
                    stripExtent = extentEdge(parts[i].BoundingBox);
                }
                else
                {
                    strip.Add(parts[i]);
                    var extent = extentEdge(parts[i].BoundingBox);
                    if (extent > stripExtent)
                        stripExtent = extent;
                }
            }
            strips.Add(strip);

            if (strips.Count <= 1)
                return;

            var gap = stripMin(strips[1]) - stripMax(strips[0]);

            strips.Sort((a, b) => sortMetric(a).CompareTo(sortMetric(b)));

            var pos = primaryEdge(parts[0].BoundingBox);

            foreach (var s in strips)
            {
                var delta = pos - stripMin(s);

                if (System.Math.Abs(delta) > OpenNest.Math.Tolerance.Epsilon)
                {
                    var offset = makeOffset(delta);
                    foreach (var part in s)
                        part.Offset(offset);
                }

                pos = stripMax(s) + gap;
            }

            parts.Clear();
            foreach (var s in strips)
                parts.AddRange(s);
        }

        private static double MaxTop(List<Part> col)
        {
            var max = double.MinValue;
            foreach (var p in col)
                if (p.BoundingBox.Top > max) max = p.BoundingBox.Top;
            return max;
        }

        private static double MaxRight(List<Part> col)
        {
            var max = double.MinValue;
            foreach (var p in col)
                if (p.BoundingBox.Right > max) max = p.BoundingBox.Right;
            return max;
        }

        private static double MinLeft(List<Part> col)
        {
            var min = double.MaxValue;
            foreach (var p in col)
                if (p.BoundingBox.Left < min) min = p.BoundingBox.Left;
            return min;
        }

        private static double MinBottom(List<Part> row)
        {
            var min = double.MaxValue;
            foreach (var p in row)
                if (p.BoundingBox.Bottom < min) min = p.BoundingBox.Bottom;
            return min;
        }
    }
}
