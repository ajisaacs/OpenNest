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
        // Slice 4 (C1): inflated clamp keep-outs act like occupied space for every stage
        // below - fill remnants, pack areas, pair placement - so a zone is never covered.
        var clampKeepOuts = InteractiveClamps.ClearanceRectangles(plate);
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
            var remnantFiller = new RemnantFiller(workArea, plate.PartSpacing, clampKeepOuts);

            var fillParts = remnantFiller.FillItems(
                fillItems,
                (item, area) => fill(item, area, progress, token),
                token,
                progress
            );

            // The keep-outs above cost a row along a clamped edge, which losing the few parts
            // on the clamps may not. A lone drawing with demand left has nothing else to place
            // around the gaps that leaves.
            if (
                clampKeepOuts.Count > 0
                && fillItems.Count == 1
                && fillParts.Count < fillItems[0].Quantity
                && !token.IsCancellationRequested
            )
            {
                fillParts = BetterWithClampHitsDropped(plate, fillItems[0], fillParts, workArea,
                    comparer, fill, progress, token);
            }

            if (fillParts.Count > 0)
            {
                allParts.AddRange(fillParts);

                DeductPlacedQuantities(fillItems, fillParts);

                workArea = LargestRemnant(plate, workArea, fillParts, clampKeepOuts);
            }
        }

        var leftovers = packItems.Where(item => item.Quantity > 0).ToList();

        if (leftovers.Count > 0 && !token.IsCancellationRequested)
        {
            var packFirst = PackThenPairs(plate, leftovers, allParts, workArea, comparer, pack,
                clampKeepOuts, progress, token);
            var chosen = packFirst;

            // Neither order wins every job: pairs reach the gaps a fill leaves before singles
            // take them, but plain rectangles often pack tighter one at a time. The strategy's
            // own ranking of the whole layout decides, and a tie keeps pack-first.
            if (
                leftovers.Any(IsPairable)
                && !token.IsCancellationRequested
            )
            {
                var pairsFirst = PairsThenPack(plate, leftovers, allParts, workArea, comparer, pack,
                    clampKeepOuts, token);
                if (comparer.IsBetter(
                    allParts.Concat(pairsFirst).ToList(),
                    allParts.Concat(packFirst).ToList(),
                    plate.WorkArea()
                ))
                    chosen = pairsFirst;
            }

            allParts.AddRange(chosen);
            DeductPlacedQuantities(leftovers, chosen);
        }

        Compactor.Settle(allParts, plate);

        return allParts;
    }

    private static bool IsPairable(NestItem item) => item.Quantity >= 2 && item.Quantity % 2 == 0;

    private static List<NestItem> Copies(IEnumerable<NestItem> items) =>
        items.Select(item => new NestItem
        {
            Drawing = item.Drawing,
            Priority = item.Priority,
            Quantity = item.Quantity,
            StepAngle = item.StepAngle,
            RotationStart = item.RotationStart,
            RotationEnd = item.RotationEnd,
        }).ToList();

    /// <summary>Packs everything but quantity-two items, then places those as pairs.</summary>
    private static List<Part> PackThenPairs(
        Plate plate,
        List<NestItem> leftovers,
        List<Part> existingParts,
        Box workArea,
        IFillComparer comparer,
        Func<Box, List<NestItem>, IProgress<NestProgress>, CancellationToken, List<Part>> pack,
        IReadOnlyList<Box> clampKeepOuts,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var items = Copies(leftovers);
        var placed = new List<Part>();
        var packItems = items.Where(item => item.Quantity != 2).ToList();

        if (packItems.Count > 0 && workArea.Width > 0 && workArea.Length > 0)
            placed.AddRange(pack(workArea, packItems, progress, token));

        var pairItems = items.Where(item => item.Quantity == 2).ToList();
        if (pairItems.Count > 0 && !token.IsCancellationRequested)
            placed.AddRange(PlaceBestFitPairs(plate, comparer, pairItems,
                existingParts.Concat(placed).ToList(), plate.WorkArea(), clampKeepOuts));

        return placed;
    }

    /// <summary>
    /// Places every even quantity as pairs, largest box first so a part that fits a gap reaches
    /// it before a smaller one takes it, then packs the rest around them.
    /// </summary>
    private static List<Part> PairsThenPack(
        Plate plate,
        List<NestItem> leftovers,
        List<Part> existingParts,
        Box workArea,
        IFillComparer comparer,
        Func<Box, List<NestItem>, IProgress<NestProgress>, CancellationToken, List<Part>> pack,
        IReadOnlyList<Box> clampKeepOuts,
        CancellationToken token
    )
    {
        var items = Copies(leftovers);
        var pairItems = items
            .Where(IsPairable)
            .OrderByDescending(item => item.Drawing.Program.BoundingBox().Area())
            .ToList();

        var placed = PlaceBestFitPairs(plate, comparer, pairItems, existingParts, plate.WorkArea(), clampKeepOuts);
        if (placed.Count > 0)
            workArea = LargestRemnant(plate, plate.WorkArea(), existingParts.Concat(placed), clampKeepOuts);

        var packItems = items.Where(item => item.Quantity > 0).ToList();
        if (
            packItems.Count > 0
            && workArea.Width > 0
            && workArea.Length > 0
            && !token.IsCancellationRequested
        )
            placed.AddRange(pack(workArea, packItems, null, token));

        return placed;
    }

    private static Box LargestRemnant(
        Plate plate,
        Box area,
        IEnumerable<Part> parts,
        IReadOnlyList<Box> clampKeepOuts
    )
    {
        var obstacles = parts.Select(part => part.BoundingBox.Offset(plate.PartSpacing)).ToList();
        obstacles.AddRange(clampKeepOuts);
        var remnants = new RemnantFinder(area, obstacles).FindRemnants();
        return remnants.Count > 0 ? remnants[0] : new Box(0, 0, 0, 0);
    }

    private static List<Part> BetterWithClampHitsDropped(
        Plate plate,
        NestItem item,
        List<Part> current,
        Box workArea,
        IFillComparer comparer,
        Func<NestItem, Box, IProgress<NestProgress>, CancellationToken, List<Part>> fill,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var demand = new List<NestItem> { item };
        bool IsBetter(List<Part> candidate, List<Part> floor) =>
            comparer.IsBetter(candidate, floor, workArea);

        var dropped = PlateFillService.FillDroppingClampHits(
            (demanded, area, sink) => fill(demanded, area, sink, token),
            plate, item, demand, workArea,
            PlateFillService.ClearProgress(progress, plate, workArea, demand, 1, current, IsBetter),
            token
        );
        return IsBetter(dropped, current) ? dropped : current;
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
        Box fullWorkArea,
        IReadOnlyList<Box> clampKeepOuts = null
    )
    {
        var result = new List<Part>();
        var obstacles = existingParts
            .Select(part => part.BoundingBox.Offset(plate.PartSpacing))
            .ToList();
        if (clampKeepOuts != null)
            obstacles.AddRange(clampKeepOuts);
        var finder = new RemnantFinder(fullWorkArea, obstacles);
        var layout = new List<Part>(existingParts);

        foreach (var item in pairItems)
        {
            var bestFits = BestFitCache.GetOrCompute(
                item.Drawing,
                plate.Size.Length,
                plate.Size.Width,
                plate.PartSpacing
            );

            var canonicalDrawing = CanonicalFrame.AsCanonicalCopy(item.Drawing);

            // A part free to rotate may lie along either sheet axis as a pair.
            var turns = item.StepAngle == 0 && item.RotationStart == 0 && item.RotationEnd == 0
                ? new[] { 0, Angle.HalfPI }
                : new[] { 0.0 };

            while (item.Quantity >= 2)
            {
                List<Part> bestPlacement = null;
                Box bestTarget = null;

                foreach (var fit in bestFits)
                {
                    if (!fit.Keep)
                        continue;

                    foreach (var turn in turns)
                    {
                        var probe = ((IEnumerable<IBoundable>)BuildPair(fit, canonicalDrawing, turn)).GetBoundingBox();
                        var pairWidth = probe.Width;
                        var pairLength = probe.Length;

                        foreach (var remnant in finder.FindRemnants(System.Math.Min(pairWidth, pairLength)))
                        {
                            if (
                                pairWidth > remnant.Width + Tolerance.Epsilon
                                || pairLength > remnant.Length + Tolerance.Epsilon
                            )
                                continue;

                            var parts = BuildPair(fit, canonicalDrawing, turn);
                            var offset = remnant.Location - probe.Location;
                            foreach (var part in parts)
                            {
                                part.Offset(offset);
                                part.UpdateBounds();
                            }

                            // Ranked with the layout around it: alone, a pair's own extent says
                            // nothing about whether it stays inside the layout or lengthens it.
                            if (
                                bestPlacement == null
                                || comparer.IsBetter(layout.Concat(parts).ToList(), layout.Concat(bestPlacement).ToList(), fullWorkArea)
                            )
                            {
                                bestPlacement = parts;
                                bestTarget = remnant;
                            }
                        }
                    }
                }

                if (bestPlacement == null)
                    break;

                bestPlacement = CanonicalFrame.RebindToOriginal(bestPlacement, item.Drawing);
                CompactPair(bestPlacement, layout, plate, fullWorkArea);

                result.AddRange(bestPlacement);
                layout.AddRange(bestPlacement);
                item.Quantity -= 2;

                var envelope = ((IEnumerable<IBoundable>)bestPlacement).GetBoundingBox();
                finder.AddObstacle(envelope.Offset(plate.PartSpacing));

                Debug.WriteLine(
                    $"[Nest] Placed best-fit pair for {item.Drawing.Name} "
                        + $"at ({bestTarget.X:F1},{bestTarget.Y:F1}), "
                        + $"size {envelope.Width:F1}x{envelope.Length:F1}"
                );
            }
        }

        return result;
    }

    /// <summary>
    /// Slides a pair placed by its bounding box left then down against the true outlines around
    /// it, so it takes the space its box could not. The whole-layout settle cannot: it moves
    /// everything as one group. Clamp keep-outs stay walls, here at the part spacing.
    /// </summary>
    private static void CompactPair(List<Part> pair, List<Part> layout, Plate plate, Box workArea)
    {
        const double threshold = 0.01;
        const int maxIterations = 20;

        var obstacles = layout.Concat(plate.Parts).Concat(InteractiveClamps.ObstacleParts(plate)).ToList();

        for (var i = 0; i < maxIterations; i++)
        {
            var moved = Compactor.Push(pair, obstacles, workArea, plate.PartSpacing, PushDirection.Left)
                + Compactor.Push(pair, obstacles, workArea, plate.PartSpacing, PushDirection.Down);
            if (moved < threshold)
                break;
        }
    }

    private static List<Part> BuildPair(BestFitResult fit, Drawing drawing, double turn)
    {
        var parts = fit.BuildParts(drawing);
        if (turn != 0)
            foreach (var part in parts)
            {
                part.Rotate(turn, new Vector());
                part.UpdateBounds();
            }
        return parts;
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
