using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Placement.Fillers;

internal static class PlateFillOrchestrator
{
    internal static List<Part> Nest(
        Plate plate,
        List<NestItem> items,
        IFillComparer comparer,
        Func<NestItem, Box, IProgress<NestProgress>, CancellationToken, List<Part>> fill,
        Func<Box, List<NestItem>, IProgress<NestProgress>, CancellationToken, List<Part>> pack,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        if (items == null || items.Count == 0)
            return new List<Part>();

        var workArea = plate.WorkArea();
        var allParts = new List<Part>();

        var plateArea = workArea.Width * workArea.Length;

        var fillItems = items
            .Where(item => ShouldFill(item, plate, plateArea))
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.Drawing.Area)
            .ToList();

        var packItems = items.Where(item => !ShouldFill(item, plate, plateArea)).ToList();

        if (fillItems.Count > 0)
        {
            var remnantFiller = new RemnantFiller(workArea, plate.PartSpacing);

            var fillParts = remnantFiller.FillItems(
                fillItems,
                (item, area) => fill(item, area, progress, token),
                token,
                progress
            );
            if (fillParts.Count > 0)
            {
                allParts.AddRange(fillParts);

                DeductPlacedQuantities(fillItems, fillParts);

                var placedObstacles = fillParts
                    .Select(part => part.BoundingBox.Offset(plate.PartSpacing))
                    .ToList();
                var finder = new RemnantFinder(workArea, placedObstacles);
                var remnants = finder.FindRemnants();
                if (remnants.Count > 0)
                    workArea = remnants[0];
                else
                    workArea = new Box(0, 0, 0, 0);
            }
        }

        packItems = packItems.Where(item => item.Quantity > 0).ToList();
        var pairItems = packItems.Where(item => item.Quantity == 2).ToList();
        var regularPackItems = packItems.Where(item => item.Quantity != 2).ToList();

        if (
            regularPackItems.Count > 0
            && workArea.Width > 0
            && workArea.Length > 0
            && !token.IsCancellationRequested
        )
        {
            var packParts = pack(workArea, regularPackItems, progress, token);

            if (packParts.Count > 0)
            {
                allParts.AddRange(packParts);

                DeductPlacedQuantities(regularPackItems, packParts);
            }
        }

        if (pairItems.Count > 0 && !token.IsCancellationRequested)
        {
            var placed = PlaceBestFitPairs(plate, comparer, pairItems, allParts, plate.WorkArea());
            allParts.AddRange(placed);
        }

        Compactor.Settle(allParts, plate.WorkArea(), plate.PartSpacing);

        return allParts;
    }

    private static void DeductPlacedQuantities(List<NestItem> items, List<Part> parts)
    {
        foreach (var item in items)
        {
            var placed = parts.Count(part => ReferenceEquals(part.BaseDrawing, item.Drawing));
            item.Quantity = System.Math.Max(0, item.Quantity - placed);
        }
    }

    private static List<Part> PlaceBestFitPairs(
        Plate plate,
        IFillComparer comparer,
        List<NestItem> pairItems,
        List<Part> existingParts,
        Box fullWorkArea
    )
    {
        var result = new List<Part>();
        var obstacles = existingParts
            .Select(part => part.BoundingBox.Offset(plate.PartSpacing))
            .ToList();
        var finder = new RemnantFinder(fullWorkArea, obstacles);

        foreach (var item in pairItems)
        {
            if (item.Quantity < 2)
                continue;

            var bestFits = BestFitCache.GetOrCompute(
                item.Drawing,
                plate.Size.Length,
                plate.Size.Width,
                plate.PartSpacing
            );

            var canonicalDrawing = CanonicalFrame.AsCanonicalCopy(item.Drawing);

            List<Part> bestPlacement = null;
            Box bestTarget = null;

            foreach (var fit in bestFits)
            {
                if (!fit.Keep)
                    continue;

                var parts = fit.BuildParts(canonicalDrawing);
                var pairBbox = ((IEnumerable<IBoundable>)parts).GetBoundingBox();
                var pairWidth = pairBbox.Width;
                var pairLength = pairBbox.Length;
                var minDimension = System.Math.Min(pairWidth, pairLength);

                var remnants = finder.FindRemnants(minDimension);

                foreach (var remnant in remnants)
                {
                    if (
                        pairWidth <= remnant.Width + Tolerance.Epsilon
                        && pairLength <= remnant.Length + Tolerance.Epsilon
                    )
                    {
                        var offset = remnant.Location - pairBbox.Location;
                        foreach (var part in parts)
                        {
                            part.Offset(offset);
                            part.UpdateBounds();
                        }

                        if (bestPlacement == null || comparer.IsBetter(parts, bestPlacement, remnant))
                        {
                            bestPlacement = parts;
                            bestTarget = remnant;
                        }
                        break;
                    }
                }
            }

            if (bestPlacement == null)
                continue;

            bestPlacement = CanonicalFrame.RebindToOriginal(bestPlacement, item.Drawing);

            result.AddRange(bestPlacement);
            item.Quantity = 0;

            var envelope = ((IEnumerable<IBoundable>)bestPlacement).GetBoundingBox();
            finder.AddObstacle(envelope.Offset(plate.PartSpacing));

            Debug.WriteLine(
                $"[Nest] Placed best-fit pair for {item.Drawing.Name} "
                    + $"at ({bestTarget.X:F1},{bestTarget.Y:F1}), "
                    + $"size {envelope.Width:F1}x{envelope.Length:F1}"
            );
        }

        return result;
    }

    private static bool ShouldFill(NestItem item, Plate plate, double plateArea)
    {
        if (item.Quantity <= 1)
            return false;

        var boundingBox = item.Drawing.Program.BoundingBox();
        var partArea = (boundingBox.Width + plate.PartSpacing)
            * (boundingBox.Length + plate.PartSpacing);
        if (partArea <= 0)
            return false;

        var totalArea = partArea * item.Quantity;

        return totalArea >= plateArea * 0.1;
    }
}
