#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
namespace OpenNest.Engine.RectanglePacking;

/// <summary>Axis-aligned rectangle in sheet-local packing coordinates.</summary>
internal readonly record struct Rect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Top => Y + H;

    public bool Contains(Rect other) =>
        other.X >= X - MaxRectsSheet.Eps && other.Y >= Y - MaxRectsSheet.Eps
        && other.Right <= Right + MaxRectsSheet.Eps && other.Top <= Top + MaxRectsSheet.Eps;

    public bool Overlaps(Rect other) =>
        other.X < Right - MaxRectsSheet.Eps && other.Right > X + MaxRectsSheet.Eps
        && other.Y < Top - MaxRectsSheet.Eps && other.Top > Y + MaxRectsSheet.Eps;
}

/// <summary>How a free position is scored; lower (Primary, Secondary) wins.</summary>
internal enum FitRule
{
    /// <summary>Smallest leftover on the tighter side of the free rectangle.</summary>
    BestShortSide,
    /// <summary>Smallest leftover on the looser side of the free rectangle.</summary>
    BestLongSide,
    /// <summary>Smallest free rectangle that holds the item.</summary>
    BestArea,
    /// <summary>Lowest top edge, then leftmost: packs rows upward and keeps a clean top offcut.</summary>
    BottomLeft,
    /// <summary>Leftmost right edge, then lowest: packs columns rightward and keeps a clean right offcut.</summary>
    LeftBottom,
    /// <summary>Most perimeter touching the sheet edge or already placed items.</summary>
    ContactPoint,
}

/// <summary>
/// Maximal-rectangles free-space tracker for one sheet (Jylänki, "A Thousand Ways to Pack the
/// Bin", 2010). Keeps every maximal empty rectangle, so any position a box can legally occupy
/// is the bottom-left corner of some free rectangle. Items and the bin are inflated by the part
/// spacing on their right/top sides by the caller, so touching inflated boxes are exactly one
/// spacing apart and the last box may touch the sheet's work-area edge.
/// </summary>
internal sealed class MaxRectsSheet
{
    public const double Eps = 1e-9;

    private readonly List<Rect> free = new();
    private readonly List<Rect> used = new();

    public MaxRectsSheet(double width, double height)
    {
        Width = width;
        Height = height;
        free.Add(new Rect(0, 0, width, height));
    }

    public double Width { get; }
    public double Height { get; }
    public IReadOnlyList<Rect> Used => used;

    /// <summary>Best position for a w-by-h item under the rule, or null when nothing holds it.</summary>
    public (Rect Place, double Primary, double Secondary)? FindBest(double w, double h, FitRule rule)
    {
        (Rect Place, double Primary, double Secondary)? best = null;
        foreach (var f in free)
        {
            if (w > f.W + Eps || h > f.H + Eps)
                continue;
            var place = new Rect(f.X, f.Y, w, h);
            var (p, s) = Score(f, place, rule);
            if (best is not { } b || p < b.Primary - Eps
                || (p <= b.Primary + Eps && s < b.Secondary - Eps))
                best = (place, p, s);
        }
        return best;
    }

    /// <summary>Commits an item and splits every free rectangle it intersects.</summary>
    public void Place(Rect item)
    {
        var next = new List<Rect>(free.Count + 8);
        foreach (var f in free)
        {
            if (!f.Overlaps(item))
            {
                next.Add(f);
                continue;
            }
            if (item.X > f.X + Eps)
                next.Add(new Rect(f.X, f.Y, item.X - f.X, f.H));
            if (item.Right < f.Right - Eps)
                next.Add(new Rect(item.Right, f.Y, f.Right - item.Right, f.H));
            if (item.Y > f.Y + Eps)
                next.Add(new Rect(f.X, f.Y, f.W, item.Y - f.Y));
            if (item.Top < f.Top - Eps)
                next.Add(new Rect(f.X, item.Top, f.W, f.Top - item.Top));
        }
        free.Clear();
        free.AddRange(Prune(next));
        used.Add(item);
    }

    private static List<Rect> Prune(List<Rect> rects)
    {
        // Drop rectangles contained in another; of two equal ones keep the first (deterministic).
        var keep = new bool[rects.Count];
        for (var i = 0; i < rects.Count; i++)
            keep[i] = rects[i].W > Eps && rects[i].H > Eps;
        for (var i = 0; i < rects.Count; i++)
        {
            if (!keep[i])
                continue;
            for (var j = 0; j < rects.Count; j++)
            {
                if (i == j || !keep[j])
                    continue;
                if (rects[j].Contains(rects[i]) && (!rects[i].Contains(rects[j]) || j < i))
                {
                    keep[i] = false;
                    break;
                }
            }
        }
        var result = new List<Rect>(rects.Count);
        for (var i = 0; i < rects.Count; i++)
            if (keep[i])
                result.Add(rects[i]);
        return result;
    }

    private (double Primary, double Secondary) Score(Rect f, Rect place, FitRule rule)
    {
        var dx = f.W - place.W;
        var dy = f.H - place.H;
        return rule switch
        {
            FitRule.BestShortSide => (System.Math.Min(dx, dy), System.Math.Max(dx, dy)),
            FitRule.BestLongSide => (System.Math.Max(dx, dy), System.Math.Min(dx, dy)),
            FitRule.BestArea => (f.W * f.H - place.W * place.H, System.Math.Min(dx, dy)),
            FitRule.BottomLeft => (place.Top, place.X),
            FitRule.LeftBottom => (place.Right, place.Y),
            FitRule.ContactPoint => (-Contact(place), place.Top + place.Right),
            _ => throw new ArgumentOutOfRangeException(nameof(rule)),
        };
    }

    private double Contact(Rect r)
    {
        var total = 0.0;
        if (r.X <= Eps) total += r.H;
        if (r.Right >= Width - Eps) total += r.H;
        if (r.Y <= Eps) total += r.W;
        if (r.Top >= Height - Eps) total += r.W;
        foreach (var u in used)
        {
            if (System.Math.Abs(u.X - r.Right) <= Eps || System.Math.Abs(u.Right - r.X) <= Eps)
                total += Overlap(u.Y, u.Top, r.Y, r.Top);
            if (System.Math.Abs(u.Y - r.Top) <= Eps || System.Math.Abs(u.Top - r.Y) <= Eps)
                total += Overlap(u.X, u.Right, r.X, r.Right);
        }
        return total;
    }

    private static double Overlap(double a0, double a1, double b0, double b1) =>
        System.Math.Max(0, System.Math.Min(a1, b1) - System.Math.Max(a0, b0));
}
