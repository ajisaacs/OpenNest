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

    private static Box Union(Box a, Box b)
    {
        var l = System.Math.Min(a.Left, b.Left);
        var bo = System.Math.Min(a.Bottom, b.Bottom);
        var r = System.Math.Max(a.Right, b.Right);
        var t = System.Math.Max(a.Top, b.Top);
        return new Box(l, bo, r - l, t - bo);
    }
}
