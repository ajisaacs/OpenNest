#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.FlatBar;

/// <summary>One allowed rotation of a part, reduced to its material bounding box.</summary>
/// <param name="Angle">Rotation in radians about the snapshot origin.</param>
/// <param name="Width">Material X extent after rotation.</param>
/// <param name="Height">Material Y extent after rotation.</param>
/// <param name="OffsetX">Rotated material bounds' left edge relative to the snapshot origin.</param>
/// <param name="OffsetY">Rotated material bounds' bottom edge relative to the snapshot origin.</param>
internal sealed record BarOrientation(double Angle, double Width, double Height, double OffsetX, double OffsetY);

/// <summary>A requested part type with every legal rotation's material box.</summary>
internal sealed record BarType(
    int Index,
    NestJobPart Part,
    JobPartGeometry? Geometry,
    IReadOnlyList<BarOrientation> Orientations)
{
    public string Id => Part.Id;
    public int Priority => Part.Priority;
    public double MaterialArea => Geometry?.MaterialArea ?? 0;
}

/// <summary>
/// Reads every requested part's legal rotations as material boxes, using the same
/// validation-aware bounds as the Rectangles lane so box contact passes the layout check.
/// Unlike Rectangles, all rotations are kept: a bar accepts any rotation whose cross extent
/// fits its width, and the compaction pass may prefer a different rotation of the same length
/// (a part turned 180 degrees so its mitred end nests against its neighbor).
/// </summary>
internal static class BarCatalog
{
    private const double DimensionTie = 1e-7;

    public static IReadOnlyList<BarType> Build(NestJob job)
    {
        var types = new List<BarType>(job.Parts.Count);
        for (var i = 0; i < job.Parts.Count; i++)
            types.Add(Read(i, job.Parts[i]));
        return types;
    }

    private static BarType Read(int index, NestJobPart part)
    {
        var geometry = JobPartGeometry.TryRead(part.Geometry);
        if (geometry == null)
            return new BarType(index, part, null, Array.Empty<BarOrientation>());

        var kept = new List<BarOrientation>();
        foreach (var angle in RotationCandidates.ForShape(part.Rotation, geometry.Perimeter))
        {
            var bounds = BoxCatalog.RotatedMaterialBounds(part.Geometry, angle);
            if (bounds is not { } b || !(b.Width > 0) || !(b.Height > 0))
                continue;
            kept.Add(new BarOrientation(angle, b.Width, b.Height, b.Left, b.Bottom));
        }
        return new BarType(index, part, geometry, kept);
    }

    /// <summary>True when two lengths are the same box length for orientation choice.</summary>
    public static bool SameLength(double a, double b) => System.Math.Abs(a - b) <= DimensionTie;
}

/// <summary>A free run of the bar along its length, in placement-frame axis coordinates.</summary>
internal readonly record struct BarSegment(double Start, double End)
{
    public double Length => End - Start;
}

/// <summary>
/// One stock read as a bar: its long work-area axis is the bar's length, its short axis the
/// bar's width. Parts are laid in a single row along the length, each at a rotation whose
/// cross extent fits the width. Clamp zones that reach the row split the length into segments.
/// </summary>
internal sealed class BarStock
{
    private BarStock(NestPlateStock stock, bool alongX, Box work, IReadOnlyList<BarSegment> segments,
        IReadOnlyList<IReadOnlyList<BarOrientation>> fits, double[] lengths)
    {
        Stock = stock;
        AlongX = alongX;
        Work = work;
        Segments = segments;
        Fits = fits;
        Lengths = lengths;
    }

    public NestPlateStock Stock { get; }

    /// <summary>True when the bar runs along X (the work area is at least as long as it is wide).</summary>
    public bool AlongX { get; }

    public Box Work { get; }
    public IReadOnlyList<BarSegment> Segments { get; }

    /// <summary>Per type: the rotations of shortest length whose cross extent fits the bar.</summary>
    public IReadOnlyList<IReadOnlyList<BarOrientation>> Fits { get; }

    /// <summary>Per type: material length along the bar, or NaN when no rotation fits.</summary>
    public double[] Lengths { get; }

    public double Spacing => Stock.PartSpacing;
    public double AxisStart => AlongX ? Work.Left : Work.Bottom;
    public double CrossStart => AlongX ? Work.Bottom : Work.Left;
    public double CrossExtent => AlongX ? Work.Width : Work.Length;

    public double Along(BarOrientation o) => AlongX ? o.Width : o.Height;
    public double Across(BarOrientation o) => AlongX ? o.Height : o.Width;
    public double AxisOffset(BarOrientation o) => AlongX ? o.OffsetX : o.OffsetY;

    /// <summary>Job pose putting the orientation's material box at the given axis start.</summary>
    public (double X, double Y) Pose(BarOrientation o, double axisStart) =>
        AlongX
            ? (axisStart - o.OffsetX, CrossStart - o.OffsetY)
            : (CrossStart - o.OffsetX, axisStart - o.OffsetY);

    public static BarStock Build(NestPlateStock stock, IReadOnlyList<BarType> types)
    {
        var work = stock.WorkArea;
        var alongX = work.Length >= work.Width;
        var crossExtent = alongX ? work.Width : work.Length;
        var axisLength = alongX ? work.Length : work.Width;
        var fits = new IReadOnlyList<BarOrientation>[types.Count];
        var lengths = new double[types.Count];
        for (var t = 0; t < types.Count; t++)
        {
            // A bar is a rectangle; an outlined sheet is left to engines that fill outlines.
            var fitting = stock.Outline != null
                ? new List<BarOrientation>()
                : types[t].Orientations
                    .Where(o => (alongX ? o.Height : o.Width) <= crossExtent + BarPacker.OverhangAllowance)
                    .ToList();
            if (fitting.Count == 0)
            {
                fits[t] = Array.Empty<BarOrientation>();
                lengths[t] = double.NaN;
                continue;
            }
            var shortest = fitting.Min(o => alongX ? o.Width : o.Height);
            fits[t] = fitting.Where(o => BarCatalog.SameLength(alongX ? o.Width : o.Height, shortest)).ToList();
            // An overhang no larger than the allowance packs as the bar's exact length.
            lengths[t] = shortest > axisLength && shortest <= axisLength + BarPacker.OverhangAllowance
                ? axisLength
                : shortest;
        }
        return new BarStock(stock, alongX, work, FreeRuns(stock, work, alongX), fits, lengths);
    }

    /// <summary>
    /// Free runs of the bar's length. A clamp zone whose clearance-expanded cross extent
    /// reaches the bar's row blocks its clearance-expanded axis interval for the whole row:
    /// parts in a bar span its width, so none can pass beside a clamp. Clearance is the
    /// stock's clamp clearance, as in the other engines; part spacing is not added.
    /// </summary>
    private static IReadOnlyList<BarSegment> FreeRuns(NestPlateStock stock, Box work, bool alongX)
    {
        var start = alongX ? work.Left : work.Bottom;
        var end = alongX ? work.Right : work.Top;
        var crossLow = alongX ? work.Bottom : work.Left;
        var crossHigh = alongX ? work.Top : work.Right;
        var clearance = stock.ClampClearance;
        var blocked = new List<(double Low, double High)>();
        foreach (var zone in stock.ClampZones)
        {
            var b = zone.Bounds;
            var zoneCrossLow = (alongX ? b.Bottom : b.Left) - clearance;
            var zoneCrossHigh = (alongX ? b.Top : b.Right) + clearance;
            if (zoneCrossHigh < crossLow || zoneCrossLow > crossHigh)
                continue;
            blocked.Add(((alongX ? b.Left : b.Bottom) - clearance, (alongX ? b.Right : b.Top) + clearance));
        }

        var segments = new List<BarSegment>();
        var cursor = start;
        foreach (var (low, high) in blocked.OrderBy(b => b.Low))
        {
            if (low > cursor)
                segments.Add(new BarSegment(cursor, System.Math.Min(low, end)));
            cursor = System.Math.Max(cursor, high);
            if (cursor >= end)
                break;
        }
        if (cursor < end)
            segments.Add(new BarSegment(cursor, end));
        return segments.Where(s => s.Length > 0).ToList();
    }
}
