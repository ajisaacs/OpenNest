#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.RectanglePacking;

/// <summary>
/// Packs mixed parts into one rectangular area as their program bounding boxes, at 0 or 90
/// degrees, with the maximal-rectangles packer. Every fit rule and pick mode is tried. The layout
/// that places the most parts, then the most box area, wins, compared tier by tier from the lowest
/// <see cref="NestItem.Priority"/>; the caller's fill comparer breaks ties, so leftover packing
/// keeps the same goal (density, or a clear right or top remnant) as the rest of the fill.
/// </summary>
internal static class AreaPacker
{
    private static readonly FitRule[] Rules =
    {
        FitRule.BestShortSide,
        FitRule.BestLongSide,
        FitRule.BestArea,
        FitRule.BottomLeft,
        FitRule.LeftBottom,
        FitRule.ContactPoint,
    };

    private static readonly PickMode[] Modes = { PickMode.Global, PickMode.Ordered };

    /// <summary>
    /// Returns the packed parts. Items and the area grow by <paramref name="spacing"/> on their
    /// right/top sides, so neighbouring parts are one spacing apart and the last may touch the
    /// area's edge. A cancelled token stops between candidates and keeps the best layout so far.
    /// </summary>
    public static List<Part> Pack(
        Box area, IReadOnlyList<NestItem> items, double spacing, IFillComparer comparer, CancellationToken token)
    {
        var boxes = items.Select(i => i.Drawing.Program.BoundingBox()).ToList();
        var types = new List<PackType>(items.Count);
        var demand = new int[items.Count];
        var packArea = (area.Length + spacing) * (area.Width + spacing);
        for (var i = 0; i < items.Count; i++)
        {
            var w = boxes[i].Length + spacing;
            var h = boxes[i].Width + spacing;
            var sizes = new List<(double, double)>();
            if (w > 0 && h > 0 && boxes[i].Length > 0 && boxes[i].Width > 0)
            {
                sizes.Add((w, h));
                if (!w.IsEqualTo(h))
                    sizes.Add((h, w));
            }
            types.Add(new PackType(items[i].Priority, sizes, boxes[i].Length * boxes[i].Width));
            // More copies than the area could hold by box area alone can never be placed.
            var capacity = sizes.Count > 0 ? (int)System.Math.Min(int.MaxValue, packArea / (w * h)) : 0;
            demand[i] = System.Math.Clamp(items[i].Quantity, 0, capacity);
        }

        var tiers = types.Select(t => t.Priority).Distinct().Order().ToArray();
        List<Part>? best = null;
        (int Count, double Area)[]? bestTiers = null;

        foreach (var mode in Modes)
            foreach (var rule in Rules)
            {
                if (best != null && token.IsCancellationRequested)
                    return best;

                var sheet = new MaxRectsSheet(area.Length + spacing, area.Width + spacing);
                var placed = MaxRectsPacker.Pack(
                    types, (int[])demand.Clone(), sheet, rule, mode, CancellationToken.None);
                var tierScores = tiers
                    .Select(tier =>
                    {
                        var inTier = placed.Where(p => types[p.Type].Priority == tier).ToList();
                        return (inTier.Count, inTier.Sum(p => types[p.Type].Area));
                    })
                    .ToArray();
                var compare = best == null ? 1 : CompareTiers(tierScores, bestTiers!);
                if (compare < 0)
                    continue;
                var parts = ToParts(placed, items, boxes, area);
                if (best == null || compare > 0 || comparer.IsBetter(parts, best, area))
                {
                    best = parts;
                    bestTiers = tierScores;
                }
            }

        return best!;
    }

    /// <summary>Positive when <paramref name="a"/> serves the most urgent tier better, negative when worse, 0 on a tie.</summary>
    private static int CompareTiers((int Count, double Area)[] a, (int Count, double Area)[] b)
    {
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].Count != b[i].Count)
                return a[i].Count > b[i].Count ? 1 : -1;
            if (a[i].Area > b[i].Area + Tolerance.Epsilon)
                return 1;
            if (a[i].Area < b[i].Area - Tolerance.Epsilon)
                return -1;
        }
        return 0;
    }

    private static List<Part> ToParts(
        List<PackPlacement> placed, IReadOnlyList<NestItem> items, IReadOnlyList<Box> boxes, Box area)
    {
        var parts = new List<Part>(placed.Count);
        foreach (var p in placed)
        {
            var part = new Part(items[p.Type].Drawing);
            if (p.Size == 1)
                part.Rotate(Angle.HalfPI);
            var bounds = p.Size == 1 ? part.Program.BoundingBox() : boxes[p.Type];
            part.Offset(new Vector(area.X + p.X, area.Y + p.Y) - bounds.Location);
            parts.Add(part);
        }
        return parts;
    }
}
