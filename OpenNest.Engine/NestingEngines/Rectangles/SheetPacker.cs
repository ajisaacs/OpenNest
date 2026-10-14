#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.RectanglePacking;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Rectangles;

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
    /// <summary>
    /// How far a box may overhang the work area. Kept just inside the layout check's
    /// <see cref="NestTolerances.WorkAreaSlack"/> so floating-point rounding cannot push an
    /// accepted box over it.
    /// </summary>
    public const double OverhangAllowance = NestTolerances.WorkAreaSlack * 0.9;

    /// <summary>
    /// Packing size along one axis: a box that exceeds the sheet by no more than
    /// <see cref="OverhangAllowance"/> packs as exactly the sheet's size. It then spans the whole
    /// axis, so it has no neighbour there and the overhang lands only past the work-area edge.
    /// </summary>
    public static double PackSize(double size, double sheet) =>
        size > sheet && size <= sheet + OverhangAllowance ? sheet : size;

    public static SheetPlan Pack(
        IReadOnlyList<BoxType> types, IReadOnlyList<int> remaining, NestPlateStock stock,
        FitRule rule, PickMode mode, CancellationToken token)
    {
        var work = stock.WorkArea;
        var s = stock.PartSpacing;
        var sheet = new MaxRectsSheet(work.Right - work.Left + s, work.Top - work.Bottom + s);
        BlockClamps(sheet, stock, work, s);
        var packTypes = types
            .Select(t => new PackType(
                t.Priority,
                t.Orientations
                    .Select(o => (PackSize(o.Width + s, sheet.Width), PackSize(o.Height + s, sheet.Height)))
                    .ToList(),
                t.BoxArea))
            .ToList();
        var placed = MaxRectsPacker
            .Pack(packTypes, remaining.ToArray(), sheet, rule, mode, token)
            .Select(p => new Placed(types[p.Type], types[p.Type].Orientations[p.Size], p.X, p.Y))
            .ToList();

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

    /// <summary>
    /// Blocks every clamp keep-out into the sheet's free list before packing. In the
    /// inflated-box model an item at position p covers material [p, p + size] plus a trailing
    /// part-spacing tail; blocking bin-local [zone - clearance + s, zone + clearance] per axis
    /// therefore means exactly: item material keeps at least the bottom-edge clearance from
    /// every clamp side, without paying any extra part-spacing tax there. When s exceeds the
    /// inflated span the interval inverts (decision D11): block a zero-extent line at the
    /// clamp edge facing the sheet interior — at (zone max + clearance) when the zone center
    /// lies in the lower half of the bin on that axis, else at (zone min - clearance + s).
    /// Parts on the interior side then keep the exact clearance; the unavoidable extra
    /// exclusion (s - extent - 2*clearance) lands on the side toward the nearer sheet edge.
    /// The old collapse to the zone centerline was NOT conservative: an item could start
    /// exactly on the centerline with material inside the clamp.
    /// Empty zone lists change nothing: the free list evolves bit-identically (C2).
    /// </summary>
    private static void BlockClamps(MaxRectsSheet sheet, NestPlateStock stock, Box work, double s)
    {
        if (stock.ClampZones.Count == 0)
            return;

        var clearance = System.Math.Max(0, stock.EdgeSpacing.Bottom);
        foreach (var zone in stock.ClampZones)
        {
            var b = zone.Bounds; // placement frame -> bin-local packing coordinates
            double x0(double v) => v - work.Left;
            double y0(double v) => v - work.Bottom;
            var left = x0(b.Left) - clearance + s;
            var right = x0(b.Right) + clearance;
            if (right <= left)
            {
                var center = x0((b.Left + b.Right) * 0.5);
                // D11: the interior-facing clamp edge (toward the farther sheet edge).
                var line = center < sheet.Width * 0.5
                    ? x0(b.Right) + clearance
                    : x0(b.Left) - clearance + s;
                left = line;
                right = line;
            }
            var bottom = y0(b.Bottom) - clearance + s;
            var top = y0(b.Top) + clearance;
            if (top <= bottom)
            {
                var center = y0((b.Bottom + b.Top) * 0.5);
                var line = center < sheet.Height * 0.5
                    ? y0(b.Top) + clearance
                    : y0(b.Bottom) - clearance + s;
                bottom = line;
                top = line;
            }
            sheet.Block(new Rect(left, bottom, right - left, top - bottom));
        }
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
