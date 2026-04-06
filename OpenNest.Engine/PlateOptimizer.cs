using OpenNest.Engine;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace OpenNest
{
    public static class PlateOptimizer
    {
        public static PlateOptimizerResult Optimize(
            List<NestItem> items,
            List<PlateOption> plateOptions,
            double salvageRate,
            Plate templatePlate,
            IProgress<NestProgress> progress = null,
            CancellationToken token = default)
        {
            if (items == null || items.Count == 0 || plateOptions == null || plateOptions.Count == 0)
                return null;

            // Find the minimum dimension needed to fit the largest part.
            var minPartWidth = 0.0;
            var minPartLength = 0.0;
            foreach (var item in items)
            {
                if (item.Quantity <= 0) continue;
                var bb = item.Drawing.Program.BoundingBox();
                var shortSide = System.Math.Min(bb.Width, bb.Length);
                var longSide = System.Math.Max(bb.Width, bb.Length);
                if (shortSide > minPartWidth) minPartWidth = shortSide;
                if (longSide > minPartLength) minPartLength = longSide;
            }

            // Sort candidates by cost ascending — try cheapest first.
            var candidates = plateOptions
                .Where(o => FitsPart(o, minPartWidth, minPartLength, templatePlate.EdgeSpacing))
                .OrderBy(o => o.Cost)
                .ToList();

            if (candidates.Count == 0)
                return null;

            PlateOptimizerResult best = null;

            foreach (var option in candidates)
            {
                if (token.IsCancellationRequested)
                    break;

                var result = TryPlateSize(option, items, salvageRate, templatePlate, progress, token);
                if (result == null)
                    continue;

                if (IsBetter(result, best))
                    best = result;

                // Early exit: when salvage is zero, cheapest plate that fits everything wins.
                // With salvage > 0, larger plates may have lower net cost, so keep searching.
                if (salvageRate <= 0)
                {
                    var allPlaced = items.All(i => i.Quantity <= 0 ||
                        result.Parts.Count(p => p.BaseDrawing.Name == i.Drawing.Name) >= i.Quantity);
                    if (allPlaced)
                    {
                        Debug.WriteLine($"[PlateOptimizer] Early exit: {option.Width}x{option.Length} placed all items");
                        break;
                    }
                }
            }

            return best;
        }

        private static bool FitsPart(PlateOption option, double minWidth, double minLength, Spacing edgeSpacing)
        {
            var workW = option.Width - edgeSpacing.Left - edgeSpacing.Right;
            var workL = option.Length - edgeSpacing.Top - edgeSpacing.Bottom;

            // Part fits in either orientation.
            var fitsNormal = workW >= minWidth - Tolerance.Epsilon && workL >= minLength - Tolerance.Epsilon;
            var fitsRotated = workW >= minLength - Tolerance.Epsilon && workL >= minWidth - Tolerance.Epsilon;
            return fitsNormal || fitsRotated;
        }

        private static PlateOptimizerResult TryPlateSize(
            PlateOption option,
            List<NestItem> items,
            double salvageRate,
            Plate templatePlate,
            IProgress<NestProgress> progress,
            CancellationToken token)
        {
            // Create a temporary plate with candidate size + settings from template.
            var tempPlate = new Plate(option.Width, option.Length)
            {
                PartSpacing = templatePlate.PartSpacing,
                EdgeSpacing = new Spacing
                {
                    Left = templatePlate.EdgeSpacing.Left,
                    Right = templatePlate.EdgeSpacing.Right,
                    Top = templatePlate.EdgeSpacing.Top,
                    Bottom = templatePlate.EdgeSpacing.Bottom,
                },
            };

            // Clone items so the dry run doesn't mutate originals.
            var clonedItems = items.Select(i => new NestItem
            {
                Drawing = i.Drawing,  // share Drawing reference for BestFitCache compatibility
                Priority = i.Priority,
                Quantity = i.Quantity,
                StepAngle = i.StepAngle,
                RotationStart = i.RotationStart,
                RotationEnd = i.RotationEnd,
            }).ToList();

            var engine = NestEngineRegistry.Create(tempPlate);
            var parts = engine.Nest(clonedItems, progress, token);

            if (parts == null || parts.Count == 0)
                return null;

            var workArea = tempPlate.WorkArea();
            var plateArea = workArea.Width * workArea.Length;
            var partsArea = 0.0;
            foreach (var part in parts)
                partsArea += part.BoundingBox.Area();

            var remnantArea = plateArea - partsArea;
            var costPerSqUnit = option.Cost / option.Area;
            var netCost = option.Cost - (remnantArea * costPerSqUnit * salvageRate);

            Debug.WriteLine($"[PlateOptimizer] {option.Width}x{option.Length} ${option.Cost}: " +
                $"{parts.Count} parts, util={partsArea / plateArea:P1}, net=${netCost:F2}");

            return new PlateOptimizerResult
            {
                Parts = parts,
                ChosenSize = option,
                NetCost = netCost,
                Utilization = plateArea > 0 ? partsArea / plateArea : 0,
            };
        }

        private static bool IsBetter(PlateOptimizerResult candidate, PlateOptimizerResult current)
        {
            if (current == null) return true;

            // 1. More parts placed is always better.
            if (candidate.Parts.Count != current.Parts.Count)
                return candidate.Parts.Count > current.Parts.Count;

            // 2. Lower net cost.
            if (!candidate.NetCost.IsEqualTo(current.NetCost))
                return candidate.NetCost < current.NetCost;

            // 3. Higher utilization (tighter density) as tiebreak.
            return candidate.Utilization > current.Utilization;
        }
    }
}
