using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public enum PartClass
    {
        Large,
        Medium,
        Small,
    }

    public static class MultiPlateNester
    {
        public static List<NestItem> SortItems(List<NestItem> items, PartSortOrder sortOrder)
        {
            switch (sortOrder)
            {
                case PartSortOrder.BoundingBoxArea:
                    return items
                        .OrderByDescending(i =>
                        {
                            var bb = i.Drawing.Program.BoundingBox();
                            return bb.Width * bb.Length;
                        })
                        .ToList();

                case PartSortOrder.Size:
                    return items
                        .OrderByDescending(i =>
                        {
                            var bb = i.Drawing.Program.BoundingBox();
                            return System.Math.Max(bb.Width, bb.Length);
                        })
                        .ToList();

                default:
                    return items.ToList();
            }
        }

        public static PartClass Classify(Box partBounds, Box workArea)
        {
            var halfWidth = workArea.Width / 2.0;
            var halfLength = workArea.Length / 2.0;

            if (partBounds.Width > halfWidth || partBounds.Length > halfLength)
                return PartClass.Large;

            var workAreaArea = workArea.Width * workArea.Length;
            var partArea = partBounds.Width * partBounds.Length;

            if (partArea > workAreaArea / 9.0)
                return PartClass.Medium;

            return PartClass.Small;
        }

        public static bool IsScrapRemnant(Box remnant, double minRemnantSize)
        {
            return remnant.Width < minRemnantSize && remnant.Length < minRemnantSize;
        }

        public static List<Box> FindScrapZones(Plate plate, double minRemnantSize)
        {
            var finder = RemnantFinder.FromPlate(plate);
            var remnants = finder.FindRemnants();

            var scrap = new List<Box>();
            foreach (var remnant in remnants)
            {
                if (IsScrapRemnant(remnant, minRemnantSize))
                    scrap.Add(remnant);
            }

            return scrap;
        }

        public static List<Box> FindViableRemnants(Plate plate, double minRemnantSize)
        {
            var finder = RemnantFinder.FromPlate(plate);
            var remnants = finder.FindRemnants();

            var viable = new List<Box>();
            foreach (var remnant in remnants)
            {
                if (!IsScrapRemnant(remnant, minRemnantSize))
                    viable.Add(remnant);
            }

            return viable;
        }

        public struct UpgradeDecision
        {
            public bool ShouldUpgrade;
            public double UpgradeCost;
            public double NewPlateCost;
        }

        public static Plate CreatePlate(Plate template, List<PlateOption> options, Box minBounds)
        {
            var plate = new Plate(template.Size)
            {
                PartSpacing = template.PartSpacing,
                Quadrant = template.Quadrant,
            };
            plate.EdgeSpacing = new Spacing
            {
                Left = template.EdgeSpacing.Left,
                Right = template.EdgeSpacing.Right,
                Top = template.EdgeSpacing.Top,
                Bottom = template.EdgeSpacing.Bottom,
            };

            if (options == null || options.Count == 0 || minBounds == null)
                return plate;

            // Find smallest option that fits the part (by cost ascending).
            var sorted = options.OrderBy(o => o.Cost).ToList();

            foreach (var option in sorted)
            {
                var workW = option.Width - template.EdgeSpacing.Left - template.EdgeSpacing.Right;
                var workL = option.Length - template.EdgeSpacing.Top - template.EdgeSpacing.Bottom;

                var fitsNormal = workW >= minBounds.Width - Tolerance.Epsilon
                              && workL >= minBounds.Length - Tolerance.Epsilon;
                var fitsRotated = workW >= minBounds.Length - Tolerance.Epsilon
                               && workL >= minBounds.Width - Tolerance.Epsilon;

                if (fitsNormal || fitsRotated)
                {
                    plate.Size = new Size(option.Width, option.Length);
                    return plate;
                }
            }

            // No option fits — use template size.
            return plate;
        }

        public static UpgradeDecision EvaluateUpgradeVsNew(
            PlateOption currentSize,
            PlateOption upgradeSize,
            PlateOption newPlateSize,
            double salvageRate,
            double estimatedNewPlateUtilization)
        {
            var upgradeCost = upgradeSize.Cost - currentSize.Cost;

            var newPlateCost = newPlateSize.Cost;
            var remnantFraction = 1.0 - estimatedNewPlateUtilization;
            var salvageCredit = remnantFraction * newPlateSize.Cost * salvageRate;
            var netNewCost = newPlateCost - salvageCredit;

            return new UpgradeDecision
            {
                ShouldUpgrade = upgradeCost < netNewCost,
                UpgradeCost = upgradeCost,
                NewPlateCost = netNewCost,
            };
        }

        // --- Main Orchestration ---

        public static MultiPlateResult Nest(
            List<NestItem> items,
            Plate template,
            List<PlateOption> plateOptions,
            double salvageRate,
            PartSortOrder sortOrder,
            double minRemnantSize,
            bool allowPlateCreation,
            List<Plate> existingPlates,
            IProgress<NestProgress> progress,
            CancellationToken token)
        {
            var result = new MultiPlateResult();

            if (items == null || items.Count == 0)
                return result;

            // Initialize plate pool from existing plates.
            var platePool = new List<PlateResult>();

            if (existingPlates != null)
            {
                foreach (var plate in existingPlates)
                    platePool.Add(new PlateResult { Plate = plate, IsNew = false });
            }

            // Sort items by selected order.
            var sorted = SortItems(items.Where(i => i.Quantity > 0).ToList(), sortOrder);

            // Single pass — process each drawing batch.
            foreach (var item in sorted)
            {
                if (token.IsCancellationRequested)
                    break;

                if (item.Quantity <= 0)
                    continue;

                var bb = item.Drawing.Program.BoundingBox();
                var placed = false;

                // Try to place on existing plates in the pool.
                placed = TryPlaceOnExistingPlates(item, bb, platePool, template,
                    minRemnantSize, progress, token);

                // If items remain, try creating new plates.
                if (item.Quantity > 0 && allowPlateCreation)
                {
                    placed = PlaceOnNewPlates(item, bb, platePool, template,
                        plateOptions, minRemnantSize, progress, token) || placed;
                }

                // If items remain, try upgrade-vs-new-plate.
                if (item.Quantity > 0 && allowPlateCreation && plateOptions != null && plateOptions.Count > 0)
                {
                    placed = TryUpgradeOrNewPlate(item, bb, platePool, template,
                        plateOptions, salvageRate, minRemnantSize, progress, token) || placed;
                }

                if (item.Quantity > 0)
                    result.UnplacedItems.Add(item);
            }

            result.Plates.AddRange(platePool.Where(p => p.Parts.Count > 0 || p.IsNew));
            return result;
        }

        private static bool TryPlaceOnExistingPlates(
            NestItem item, Box partBounds,
            List<PlateResult> platePool, Plate template,
            double minRemnantSize,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var anyPlaced = false;

            while (item.Quantity > 0 && !token.IsCancellationRequested)
            {
                // Find the best zone across all plates for this item.
                PlateResult bestPlate = null;
                Box bestZone = null;
                var bestScore = double.MinValue;

                foreach (var pr in platePool)
                {
                    if (token.IsCancellationRequested)
                        break;

                    var workArea = pr.Plate.WorkArea();
                    var classification = Classify(partBounds, workArea);

                    if (classification == PartClass.Large)
                        continue; // Large parts don't go on existing plates — they create new ones.

                    // Get all remnants and try to place in them.
                    var remnants = classification == PartClass.Small
                        ? FindAllRemnants(pr.Plate)
                        : FindViableRemnants(pr.Plate, minRemnantSize);

                    foreach (var zone in remnants)
                    {
                        // Check normal orientation.
                        if (zone.Length >= partBounds.Length && zone.Width >= partBounds.Width)
                        {
                            var score = (partBounds.Length * partBounds.Width) / zone.Area();
                            if (score > bestScore)
                            {
                                bestPlate = pr;
                                bestZone = zone;
                                bestScore = score;
                            }
                        }

                        // Check rotated orientation.
                        if (zone.Length >= partBounds.Width && zone.Width >= partBounds.Length)
                        {
                            var score = (partBounds.Length * partBounds.Width) / zone.Area();
                            if (score > bestScore)
                            {
                                bestPlate = pr;
                                bestZone = zone;
                                bestScore = score;
                            }
                        }
                    }
                }

                if (bestPlate == null || bestZone == null)
                    break;

                // Use the engine to fill into the zone.
                var engine = NestEngineRegistry.Create(bestPlate.Plate);
                var clonedItem = CloneItem(item);
                var parts = engine.Fill(clonedItem, bestZone, progress, token);

                if (parts.Count == 0)
                    break;

                bestPlate.Plate.Parts.AddRange(parts);
                bestPlate.Parts.AddRange(parts);
                item.Quantity = System.Math.Max(0, item.Quantity - parts.Count);
                anyPlaced = true;
            }

            return anyPlaced;
        }

        private static bool PlaceOnNewPlates(
            NestItem item, Box partBounds,
            List<PlateResult> platePool, Plate template,
            List<PlateOption> plateOptions,
            double minRemnantSize,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var anyPlaced = false;

            while (item.Quantity > 0 && !token.IsCancellationRequested)
            {
                var plate = CreatePlate(template, plateOptions, partBounds);
                var workArea = plate.WorkArea();

                // Can't fit on any plate we can create.
                if (partBounds.Length > workArea.Length && partBounds.Length > workArea.Width)
                    break;
                if (partBounds.Width > workArea.Width && partBounds.Width > workArea.Length)
                    break;

                var engine = NestEngineRegistry.Create(plate);
                var clonedItem = CloneItem(item);
                var parts = engine.Fill(clonedItem, workArea, progress, token);

                if (parts.Count == 0)
                    break;

                plate.Parts.AddRange(parts);
                var pr = new PlateResult
                {
                    Plate = plate,
                    IsNew = true,
                };
                pr.Parts.AddRange(parts);

                // Find the PlateOption used (if any).
                if (plateOptions != null)
                {
                    pr.ChosenSize = plateOptions.FirstOrDefault(o =>
                        o.Width.IsEqualTo(plate.Size.Width) && o.Length.IsEqualTo(plate.Size.Length));
                }

                platePool.Add(pr);
                item.Quantity = System.Math.Max(0, item.Quantity - parts.Count);
                anyPlaced = true;
            }

            return anyPlaced;
        }

        private static bool TryUpgradeOrNewPlate(
            NestItem item, Box partBounds,
            List<PlateResult> platePool, Plate template,
            List<PlateOption> plateOptions,
            double salvageRate, double minRemnantSize,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            if (plateOptions == null || plateOptions.Count == 0)
                return false;

            // Find cheapest upgrade candidate among existing new plates.
            var sortedOptions = plateOptions.OrderBy(o => o.Cost).ToList();

            foreach (var pr in platePool.Where(p => p.IsNew && p.ChosenSize != null))
            {
                var currentOption = pr.ChosenSize;
                var currentIdx = sortedOptions.FindIndex(o =>
                    o.Width.IsEqualTo(currentOption.Width) && o.Length.IsEqualTo(currentOption.Length));

                if (currentIdx < 0 || currentIdx >= sortedOptions.Count - 1)
                    continue;

                // Try each larger option.
                for (var i = currentIdx + 1; i < sortedOptions.Count; i++)
                {
                    var upgradeOption = sortedOptions[i];
                    var smallestNew = sortedOptions.FirstOrDefault(o =>
                    {
                        var ww = o.Width - template.EdgeSpacing.Left - template.EdgeSpacing.Right;
                        var wl = o.Length - template.EdgeSpacing.Top - template.EdgeSpacing.Bottom;
                        return (ww >= partBounds.Width && wl >= partBounds.Length)
                            || (ww >= partBounds.Length && wl >= partBounds.Width);
                    });

                    if (smallestNew == null)
                        continue;

                    var utilEst = pr.Plate.Utilization();
                    var decision = EvaluateUpgradeVsNew(currentOption, upgradeOption, smallestNew,
                        salvageRate, utilEst);

                    if (decision.ShouldUpgrade)
                    {
                        // Upgrade the plate size and re-nest with remaining items.
                        pr.Plate.Size = new Size(upgradeOption.Width, upgradeOption.Length);
                        pr.ChosenSize = upgradeOption;

                        var engine = NestEngineRegistry.Create(pr.Plate);
                        var clonedItem = CloneItem(item);
                        var remainingArea = RemnantFinder.FromPlate(pr.Plate).FindRemnants();

                        if (remainingArea.Count > 0)
                        {
                            var parts = engine.Fill(clonedItem, remainingArea[0], progress, token);
                            if (parts.Count > 0)
                            {
                                pr.Plate.Parts.AddRange(parts);
                                pr.Parts.AddRange(parts);
                                item.Quantity = System.Math.Max(0, item.Quantity - parts.Count);
                                return true;
                            }
                        }
                    }
                    break; // Only try next size up.
                }
            }

            return false;
        }

        private static List<Box> FindAllRemnants(Plate plate)
        {
            var finder = RemnantFinder.FromPlate(plate);
            return finder.FindRemnants();
        }

        private static NestItem CloneItem(NestItem item)
        {
            return new NestItem
            {
                Drawing = item.Drawing,
                Priority = item.Priority,
                Quantity = item.Quantity,
                StepAngle = item.StepAngle,
                RotationStart = item.RotationStart,
                RotationEnd = item.RotationEnd,
            };
        }
    }
}
