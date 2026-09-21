using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Placement.Fillers;

internal class StripPlateFiller : PlateFillerBase
{
    internal StripPlateFiller(Plate plate)
        : base(plate) { }

    public override List<Part> Fill(
        NestItem item,
        Box workArea,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var inner = new DefaultPlateFiller(Plate);
        return inner.Fill(item, workArea, progress, token);
    }

    public override List<Part> Fill(
        List<Part> groupParts,
        Box workArea,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var inner = new DefaultPlateFiller(Plate);
        return inner.Fill(groupParts, workArea, progress, token);
    }

    public override List<Part> PackArea(
        Box box,
        List<NestItem> items,
        IProgress<NestProgress> progress,
        CancellationToken token
    ) => PackAreaCore(box, items, progress, token);

    internal List<Part> PackAreaCore(
        Box box,
        List<NestItem> items,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var inner = new DefaultPlateFiller(Plate);
        return inner.PackArea(box, items, progress, token);
    }

    public override List<Part> Nest(
        List<NestItem> items,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        if (items == null || items.Count == 0)
            return new List<Part>();

        var workArea = Plate.WorkArea();
        var fillItems = items
            .Where(item => item.Quantity != 1)
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.Drawing.Area)
            .ToList();
        var packItems = items.Where(item => item.Quantity == 1).ToList();
        var allParts = new List<Part>();

        if (fillItems.Count > 0)
        {
            Func<NestItem, Box, List<Part>> heightFillFunc = (item, box) =>
            {
                var inner = new RemnantPlateFiller(Plate, RemnantFillPolicy.Horizontal);
                return inner.Fill(item, box, progress, token);
            };
            Func<NestItem, Box, List<Part>> widthFillFunc = (item, box) =>
            {
                var inner = new RemnantPlateFiller(Plate, RemnantFillPolicy.Vertical);
                return inner.Fill(item, box, progress, token);
            };

            var shrinkResult = IterativeShrinkFiller.Fill(
                fillItems,
                workArea,
                heightFillFunc,
                Plate.PartSpacing,
                token,
                progress,
                PlateNumber,
                widthFillFunc
            );

            allParts.AddRange(shrinkResult.Parts);
            Compactor.Settle(allParts, workArea, Plate.PartSpacing);
            packItems.AddRange(shrinkResult.Leftovers);
        }

        packItems = packItems.Where(item => item.Quantity > 0).ToList();
        if (packItems.Count > 0 && !token.IsCancellationRequested)
        {
            var packArea = workArea;
            if (allParts.Count > 0)
            {
                var obstacles = allParts
                    .Select(part => part.BoundingBox.Offset(Plate.PartSpacing))
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

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
                continue;

            var placed = allParts.Count(part => ReferenceEquals(part.BaseDrawing, item.Drawing));
            item.Quantity = System.Math.Max(0, item.Quantity - placed);
        }

        return allParts;
    }
}
