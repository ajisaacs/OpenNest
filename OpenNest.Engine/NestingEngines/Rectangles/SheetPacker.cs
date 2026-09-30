#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Rectangles;

/// <summary>How the next box is chosen on a sheet.</summary>
internal enum PickMode
{
    /// <summary>Every step places whichever remaining type/orientation scores best anywhere.</summary>
    Global,
    /// <summary>Types in (priority, largest box first) order; each fills until it no longer fits.</summary>
    Ordered,
}

/// <summary>One placed box: which part type, which orientation, and its material bounds' corner.</summary>
internal readonly record struct Placed(BoxType Type, BoxOrientation Orientation, double Left, double Bottom);

/// <summary>A proposed single-sheet layout.</summary>
internal sealed record SheetPlan(
    NestPlateStock Stock,
    IReadOnlyList<Placed> Parts,
    double MaterialArea,
    Box? Envelope,
    FitRule Rule,
    PickMode Mode)
{
    /// <summary>Converts box corners into job poses (rotate about the snapshot origin, then translate).</summary>
    public IEnumerable<(string PartId, double X, double Y, double Rotation)> Poses() =>
        Parts.Select(p => (p.Type.Id, p.Left - p.Orientation.OffsetX, p.Bottom - p.Orientation.OffsetY,
            p.Orientation.Angle));
}

/// <summary>
/// Packs the remaining demand onto one sheet of the given stock with a maximal-rectangles free
/// list. Lower priority numbers are always served first: a higher-number type is only placed
/// when no lower-number type still fits anywhere.
/// </summary>
internal static class SheetPacker
{
    public static SheetPlan Pack(
        IReadOnlyList<BoxType> types, IReadOnlyList<int> remaining, NestPlateStock stock,
        FitRule rule, PickMode mode, CancellationToken token)
    {
        var work = stock.WorkArea;
        var s = stock.PartSpacing;
        var sheet = new MaxRectsSheet(work.Right - work.Left + s, work.Top - work.Bottom + s);
        var left = remaining.ToArray();
        var placed = new List<Placed>();

        if (mode == PickMode.Global)
            PackGlobal(types, left, sheet, s, rule, placed, token);
        else
            PackOrdered(types, left, sheet, s, rule, placed, token);

        var area = 0.0;
        Box? envelope = null;
        foreach (var p in placed)
        {
            area += p.Type.MaterialArea;
            var box = new Box(work.Left + p.Left, work.Bottom + p.Bottom, p.Orientation.Width, p.Orientation.Height);
            envelope = envelope == null ? box : Union(envelope, box);
        }

        var world = placed
            .Select(p => p with { Left = work.Left + p.Left, Bottom = work.Bottom + p.Bottom })
            .ToList();
        return new SheetPlan(stock, world, area, envelope, rule, mode);
    }

    private static void PackGlobal(
        IReadOnlyList<BoxType> types, int[] left, MaxRectsSheet sheet, double s, FitRule rule,
        List<Placed> placed, CancellationToken token)
    {
        // Free space only shrinks, so an orientation that fails once never fits again on this sheet.
        var dead = types.Select(t => new bool[t.Orientations.Count]).ToArray();
        var tiers = types.Select(t => t.Priority).Distinct().Order().ToArray();

        while (true)
        {
            token.ThrowIfCancellationRequested();
            (BoxType Type, int Orientation, Rect Place, double P, double S)? best = null;
            foreach (var tier in tiers)
            {
                foreach (var type in types)
                {
                    if (type.Priority != tier || left[type.Index] == 0)
                        continue;
                    for (var o = 0; o < type.Orientations.Count; o++)
                    {
                        if (dead[type.Index][o])
                            continue;
                        var orientation = type.Orientations[o];
                        var fit = sheet.FindBest(orientation.Width + s, orientation.Height + s, rule);
                        if (fit is not { } f)
                        {
                            dead[type.Index][o] = true;
                            continue;
                        }
                        if (best is not { } b || Better(f.Primary, f.Secondary, type.BoxArea, b.P, b.S, b.Type.BoxArea))
                            best = (type, o, f.Place, f.Primary, f.Secondary);
                    }
                }
                if (best != null)
                    break;
            }

            if (best is not { } chosen)
                return;
            sheet.Place(chosen.Place);
            left[chosen.Type.Index]--;
            placed.Add(new Placed(chosen.Type, chosen.Type.Orientations[chosen.Orientation], chosen.Place.X, chosen.Place.Y));
        }
    }

    private static void PackOrdered(
        IReadOnlyList<BoxType> types, int[] left, MaxRectsSheet sheet, double s, FitRule rule,
        List<Placed> placed, CancellationToken token)
    {
        var order = types
            .Where(t => t.Orientations.Count > 0)
            .OrderBy(t => t.Priority)
            .ThenByDescending(t => t.BoxArea)
            .ThenBy(t => t.Index);

        foreach (var type in order)
        {
            while (left[type.Index] > 0)
            {
                token.ThrowIfCancellationRequested();
                (int Orientation, Rect Place, double P, double S)? best = null;
                for (var o = 0; o < type.Orientations.Count; o++)
                {
                    var orientation = type.Orientations[o];
                    var fit = sheet.FindBest(orientation.Width + s, orientation.Height + s, rule);
                    if (fit is { } f && (best is not { } b || Better(f.Primary, f.Secondary, 0, b.P, b.S, 0)))
                        best = (o, f.Place, f.Primary, f.Secondary);
                }
                if (best is not { } chosen)
                    break;
                sheet.Place(chosen.Place);
                left[type.Index]--;
                placed.Add(new Placed(type, type.Orientations[chosen.Orientation], chosen.Place.X, chosen.Place.Y));
            }
        }
    }

    /// <summary>Lower score wins; on a tie the larger box goes first (strict, so input order breaks full ties).</summary>
    private static bool Better(double p, double s, double area, double bp, double bs, double barea)
    {
        if (p < bp - MaxRectsSheet.Eps) return true;
        if (p > bp + MaxRectsSheet.Eps) return false;
        if (s < bs - MaxRectsSheet.Eps) return true;
        if (s > bs + MaxRectsSheet.Eps) return false;
        return area > barea + MaxRectsSheet.Eps;
    }

    private static Box Union(Box a, Box b)
    {
        var l = System.Math.Min(a.Left, b.Left);
        var bo = System.Math.Min(a.Bottom, b.Bottom);
        var r = System.Math.Max(a.Right, b.Right);
        var t = System.Math.Max(a.Top, b.Top);
        return new Box(l, bo, r - l, t - bo);
    }
}
