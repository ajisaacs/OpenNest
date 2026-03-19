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

                // Run height and width shrinks in parallel — they are independent
                // (same inputs, no shared mutable state).
                ShrinkResult heightResult = null;
                ShrinkResult widthResult = null;

                Parallel.Invoke(
                    () => heightResult = ShrinkFiller.Shrink(fillFunc, ni, box, spacing, ShrinkAxis.Height, token,
                        targetCount: target, progress: progress, plateNumber: plateNumber, placedParts: placedSoFar),
                    () => widthResult = ShrinkFiller.Shrink(fillFunc, ni, box, spacing, ShrinkAxis.Width, token,
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

        /// <summary>
        /// Sorts pair columns by height (shortest first on the left) to create
        /// a staircase profile that maximizes usable remnant area.
        /// </summary>
        internal static void SortColumnsByHeight(List<Part> parts, double spacing)
        {
            if (parts == null || parts.Count <= 1)
                return;

            // Sort parts by Left edge for grouping.
            parts.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

            // Group parts into columns by X overlap.
            var columns = new List<List<Part>>();
            var column = new List<Part> { parts[0] };
            var columnRight = parts[0].BoundingBox.Right;

            for (var i = 1; i < parts.Count; i++)
            {
                if (parts[i].BoundingBox.Left > columnRight + spacing / 2)
                {
                    columns.Add(column);
                    column = new List<Part> { parts[i] };
                    columnRight = parts[i].BoundingBox.Right;
                }
                else
                {
                    column.Add(parts[i]);
                    if (parts[i].BoundingBox.Right > columnRight)
                        columnRight = parts[i].BoundingBox.Right;
                }
            }
            columns.Add(column);

            if (columns.Count <= 1)
                return;

            // Measure inter-column gap from original layout.
            var gap = MinLeft(columns[1]) - MaxRight(columns[0]);

            // Sort columns by height ascending (shortest first).
            columns.Sort((a, b) => MaxTop(a).CompareTo(MaxTop(b)));

            // Reposition columns left-to-right.
            var x = parts[0].BoundingBox.Left; // parts already sorted by Left

            foreach (var col in columns)
            {
                var colLeft = MinLeft(col);
                var dx = x - colLeft;

                if (System.Math.Abs(dx) > OpenNest.Math.Tolerance.Epsilon)
                {
                    var offset = new Vector(dx, 0);
                    foreach (var part in col)
                        part.Offset(offset);
                }

                x = MaxRight(col) + gap;
            }

            // Rebuild the parts list in column order.
            parts.Clear();
            foreach (var col in columns)
                parts.AddRange(col);
        }

        /// <summary>
        /// Sorts pair rows by width (narrowest first on the bottom) to create
        /// a staircase profile on the right side that maximizes usable remnant area.
        /// </summary>
        internal static void SortRowsByWidth(List<Part> parts, double spacing)
        {
            if (parts == null || parts.Count <= 1)
                return;

            // Sort parts by Bottom edge for grouping.
            parts.Sort((a, b) => a.BoundingBox.Bottom.CompareTo(b.BoundingBox.Bottom));

            // Group parts into rows by Y overlap.
            var rows = new List<List<Part>>();
            var row = new List<Part> { parts[0] };
            var rowTop = parts[0].BoundingBox.Top;

            for (var i = 1; i < parts.Count; i++)
            {
                if (parts[i].BoundingBox.Bottom > rowTop + spacing / 2)
                {
                    rows.Add(row);
                    row = new List<Part> { parts[i] };
                    rowTop = parts[i].BoundingBox.Top;
                }
                else
                {
                    row.Add(parts[i]);
                    if (parts[i].BoundingBox.Top > rowTop)
                        rowTop = parts[i].BoundingBox.Top;
                }
            }
            rows.Add(row);

            if (rows.Count <= 1)
                return;

            // Measure inter-row gap from original layout.
            var gap = MinBottom(rows[1]) - MaxTop(rows[0]);

            // Sort rows by width ascending (narrowest first).
            rows.Sort((a, b) => MaxRight(a).CompareTo(MaxRight(b)));

            // Reposition rows bottom-to-top.
            var y = parts[0].BoundingBox.Bottom; // parts already sorted by Bottom

            foreach (var r in rows)
            {
                var rowBottom = MinBottom(r);
                var dy = y - rowBottom;

                if (System.Math.Abs(dy) > OpenNest.Math.Tolerance.Epsilon)
                {
                    var offset = new Vector(0, dy);
                    foreach (var part in r)
                        part.Offset(offset);
                }

                y = MaxTop(r) + gap;
            }

            // Rebuild the parts list in row order.
            parts.Clear();
            foreach (var r in rows)
                parts.AddRange(r);
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
