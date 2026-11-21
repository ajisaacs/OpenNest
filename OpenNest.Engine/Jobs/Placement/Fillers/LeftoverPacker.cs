using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Placement.Fillers;

/// <summary>
/// Packs what a plate's fills left unplaced into the room around the placed parts: the remnants
/// in <see cref="RemnantFinder"/> order, each offered whatever is still unplaced. The first
/// remnant is often a sliver along an edge that holds none of them, so the first to take a part
/// is packed, the remnants are found again around what it took, and the search ends when no
/// remnant takes anything.
/// </summary>
internal static class LeftoverPacker
{
    /// <summary>
    /// Returns the packed parts. <paramref name="items"/> are not changed: quantities are the
    /// caller's to deduct. With nothing placed and no keep-outs the whole
    /// <paramref name="workArea"/> is packed first.
    /// </summary>
    /// <param name="keepOuts">Rectangles treated as occupied (inflated clamp zones).</param>
    /// <param name="pack">Packs one rectangular area with the items it is given.</param>
    internal static List<Part> Pack(
        Box workArea,
        double partSpacing,
        IEnumerable<Part> placed,
        IReadOnlyList<NestItem> items,
        IReadOnlyList<Box> keepOuts,
        Func<Box, List<NestItem>, IProgress<NestProgress>, CancellationToken, List<Part>> pack,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        var packed = new List<Part>();
        var remaining = items
            .Where(item => item.Quantity > 0)
            .Select(item => new NestItem
            {
                Drawing = item.Drawing,
                Quantity = item.Quantity,
                Priority = item.Priority,
                StepAngle = item.StepAngle,
                RotationStart = item.RotationStart,
                RotationEnd = item.RotationEnd,
            })
            .ToList();
        var sizes = remaining.ToDictionary(
            item => item,
            item => item.Drawing.Program.BoundingBox(),
            ReferenceEqualityComparer.Instance
        );
        var obstacles = placed.Select(part => part.BoundingBox.Offset(partSpacing)).ToList();
        obstacles.AddRange(keepOuts);

        while (remaining.Count > 0 && !token.IsCancellationRequested)
        {
            var areas = obstacles.Count > 0
                ? new RemnantFinder(workArea, obstacles).FindRemnants()
                : new List<Box> { workArea };

            List<Part> taken = null;
            foreach (var area in areas)
            {
                if (token.IsCancellationRequested)
                    break;
                if (area.Width <= 0 || area.Length <= 0)
                    continue;
                if (!remaining.Any(item => CouldHold(area, sizes[item])))
                    continue;

                var parts = pack(area, remaining, progress, token);
                if (parts != null && parts.Count > 0)
                {
                    taken = parts;
                    break;
                }
            }

            if (taken == null)
                break;

            packed.AddRange(taken);
            obstacles.AddRange(taken.Select(part => part.BoundingBox.Offset(partSpacing)));
            var deducted = 0;
            foreach (var item in remaining)
            {
                var count = taken.Count(part => ReferenceEquals(part.BaseDrawing, item.Drawing));
                item.Quantity -= count;
                deducted += count;
            }
            remaining.RemoveAll(item => item.Quantity <= 0);

            // A pack function may answer with parts nobody asked for; without this the demand
            // would never run out.
            if (deducted == 0)
                break;
        }

        return packed;
    }

    /// <summary>
    /// False only when <paramref name="size"/> cannot lie in <paramref name="area"/> at 0 or 90
    /// degrees even with the overhang a packer allows at a work-area edge, so a remnant this
    /// rules out is one <see cref="AreaPacker"/> would leave empty.
    /// </summary>
    private static bool CouldHold(Box area, Box size)
    {
        const double slack = SheetPacker.OverhangAllowance + Tolerance.Epsilon;
        var length = area.Length + slack;
        var width = area.Width + slack;
        return (size.Length <= length && size.Width <= width)
            || (size.Width <= length && size.Length <= width);
    }
}
