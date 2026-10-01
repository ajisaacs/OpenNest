#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.RectanglePacking;

/// <summary>How the next box is chosen on a sheet.</summary>
internal enum PickMode
{
    /// <summary>Every step places whichever remaining type/size scores best anywhere.</summary>
    Global,

    /// <summary>Types in (priority, largest box first) order; each fills until it no longer fits.</summary>
    Ordered,
}

/// <summary>A box type to pack.</summary>
/// <param name="Priority">Demand tier; lower numbers are served first.</param>
/// <param name="Sizes">Allowed packing sizes (one per orientation), already grown by any spacing.</param>
/// <param name="Area">Ranking area: larger boxes win score ties and go first in ordered mode.</param>
internal sealed record PackType(int Priority, IReadOnlyList<(double Width, double Height)> Sizes, double Area);

/// <summary>One placed box: type and size index, and its corner in sheet-local coordinates.</summary>
internal readonly record struct PackPlacement(int Type, int Size, double X, double Y);

/// <summary>
/// Packs box types onto one <see cref="MaxRectsSheet"/> under a fit rule and pick mode. Lower
/// priority numbers are always served first: a higher-number type is only placed when no
/// lower-number type still fits anywhere. Deterministic: ties resolve by input order.
/// </summary>
internal static class MaxRectsPacker
{
    /// <param name="left">Remaining count per type, indexed like <paramref name="types"/>; decremented as boxes are placed.</param>
    public static List<PackPlacement> Pack(
        IReadOnlyList<PackType> types,
        int[] left,
        MaxRectsSheet sheet,
        FitRule rule,
        PickMode mode,
        CancellationToken token
    )
    {
        var placed = new List<PackPlacement>();
        if (mode == PickMode.Global)
            PackGlobal(types, left, sheet, rule, placed, token);
        else
            PackOrdered(types, left, sheet, rule, placed, token);
        return placed;
    }

    private static void PackGlobal(
        IReadOnlyList<PackType> types,
        int[] left,
        MaxRectsSheet sheet,
        FitRule rule,
        List<PackPlacement> placed,
        CancellationToken token
    )
    {
        // Free space only shrinks, so a size that fails once never fits again on this sheet.
        var dead = types.Select(t => new bool[t.Sizes.Count]).ToArray();
        var tiers = types.Select(t => t.Priority).Distinct().Order().ToArray();

        while (true)
        {
            token.ThrowIfCancellationRequested();
            (int Type, int Size, Rect Place, double P, double S)? best = null;
            foreach (var tier in tiers)
            {
                for (var t = 0; t < types.Count; t++)
                {
                    var type = types[t];
                    if (type.Priority != tier || left[t] == 0)
                        continue;
                    for (var o = 0; o < type.Sizes.Count; o++)
                    {
                        if (dead[t][o])
                            continue;
                        var (w, h) = type.Sizes[o];
                        var fit = sheet.FindBest(w, h, rule);
                        if (fit is not { } f)
                        {
                            dead[t][o] = true;
                            continue;
                        }
                        if (
                            best is not { } b
                            || Better(f.Primary, f.Secondary, type.Area, b.P, b.S, types[b.Type].Area)
                        )
                            best = (t, o, f.Place, f.Primary, f.Secondary);
                    }
                }
                if (best != null)
                    break;
            }

            if (best is not { } chosen)
                return;
            sheet.Place(chosen.Place);
            left[chosen.Type]--;
            placed.Add(new PackPlacement(chosen.Type, chosen.Size, chosen.Place.X, chosen.Place.Y));
        }
    }

    private static void PackOrdered(
        IReadOnlyList<PackType> types,
        int[] left,
        MaxRectsSheet sheet,
        FitRule rule,
        List<PackPlacement> placed,
        CancellationToken token
    )
    {
        var order = Enumerable
            .Range(0, types.Count)
            .Where(t => types[t].Sizes.Count > 0)
            .OrderBy(t => types[t].Priority)
            .ThenByDescending(t => types[t].Area)
            .ThenBy(t => t);

        foreach (var t in order)
        {
            var type = types[t];
            while (left[t] > 0)
            {
                token.ThrowIfCancellationRequested();
                (int Size, Rect Place, double P, double S)? best = null;
                for (var o = 0; o < type.Sizes.Count; o++)
                {
                    var (w, h) = type.Sizes[o];
                    var fit = sheet.FindBest(w, h, rule);
                    if (fit is { } f && (best is not { } b || Better(f.Primary, f.Secondary, 0, b.P, b.S, 0)))
                        best = (o, f.Place, f.Primary, f.Secondary);
                }
                if (best is not { } chosen)
                    break;
                sheet.Place(chosen.Place);
                left[t]--;
                placed.Add(new PackPlacement(t, chosen.Size, chosen.Place.X, chosen.Place.Y));
            }
        }
    }

    /// <summary>Lower score wins; on a tie the larger box goes first (strict, so input order breaks full ties).</summary>
    private static bool Better(double p, double s, double area, double bp, double bs, double barea)
    {
        if (p < bp - MaxRectsSheet.Eps)
            return true;
        if (p > bp + MaxRectsSheet.Eps)
            return false;
        if (s < bs - MaxRectsSheet.Eps)
            return true;
        if (s > bs + MaxRectsSheet.Eps)
            return false;
        return area > barea + MaxRectsSheet.Eps;
    }
}
