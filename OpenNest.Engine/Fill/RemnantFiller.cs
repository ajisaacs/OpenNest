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

            // Track quantities locally — do not mutate the input NestItem objects.
            var localQty = BuildLocalQuantities(items);

            while (!token.IsCancellationRequested)
            {
                var minDim = FindMinItemDimension(items, localQty);
                if (minDim == double.MaxValue)
                    break;

                var freeBoxes = finder.FindRemnants(minDim);
                if (freeBoxes.Count == 0)
                    break;

                if (!TryFillOneItem(items, freeBoxes, localQty, fillFunc, allParts, token))
                    break;
            }

            return allParts;
        }

        private static Dictionary<string, int> BuildLocalQuantities(List<NestItem> items)
        {
            var localQty = new Dictionary<string, int>(items.Count);
            foreach (var item in items)
                localQty[item.Drawing.Name] = item.Quantity;
            return localQty;
        }

        private static double FindMinItemDimension(List<NestItem> items, Dictionary<string, int> localQty)
        {
            var minDim = double.MaxValue;
            foreach (var item in items)
            {
                if (localQty[item.Drawing.Name] <= 0)
                    continue;
                var bb = item.Drawing.Program.BoundingBox();
                var dim = System.Math.Min(bb.Width, bb.Length);
                if (dim < minDim)
                    minDim = dim;
            }
            return minDim;
        }

        private bool TryFillOneItem(
            List<NestItem> items,
            List<Box> freeBoxes,
            Dictionary<string, int> localQty,
            Func<NestItem, Box, List<Part>> fillFunc,
            List<Part> allParts,
            CancellationToken token)
        {
            foreach (var item in items)
            {
                if (token.IsCancellationRequested)
                    return false;

                var qty = localQty[item.Drawing.Name];
                if (qty <= 0)
                    continue;

                var placed = TryFillInRemnants(item, qty, freeBoxes, fillFunc);
                if (placed == null)
                    continue;

                // Remove the topmost bounding box part to create a clean
                // rectangular obstacle boundary. Without this, gaps between
                // individual bounding boxes cause the next drawing to fill
                // into inter-row spaces, producing an interleaved layout.
                if (placed.Count > 1)
                    RemoveTopmostPart(placed);

                allParts.AddRange(placed);
                localQty[item.Drawing.Name] = System.Math.Max(0, qty - placed.Count);

                // Add the envelope of all placed parts as a single obstacle
                // rather than individual bounding boxes, preventing the
                // remnant finder from seeing inter-part gaps.
                var envelope = ComputeEnvelope(placed, spacing);
                finder.AddObstacle(envelope);

                return true;
            }

            return false;
        }

        private static void RemoveTopmostPart(List<Part> parts)
        {
            var topIdx = 0;

            for (var i = 1; i < parts.Count; i++)
            {
                if (parts[i].BoundingBox.Top > parts[topIdx].BoundingBox.Top)
                    topIdx = i;
            }

            parts.RemoveAt(topIdx);
        }

        private static Box ComputeEnvelope(List<Part> parts, double spacing)
        {
            var left = double.MaxValue;
            var bottom = double.MaxValue;
            var right = double.MinValue;
            var top = double.MinValue;

            foreach (var p in parts)
            {
                var bb = p.BoundingBox;
                if (bb.Left < left) left = bb.Left;
                if (bb.Bottom < bottom) bottom = bb.Bottom;
                if (bb.Right > right) right = bb.Right;
                if (bb.Top > top) top = bb.Top;
            }

            return new Box(left - spacing, bottom - spacing,
                right - left + spacing * 2, top - bottom + spacing * 2);
        }

        private static List<Part> TryFillInRemnants(
            NestItem item,
            int qty,
            List<Box> freeBoxes,
            Func<NestItem, Box, List<Part>> fillFunc)
        {
            var itemBbox = item.Drawing.Program.BoundingBox();

            foreach (var box in freeBoxes)
            {
                var fitsNormal = box.Width >= itemBbox.Width && box.Length >= itemBbox.Length;
                var fitsRotated = box.Width >= itemBbox.Length && box.Length >= itemBbox.Width;
                if (!fitsNormal && !fitsRotated)
                    continue;

                var fillItem = new NestItem { Drawing = item.Drawing, Quantity = qty };
                var parts = fillFunc(fillItem, box);

                if (parts != null && parts.Count > 0)
                    return parts;
            }

            return null;
        }
    }
}
