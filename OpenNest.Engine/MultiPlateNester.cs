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
        private readonly List<PlateOption> _sortedOptions;
        private readonly double _salvageRate;
        private readonly double _minRemnantSize;
        private readonly List<PlateResult> _platePool;
        private readonly IProgress<NestProgress> _progress;
        private readonly CancellationToken _token;
        private readonly MultiPlateNestOptions _options;

        private bool HasPlateOptions => _plateOptions != null && _plateOptions.Count > 0;

        private MultiPlateNester(
            MultiPlateNestOptions options,
            List<Plate> existingPlates,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            _options = options;
            _template = options.Template;
            _plateOptions = options.PlateOptions;
            _sortedOptions = options.PlateOptions?.OrderBy(o => o.Cost).ToList();
            _salvageRate = options.SalvageRate;
            _minRemnantSize = options.MinRemnantSize;
            _platePool = InitializePlatePool(existingPlates);
            _progress = progress;
            _token = token;
        }

        // --- Static Utility Methods ---

        public static bool FitsBounds(Box container, Box part)
        {
            var fitsNormal = container.Width >= part.Width - Tolerance.Epsilon
                          && container.Length >= part.Length - Tolerance.Epsilon;
            var fitsRotated = container.Width >= part.Length - Tolerance.Epsilon
                           && container.Length >= part.Width - Tolerance.Epsilon;
            return fitsNormal || fitsRotated;
        }

        public static List<NestItem> SortItems(List<NestItem> items, PartSortOrder sortOrder)
        {
            var withBounds = items.Select(i => (Item: i, Bounds: i.Drawing.Program.BoundingBox())).ToList();

            switch (sortOrder)
            {
                case PartSortOrder.BoundingBoxArea:
                    return withBounds
                        .OrderByDescending(x => x.Bounds.Width * x.Bounds.Length)
                        .Select(x => x.Item)
                        .ToList();

                case PartSortOrder.Size:
                    return withBounds
                        .OrderByDescending(x => System.Math.Max(x.Bounds.Width, x.Bounds.Length))
                        .Select(x => x.Item)
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
                if (FitsBounds(OptionWorkArea(option, template), minBounds))
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
                ShouldUpgrade = upgradeCost <= netNewCost,
                UpgradeCost = upgradeCost,
                NewPlateCost = netNewCost,
            };
        }

        // --- Main Entry Point ---

        public static MultiPlateResult Nest(
            List<NestItem> items,
            MultiPlateNestOptions options,
            List<Plate> existingPlates = null,
            IProgress<NestProgress> progress = null,
            CancellationToken token = default)
        {
            var nester = new MultiPlateNester(options, existingPlates, progress, token);
            return nester.Run(items, options.SortOrder, options.AllowPlateCreation);
        }

        // --- Private Helpers ---

        private static Box OptionWorkArea(PlateOption option, Plate template)
        {
            var w = option.Width - template.EdgeSpacing.Left - template.EdgeSpacing.Right;
            var h = option.Length - template.EdgeSpacing.Top - template.EdgeSpacing.Bottom;
            return new Box(0, 0, w, h);
        }

        private static double ScoreZone(Box zone, Box partBounds)
        {
            if (!FitsBounds(zone, partBounds))
                return -1;

            var cols = (int)(zone.Width / partBounds.Width);
            var rows = (int)(zone.Length / partBounds.Length);
            var colsR = (int)(zone.Width / partBounds.Length);
            var rowsR = (int)(zone.Length / partBounds.Width);
            var estimatedCount = System.Math.Max(cols * rows, colsR * rowsR);

            var utilization = (estimatedCount * partBounds.Width * partBounds.Length) / zone.Area();

            var zoneAspect = zone.Width / zone.Length;
            var partAspect = partBounds.Width / partBounds.Length;
            var aspectMatch = System.Math.Min(zoneAspect, partAspect) / System.Math.Max(zoneAspect, partAspect);

            return utilization * 0.7 + aspectMatch * 0.3;
        }

        private static void DecrementQuantity(NestItem item, int placed)
        {
            item.Quantity = System.Math.Max(0, item.Quantity - placed);
        }

        private int FillAndPlace(PlateResult pr, Box zone, NestItem item)
        {
            var engine = NestEngineRegistry.Create(pr.Plate);
            var clonedItem = CloneItem(item);
            var parts = engine.Fill(clonedItem, zone, _progress, _token);

            if (parts.Count > 0)
            {
                pr.AddParts(parts);
                DecrementQuantity(item, parts.Count);
            }

            return parts.Count;
        }

        private PlateResult CreateNewPlateResult(Plate plate)
        {
            var pr = new PlateResult { Plate = plate, IsNew = true };

            if (HasPlateOptions)
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

        private bool TryWithUpgradedSize(PlateResult pr, PlateOption upgradeOption, Func<List<Box>, bool> tryFill)
        {
            var oldSize = pr.Plate.Size;
            var oldChosenSize = pr.ChosenSize;

            pr.Plate.Size = new Size(upgradeOption.Width, upgradeOption.Length);
            pr.ChosenSize = upgradeOption;

            var remnants = RemnantFinder.FromPlate(pr.Plate).FindRemnants();

            if (remnants.Count > 0 && tryFill(remnants))
                return true;

            pr.Plate.Size = oldSize;
            pr.ChosenSize = oldChosenSize;
            return false;
        }

        private PlateOption FindSmallestFittingOption(Box partBounds)
        {
            return _sortedOptions?.FirstOrDefault(o => FitsBounds(OptionWorkArea(o, _template), partBounds));
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

                    if (item.Quantity > 0 && HasPlateOptions)
                        TryUpgradeOrNewPlate(item, bb);
                }
            }

            var leftovers = sorted.Where(i => i.Quantity > 0).ToList();

            if (leftovers.Count > 0 && allowPlateCreation && !_token.IsCancellationRequested)
            {
                PackIntoExistingRemnants(leftovers);
                CreateSharedPlates(leftovers);
            }

            if (HasPlateOptions && !_token.IsCancellationRequested)
                TryConsolidateTailPlates();

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

                    foreach (var remnant in remnants)
                    {
                        remaining = leftovers.Where(i => i.Quantity > 0).ToList();
                        if (remaining.Count == 0)
                            break;

                        var cloned = remaining.Select(CloneItem).ToList();
                        var parts = engine.PackArea(remnant, cloned, _progress, _token);

                        if (parts.Count > 0)
                        {
                            pr.AddParts(parts);
                            anyPlaced = true;

                            foreach (var item in remaining)
                            {
                                var placed = parts.Count(p => p.BaseDrawing == item.Drawing);
                                DecrementQuantity(item, placed);
                            }
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
                var pr = CreateNewPlateResult(plate);
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

                    foreach (var remnant in remnants)
                    {
                        if (item.Quantity <= 0)
                            break;

                        var clonedItem = CloneItem(item);
                        var parts = engine.Fill(clonedItem, remnant, _progress, _token);

                        if (parts.Count > 0)
                        {
                            pr.AddParts(parts);
                            DecrementQuantity(item, parts.Count);
                            placedAny = true;
                        }
                    }
                }

                if (!placedAny)
                    break;

                _platePool.Add(pr);
                leftovers.RemoveAll(i => i.Quantity <= 0);
            }
        }

        private bool TryPlaceOnExistingPlates(NestItem item, Box partBounds)
        {
            var anyPlaced = false;
            var remnantCache = new Dictionary<PlateResult, List<Box>>();
            PlateResult lastModified = null;

            while (item.Quantity > 0 && !_token.IsCancellationRequested)
            {
                PlateResult bestPlate = null;
                Box bestZone = null;
                var bestScore = double.MinValue;

                foreach (var pr in _platePool)
                {
                    if (_token.IsCancellationRequested)
                        break;

                    if (pr == lastModified || !remnantCache.ContainsKey(pr))
                    {
                        var workArea = pr.Plate.WorkArea();
                        var classification = Classify(partBounds, workArea);

                        remnantCache[pr] = classification == PartClass.Small
                            ? FindRemnants(pr.Plate, _minRemnantSize, scrapOnly: true)
                            : FindRemnants(pr.Plate, _minRemnantSize, scrapOnly: false);
                    }

                    foreach (var zone in remnantCache[pr])
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

                lastModified = bestPlate;
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

                if (!FitsBounds(workArea, partBounds))
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
            if (!HasPlateOptions)
                return false;

            foreach (var pr in _platePool.Where(p => p.IsNew && p.ChosenSize != null))
            {
                var currentOption = pr.ChosenSize;
                var currentIdx = _sortedOptions.FindIndex(o =>
                    o.Width.IsEqualTo(currentOption.Width) && o.Length.IsEqualTo(currentOption.Length));

                if (currentIdx < 0 || currentIdx >= _sortedOptions.Count - 1)
                    continue;

                for (var i = currentIdx + 1; i < _sortedOptions.Count; i++)
                {
                    var upgradeOption = _sortedOptions[i];

                    if (upgradeOption.Width < currentOption.Width - Tolerance.Epsilon
                        || upgradeOption.Length < currentOption.Length - Tolerance.Epsilon)
                        continue;

                    var smallestNew = FindSmallestFittingOption(partBounds);

                    if (smallestNew == null)
                        continue;

                    var utilEst = pr.Plate.Utilization();
                    var decision = EvaluateUpgradeVsNew(currentOption, upgradeOption, smallestNew,
                        _salvageRate, utilEst);

                    if (decision.ShouldUpgrade)
                    {
                        var placed = TryWithUpgradedSize(pr, upgradeOption, remnants =>
                        {
                            foreach (var remnant in remnants)
                            {
                                if (FillAndPlace(pr, remnant, item) > 0)
                                    return true;
                            }
                            return false;
                        });

                        if (placed)
                            return true;
                    }
                }
            }

            return false;
        }

        private void TryConsolidateTailPlates()
        {
            var consolidated = true;
            while (consolidated)
            {
                consolidated = false;

                var activePlates = _platePool.Where(p => p.Parts.Count > 0 && p.IsNew).ToList();
                if (activePlates.Count < 2)
                    return;

                var donors = activePlates.OrderBy(p => p.Plate.Utilization()).ToList();

                foreach (var donor in donors)
                {
                    if (donor.Parts.Count == 0)
                        continue;

                    var donorParts = donor.Parts.ToList();
                    var absorbed = false;

                    foreach (var target in activePlates)
                    {
                        if (target == donor || target.ChosenSize == null || target.Parts.Count == 0)
                            continue;

                        var currentOption = target.ChosenSize;

                        foreach (var upgradeOption in _sortedOptions.Where(o =>
                            o.Width >= currentOption.Width - Tolerance.Epsilon
                            && o.Length >= currentOption.Length - Tolerance.Epsilon
                            && (o.Width > currentOption.Width + Tolerance.Epsilon
                                || o.Length > currentOption.Length + Tolerance.Epsilon)))
                        {
                            absorbed = TryWithUpgradedSize(target, upgradeOption, remnants =>
                            {
                                var engine = NestEngineRegistry.Create(target.Plate);
                                var tempItems = donorParts
                                    .GroupBy(p => p.BaseDrawing)
                                    .Select(g => new NestItem
                                    {
                                        Drawing = g.Key,
                                        Quantity = g.Count(),
                                    })
                                    .ToList();

                                var totalPlaced = new List<Part>();
                                foreach (var remnant in remnants)
                                {
                                    var placed = engine.PackArea(remnant, tempItems, _progress, _token);
                                    totalPlaced.AddRange(placed);

                                    foreach (var ti in tempItems)
                                    {
                                        var count = placed.Count(p => p.BaseDrawing == ti.Drawing);
                                        ti.Quantity = System.Math.Max(0, ti.Quantity - count);
                                    }

                                    if (tempItems.All(ti => ti.Quantity <= 0))
                                        break;
                                }

                                if (totalPlaced.Count >= donorParts.Count)
                                {
                                    target.AddParts(totalPlaced);

                                    foreach (var p in donorParts)
                                        donor.Plate.Parts.Remove(p);
                                    donor.Parts.Clear();
                                    _platePool.Remove(donor);
                                    return true;
                                }

                                return false;
                            });

                            if (absorbed)
                                break;
                        }

                        if (absorbed)
                            break;
                    }

                    if (absorbed)
                    {
                        consolidated = true;
                        break;
                    }
                }
            }
        }
    }
}
