using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Iteratively fills remnant boxes with items using a RemnantFinder.
    /// After each fill, re-discovers free rectangles and tries again
    /// until no more items can be placed.
    /// </summary>
    public class RemnantFiller
    {
        private readonly RemnantFinder finder;
        private readonly double spacing;

        public RemnantFiller(Box workArea, double spacing)
        {
            this.spacing = spacing;
            finder = new RemnantFinder(workArea);
        }

        public void AddObstacles(IEnumerable<Part> parts)
        {
            foreach (var part in parts)
                finder.AddObstacle(part.BoundingBox.Offset(spacing));
        }

        public List<Part> FillItems(
            List<NestItem> items,
            Func<NestItem, Box, List<Part>> fillFunc,
            CancellationToken token = default,
            IProgress<NestProgress> progress = null)
        {
            if (items == null || items.Count == 0)
                return new List<Part>();

            var allParts = new List<Part>();
            var madeProgress = true;

            // Track quantities locally — do not mutate the input NestItem objects.
            var localQty = new Dictionary<string, int>();
            foreach (var item in items)
                localQty[item.Drawing.Name] = item.Quantity;

            while (madeProgress && !token.IsCancellationRequested)
            {
                madeProgress = false;

                var minRemnantDim = double.MaxValue;
                foreach (var item in items)
                {
                    var qty = localQty[item.Drawing.Name];
                    if (qty <= 0)
                        continue;
                    var bb = item.Drawing.Program.BoundingBox();
                    var dim = System.Math.Min(bb.Width, bb.Length);
                    if (dim < minRemnantDim)
                        minRemnantDim = dim;
                }

                if (minRemnantDim == double.MaxValue)
                    break;

                var freeBoxes = finder.FindRemnants(minRemnantDim);

                if (freeBoxes.Count == 0)
                    break;

                foreach (var item in items)
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

                        var fillItem = new NestItem { Drawing = item.Drawing, Quantity = qty };
                        var remnantParts = fillFunc(fillItem, box);

                        if (remnantParts != null && remnantParts.Count > 0)
                        {
                            allParts.AddRange(remnantParts);
                            localQty[item.Drawing.Name] = System.Math.Max(0, qty - remnantParts.Count);

                            foreach (var p in remnantParts)
                                finder.AddObstacle(p.BoundingBox.Offset(spacing));

                            madeProgress = true;
                            break;
                        }
                    }

                    if (madeProgress)
                        break;
                }
            }

            return allParts;
        }
    }
}
