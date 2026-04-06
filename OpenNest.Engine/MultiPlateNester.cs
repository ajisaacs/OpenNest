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

    public class MultiPlateNester
    {
        private readonly Plate _template;
        private readonly List<PlateOption> _plateOptions;
        private readonly double _salvageRate;
        private readonly double _minRemnantSize;
        private readonly List<PlateResult> _platePool;
        private readonly IProgress<NestProgress> _progress;
        private readonly CancellationToken _token;

        private MultiPlateNester(
            Plate template, List<PlateOption> plateOptions,
            double salvageRate, double minRemnantSize,
            List<Plate> existingPlates,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            _template = template;
            _plateOptions = plateOptions;
            _salvageRate = salvageRate;
            _minRemnantSize = minRemnantSize;
            _platePool = InitializePlatePool(existingPlates);
            _progress = progress;
            _token = token;
        }

        // --- Static Utility Methods ---

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

        public static List<Box> FindRemnants(Plate plate, double minRemnantSize, bool scrapOnly)
        {
            var remnants = RemnantFinder.FromPlate(plate).FindRemnants();
            return remnants.Where(r => IsScrapRemnant(r, minRemnantSize) == scrapOnly).ToList();
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

        // --- Main Entry Point ---

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
            var nester = new MultiPlateNester(template, plateOptions, salvageRate,
                minRemnantSize, existingPlates, progress, token);
            return nester.Run(items, sortOrder, allowPlateCreation);
        }

        // --- Private Helpers ---

        private static double ScoreZone(Box zone, Box partBounds)
        {
            var fitsNormal = zone.Length >= partBounds.Length && zone.Width >= partBounds.Width;
            var fitsRotated = zone.Length >= partBounds.Width && zone.Width >= partBounds.Length;

            if (!fitsNormal && !fitsRotated)
                return -1;

            return (partBounds.Length * partBounds.Width) / zone.Area();
        }

        private int FillAndPlace(PlateResult pr, Box zone, NestItem item)
        {
            var engine = NestEngineRegistry.Create(pr.Plate);
            var clonedItem = CloneItem(item);
            var parts = engine.Fill(clonedItem, zone, _progress, _token);

            if (parts.Count > 0)
            {
                pr.Plate.Parts.AddRange(parts);
                pr.Parts.AddRange(parts);
                item.Quantity = System.Math.Max(0, item.Quantity - parts.Count);
            }

            return parts.Count;
        }

        private PlateResult CreateNewPlateResult(Plate plate)
        {
            var pr = new PlateResult { Plate = plate, IsNew = true };

            if (_plateOptions != null)
            {
                pr.ChosenSize = _plateOptions.FirstOrDefault(o =>
                    o.Width.IsEqualTo(plate.Size.Width) && o.Length.IsEqualTo(plate.Size.Length));
            }

            return pr;
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

        private static List<PlateResult> InitializePlatePool(List<Plate> existingPlates)
        {
            var pool = new List<PlateResult>();

            if (existingPlates != null)
            {
                foreach (var plate in existingPlates)
                    pool.Add(new PlateResult { Plate = plate, IsNew = false });
            }

            return pool;
        }

        // --- Orchestration ---

        private MultiPlateResult Run(List<NestItem> items, PartSortOrder sortOrder, bool allowPlateCreation)
        {
            var result = new MultiPlateResult();

            if (items == null || items.Count == 0)
                return result;

            var sorted = SortItems(items.Where(i => i.Quantity > 0).ToList(), sortOrder);

            foreach (var item in sorted)
            {
                if (_token.IsCancellationRequested || item.Quantity <= 0)
                    continue;

                var bb = item.Drawing.Program.BoundingBox();

                TryPlaceOnExistingPlates(item, bb);

                var templateClass = Classify(bb, _template.WorkArea());

                if (item.Quantity > 0 && allowPlateCreation && templateClass != PartClass.Small)
                {
                    PlaceOnNewPlates(item, bb);

                    if (item.Quantity > 0 && _plateOptions != null && _plateOptions.Count > 0)
                        TryUpgradeOrNewPlate(item, bb);
                }
            }

            var leftovers = sorted.Where(i => i.Quantity > 0).ToList();

            if (leftovers.Count > 0 && allowPlateCreation && !_token.IsCancellationRequested)
            {
                PackIntoExistingRemnants(leftovers);
                CreateSharedPlates(leftovers);
            }

            foreach (var item in sorted.Where(i => i.Quantity > 0))
                result.UnplacedItems.Add(item);

            result.Plates.AddRange(_platePool.Where(p => p.Parts.Count > 0 || p.IsNew));
            return result;
        }

        private void PackIntoExistingRemnants(List<NestItem> leftovers)
        {
            foreach (var pr in _platePool)
            {
                if (_token.IsCancellationRequested)
                    break;

                var anyPlaced = true;
                while (anyPlaced && !_token.IsCancellationRequested)
                {
                    anyPlaced = false;

                    var remaining = leftovers.Where(i => i.Quantity > 0).ToList();
                    if (remaining.Count == 0)
                        break;

                    var remnants = RemnantFinder.FromPlate(pr.Plate).FindRemnants();
                    if (remnants.Count == 0)
                        break;

                    var engine = NestEngineRegistry.Create(pr.Plate);
                    var cloned = remaining.Select(CloneItem).ToList();
                    var parts = engine.PackArea(remnants[0], cloned, _progress, _token);

                    if (parts.Count > 0)
                    {
                        pr.Plate.Parts.AddRange(parts);
                        pr.Parts.AddRange(parts);
                        anyPlaced = true;

                        foreach (var item in remaining)
                        {
                            var placed = parts.Count(p => p.BaseDrawing.Name == item.Drawing.Name);
                            item.Quantity = System.Math.Max(0, item.Quantity - placed);
                        }
                    }
                }
            }
        }

        private void CreateSharedPlates(List<NestItem> leftovers)
        {
            leftovers.RemoveAll(i => i.Quantity <= 0);

            while (leftovers.Count > 0 && !_token.IsCancellationRequested)
            {
                var plate = CreatePlate(_template, _plateOptions, null);
                var placedAny = false;

                foreach (var item in leftovers)
                {
                    if (item.Quantity <= 0 || _token.IsCancellationRequested)
                        continue;

                    var remnants = !placedAny
                        ? new List<Box> { plate.WorkArea() }
                        : RemnantFinder.FromPlate(plate).FindRemnants();

                    if (remnants.Count == 0)
                        break;

                    var engine = NestEngineRegistry.Create(plate);
                    var clonedItem = CloneItem(item);
                    var parts = engine.Fill(clonedItem, remnants[0], _progress, _token);

                    if (parts.Count > 0)
                    {
                        plate.Parts.AddRange(parts);
                        item.Quantity = System.Math.Max(0, item.Quantity - parts.Count);
                        placedAny = true;
                    }
                }

                if (!placedAny)
                    break;

                var pr = CreateNewPlateResult(plate);
                pr.Parts.AddRange(plate.Parts);
                _platePool.Add(pr);
                leftovers.RemoveAll(i => i.Quantity <= 0);
            }
        }

        private bool TryPlaceOnExistingPlates(NestItem item, Box partBounds)
        {
            var anyPlaced = false;

            while (item.Quantity > 0 && !_token.IsCancellationRequested)
            {
                PlateResult bestPlate = null;
                Box bestZone = null;
                var bestScore = double.MinValue;

                foreach (var pr in _platePool)
                {
                    if (_token.IsCancellationRequested)
                        break;

                    var workArea = pr.Plate.WorkArea();
                    var classification = Classify(partBounds, workArea);

                    var remnants = classification == PartClass.Small
                        ? FindRemnants(pr.Plate, _minRemnantSize, scrapOnly: true)
                        : FindRemnants(pr.Plate, _minRemnantSize, scrapOnly: false);

                    foreach (var zone in remnants)
                    {
                        var score = ScoreZone(zone, partBounds);
                        if (score > bestScore)
                        {
                            bestPlate = pr;
                            bestZone = zone;
                            bestScore = score;
                        }
                    }
                }

                if (bestPlate == null || bestZone == null)
                    break;

                if (FillAndPlace(bestPlate, bestZone, item) == 0)
                    break;

                anyPlaced = true;
            }

            return anyPlaced;
        }

        private bool PlaceOnNewPlates(NestItem item, Box partBounds)
        {
            var anyPlaced = false;

            while (item.Quantity > 0 && !_token.IsCancellationRequested)
            {
                var plate = CreatePlate(_template, _plateOptions, partBounds);
                var workArea = plate.WorkArea();

                if (partBounds.Length > workArea.Length && partBounds.Length > workArea.Width)
                    break;
                if (partBounds.Width > workArea.Width && partBounds.Width > workArea.Length)
                    break;

                var pr = CreateNewPlateResult(plate);

                if (FillAndPlace(pr, workArea, item) == 0)
                    break;

                _platePool.Add(pr);
                anyPlaced = true;
            }

            return anyPlaced;
        }

        private bool TryUpgradeOrNewPlate(NestItem item, Box partBounds)
        {
            if (_plateOptions == null || _plateOptions.Count == 0)
                return false;

            var sortedOptions = _plateOptions.OrderBy(o => o.Cost).ToList();

            foreach (var pr in _platePool.Where(p => p.IsNew && p.ChosenSize != null))
            {
                var currentOption = pr.ChosenSize;
                var currentIdx = sortedOptions.FindIndex(o =>
                    o.Width.IsEqualTo(currentOption.Width) && o.Length.IsEqualTo(currentOption.Length));

                if (currentIdx < 0 || currentIdx >= sortedOptions.Count - 1)
                    continue;

                for (var i = currentIdx + 1; i < sortedOptions.Count; i++)
                {
                    var upgradeOption = sortedOptions[i];
                    var smallestNew = sortedOptions.FirstOrDefault(o =>
                    {
                        var ww = o.Width - _template.EdgeSpacing.Left - _template.EdgeSpacing.Right;
                        var wl = o.Length - _template.EdgeSpacing.Top - _template.EdgeSpacing.Bottom;
                        return (ww >= partBounds.Width && wl >= partBounds.Length)
                            || (ww >= partBounds.Length && wl >= partBounds.Width);
                    });

                    if (smallestNew == null)
                        continue;

                    var utilEst = pr.Plate.Utilization();
                    var decision = EvaluateUpgradeVsNew(currentOption, upgradeOption, smallestNew,
                        _salvageRate, utilEst);

                    if (decision.ShouldUpgrade)
                    {
                        pr.Plate.Size = new Size(upgradeOption.Width, upgradeOption.Length);
                        pr.ChosenSize = upgradeOption;

                        var remainingArea = RemnantFinder.FromPlate(pr.Plate).FindRemnants();

                        if (remainingArea.Count > 0 && FillAndPlace(pr, remainingArea[0], item) > 0)
                            return true;
                    }
                    break;
                }
            }

            return false;
        }
    }
}
