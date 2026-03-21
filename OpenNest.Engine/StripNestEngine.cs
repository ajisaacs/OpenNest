using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest
{
    public class StripNestEngine : NestEngineBase
    {
        public StripNestEngine(Plate plate) : base(plate)
        {
        }

        public override string Name => "Strip";

        public override string Description => "Iterative shrink-fill nesting for mixed-drawing layouts";

        /// <summary>
        /// Single-item fill delegates to DefaultNestEngine.
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
        /// Multi-drawing iterative shrink-fill strategy.
        /// Each multi-quantity drawing gets shrink-filled into the tightest
        /// sub-region using dual-direction selection. Singles and leftovers
        /// are packed at the end.
        /// </summary>
        public override List<Part> Nest(List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            if (items == null || items.Count == 0)
                return new List<Part>();

            var workArea = Plate.WorkArea();

            // Separate multi-quantity from singles.
            var fillItems = items
                .Where(i => i.Quantity != 1)
                .OrderBy(i => i.Priority)
                .ThenByDescending(i => i.Drawing.Area)
                .ToList();

            var packItems = items
                .Where(i => i.Quantity == 1)
                .ToList();

            var allParts = new List<Part>();

            // Phase 1: Iterative shrink-fill for multi-quantity items.
            if (fillItems.Count > 0)
            {
                // Pass progress through so the UI shows intermediate results
                // during the initial BestFitCache computation and fill phases.
                Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
                {
                    var inner = new DefaultNestEngine(Plate);
                    return inner.Fill(ni, b, progress, token);
                };

                var shrinkResult = IterativeShrinkFiller.Fill(
                    fillItems, workArea, fillFunc, Plate.PartSpacing, token,
                    progress, PlateNumber);

                allParts.AddRange(shrinkResult.Parts);

                // Compact placed parts toward the origin to close gaps.
                Compactor.Settle(allParts, workArea, Plate.PartSpacing);

                // Add unfilled items to pack list.
                packItems.AddRange(shrinkResult.Leftovers);
            }

            // Phase 2: Pack singles + leftovers into remaining space.
            packItems = packItems.Where(i => i.Quantity > 0).ToList();

            if (packItems.Count > 0 && !token.IsCancellationRequested)
            {
                // Reconstruct remaining area from placed parts.
                var packArea = workArea;
                if (allParts.Count > 0)
                {
                    var obstacles = allParts
                        .Select(p => p.BoundingBox.Offset(Plate.PartSpacing))
                        .ToList();
                    var finder = new RemnantFinder(workArea, obstacles);
                    var remnants = finder.FindRemnants();
                    packArea = remnants.Count > 0 ? remnants[0] : new Box(0, 0, 0, 0);
                }

                if (packArea.Width > 0 && packArea.Length > 0)
                {
                    var packParts = PackArea(packArea, packItems, progress, token);
                    allParts.AddRange(packParts);
                }
            }

            // Deduct placed quantities from original items.
            foreach (var item in items)
            {
                if (item.Quantity <= 0)
                    continue;

                var placed = allParts.Count(p => p.BaseDrawing.Name == item.Drawing.Name);
                item.Quantity = System.Math.Max(0, item.Quantity - placed);
            }

            return allParts;
        }
    }
}
