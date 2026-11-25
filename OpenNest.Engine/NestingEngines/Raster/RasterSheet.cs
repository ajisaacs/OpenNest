#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Raster;

/// <summary>How a variant picks the next position.</summary>
internal enum RasterPolicy
{
    /// <summary>The position whose part ends leftmost, then lowest.</summary>
    Leftmost,

    /// <summary>Behind the frontier of the parts placed so far first, then leftmost, then lowest.</summary>
    Frontier,
}

/// <summary>One greedy decode: the order types are placed in, which rotations it may use and how
/// it picks positions.</summary>
internal sealed record RasterVariant(IReadOnlyList<int> Order, int Kinds, RasterPolicy Policy);

/// <summary>
/// Greedy raster placement on one sheet. Each type in the variant's order is placed copy by copy:
/// the coarse no-fit maps of every allowed rotation list the free coarse positions, the best by
/// the variant's policy is settled on the fine grid in a small window around it (leftmost, then
/// lowest free fine position), and a proposal the fine grid refuses is skipped for the next one.
/// A type that no longer fits gives way to the next. Deterministic, budgeted by
/// <see cref="MaxProposals"/>.
/// </summary>
internal static class RasterSheet
{
    /// <summary>Most coarse proposals tried per placed part.</summary>
    private const int MaxProposals = 4000;

    /// <summary>Gap kept between a part's material box and a clamp keep-out, so rounding cannot
    /// put a flush edge inside it (the layout check's spacing slack).</summary>
    private const double ClampMargin = NestTolerances.SpacingSlack;

    public static SheetLayout Fill(RasterGrid grid, IReadOnlyList<RasterType> types, NestPlateStock stock,
        RasterVariant variant, IReadOnlyList<int> remaining, CancellationToken token)
    {
        var work = stock.WorkArea;
        var clamps = stock.ClampZones
            .Select(z => z.Bounds)
            .Select(b => new Box(b.Left - stock.ClampClearance - ClampMargin, b.Bottom - stock.ClampClearance - ClampMargin,
                b.Length + 2 * (stock.ClampClearance + ClampMargin), b.Width + 2 * (stock.ClampClearance + ClampMargin)))
            .ToArray();
        var cell = grid.Cell;
        var fine = grid.FineCell;
        var allowance = SheetPacker.OverhangAllowance;
        var occupancyWords = ((int)System.Math.Ceiling((work.Length + 2 * grid.FineDilate) / fine) + 2 + 63) / 64 + 1;
        var occupancyRows = (int)System.Math.Ceiling((work.Width + 2 * grid.FineDilate) / fine) + 2;
        var occupancy = new ulong[occupancyRows * occupancyWords];
        var maps = new Dictionary<RasterMask, SheetMap>();
        var stamps = new List<(RasterMask Mask, int X, int Y)>();
        var placed = new List<(RasterMask Mask, int X, int Y)>();
        var left = remaining.ToArray();
        var frontier = 0.0;

        foreach (var type in variant.Order)
        {
            while (left[type] > 0)
            {
                token.ThrowIfCancellationRequested();
                var candidates = grid.Masks[type]
                    .Where(m => (m.Kinds & variant.Kinds) != 0)
                    .Select(m => (Mask: m, Map: MapOf(m)))
                    .Where(c => c.Map.Bits.Length > 0)
                    .ToList();
                var cursor = candidates
                    .Select(c => Enumerable.Range(0, c.Map.Rows)
                        .Select(y => Bits64.NextClear(c.Map.Bits, y * c.Map.Words, c.Map.Words, c.Map.Columns, 0))
                        .ToArray())
                    .ToList();
                RasterMask? chosen = null;
                int fx = 0, fy = 0;
                for (var attempt = 0; attempt < MaxProposals; attempt++)
                {
                    int best = -1, cx = 0, cy = 0;
                    var bestKey = (double.MaxValue, 0.0, 0.0);
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        var m = candidates[i].Mask;
                        var row = cursor[i];
                        for (var y = 0; y < row.Length; y++)
                        {
                            var x = row[y];
                            if (x < 0)
                                continue;
                            var right = x * cell + m.Length;
                            var key = variant.Policy == RasterPolicy.Leftmost
                                ? (right, y * cell, 0.0)
                                : (System.Math.Max(frontier, right), x * cell, y * cell);
                            if (key.CompareTo(bestKey) < 0)
                            {
                                bestKey = key;
                                best = i;
                                cx = x;
                                cy = y;
                            }
                        }
                    }
                    if (best < 0)
                        break;
                    if (Settle(candidates[best].Mask, cx, cy, out fx, out fy))
                    {
                        chosen = candidates[best].Mask;
                        break;
                    }
                    var map = candidates[best].Map;
                    cursor[best][cy] = Bits64.NextClear(map.Bits, cy * map.Words, map.Words, map.Columns, cx + 1);
                }
                if (chosen == null)
                    break;
                placed.Add((chosen, fx, fy));
                Stamp(chosen.Fine, fx, fy);
                // The coarse maps see the part at its nearest coarse position.
                stamps.Add((chosen, (int)System.Math.Round((double)fx / RasterGrid.Ratio),
                    (int)System.Math.Round((double)fy / RasterGrid.Ratio)));
                frontier = System.Math.Max(frontier, fx * fine + chosen.Length);
                left[type]--;
            }
        }

        var counts = new int[types.Count];
        Box? envelope = null;
        var poses = new List<(string PartId, double X, double Y, double Rotation)>(placed.Count);
        foreach (var (mask, x, y) in placed)
        {
            counts[mask.Type]++;
            var f = mask.Fine;
            double materialLeft = work.Left + x * fine, materialBottom = work.Bottom + y * fine;
            poses.Add((types[mask.Type].Id, materialLeft - f.MinX, materialBottom - f.MinY, mask.Angle));
            var box = new Box(materialLeft, materialBottom, f.Length, f.Width);
            envelope = envelope == null ? box : BoundingBox.Union(envelope, box);
        }
        // Summed per type in type order, so equal layouts get bit-identical areas.
        var area = types.Sum(t => counts[t.Index] * t.MaterialArea);
        return new SheetLayout(stock, poses, area, envelope);

        // The coarse map of free positions for one mask, brought up to date with every part
        // placed since it was last used.
        SheetMap MapOf(RasterMask m)
        {
            if (!maps.TryGetValue(m, out var map))
            {
                map = new SheetMap(
                    (int)System.Math.Floor((work.Length - m.Length + allowance) / cell + 1e-9) + 1,
                    (int)System.Math.Floor((work.Width - m.Width + allowance) / cell + 1e-9) + 1);
                if (map.Bits.Length > 0)
                    foreach (var c in clamps)
                    {
                        // Every position whose material box would reach the keep-out.
                        var i0 = System.Math.Max(0, (int)System.Math.Floor((c.Left - m.Length - work.Left) / cell) + 1);
                        var i1 = System.Math.Min(map.Columns - 1, (int)System.Math.Ceiling((c.Right - work.Left) / cell) - 1);
                        var j0 = System.Math.Max(0, (int)System.Math.Floor((c.Bottom - m.Width - work.Bottom) / cell) + 1);
                        var j1 = System.Math.Min(map.Rows - 1, (int)System.Math.Ceiling((c.Top - work.Bottom) / cell) - 1);
                        for (var j = j0; j <= j1; j++)
                            for (var i = i0; i <= i1; i++)
                                map.Bits[j * map.Words + (i >> 6)] |= 1UL << (i & 63);
                    }
                maps[m] = map;
            }
            if (map.Bits.Length == 0)
                return map;
            for (; map.Applied < stamps.Count; map.Applied++)
            {
                var (a, px, py) = stamps[map.Applied];
                var noFit = grid.NoFit(a, m);
                for (var y = 0; y < noFit.H; y++)
                {
                    var row = py + y - (m.H - 1);
                    if (row < 0 || row >= map.Rows)
                        continue;
                    Bits64.OrShift(map.Bits, row * map.Words, map.Words, noFit.Bits, y * noFit.Words, noFit.Words, px - (m.W - 1));
                }
            }
            return map;
        }

        // The first free fine position in the window around a coarse proposal, leftmost then
        // lowest, or false when the fine grid refuses the whole window.
        bool Settle(RasterMask m, int cx, int cy, out int x, out int y)
        {
            var f = m.Fine;
            var maxX = (int)System.Math.Floor((work.Length - f.Length + allowance) / fine + 1e-9);
            var maxY = (int)System.Math.Floor((work.Width - f.Width + allowance) / fine + 1e-9);
            int x0 = System.Math.Max(0, cx * RasterGrid.Ratio - grid.Span), x1 = System.Math.Min(maxX, cx * RasterGrid.Ratio + grid.Span);
            int y0 = System.Math.Max(0, cy * RasterGrid.Ratio - grid.Span), y1 = System.Math.Min(maxY, cy * RasterGrid.Ratio + grid.Span);
            for (x = x0; x <= x1; x++)
                for (y = y0; y <= y1; y++)
                    if (!Collides(f, x, y) && !HitsClamp(f, x, y))
                        return true;
            x = y = 0;
            return false;
        }

        bool HitsClamp(RasterMask f, int x, int y)
        {
            double l = work.Left + x * fine, b = work.Bottom + y * fine;
            foreach (var c in clamps)
                if (l < c.Right && l + f.Length > c.Left && b < c.Top && b + f.Width > c.Bottom)
                    return true;
            return false;
        }

        bool Collides(RasterMask f, int x, int y)
        {
            int q = x >> 6, r = x & 63;
            for (var j = 0; j < f.H; j++)
            {
                var at = (y + j) * occupancyWords + q;
                var row = j * f.Words;
                for (var w = 0; w < f.Words; w++)
                {
                    var bits = f.Bits[row + w];
                    if (bits == 0)
                        continue;
                    var o = occupancy[at + w] >> r;
                    if (r > 0)
                        o |= occupancy[at + w + 1] << (64 - r);
                    if ((o & bits) != 0)
                        return true;
                }
            }
            return false;
        }

        void Stamp(RasterMask f, int x, int y)
        {
            int q = x >> 6, r = x & 63;
            for (var j = 0; j < f.H; j++)
            {
                var at = (y + j) * occupancyWords + q;
                var row = j * f.Words;
                for (var w = 0; w < f.Words; w++)
                {
                    var bits = f.Bits[row + w];
                    if (bits == 0)
                        continue;
                    occupancy[at + w] |= bits << r;
                    if (r > 0)
                        occupancy[at + w + 1] |= bits >> (64 - r);
                }
            }
        }
    }

    /// <summary>Coarse positions of one mask on the sheet; a set bit collides.</summary>
    private sealed class SheetMap
    {
        public SheetMap(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
            if (columns > 0 && rows > 0)
            {
                Words = (columns + 63) / 64;
                Bits = new ulong[rows * Words];
            }
        }

        public int Columns { get; }
        public int Rows { get; }
        public int Words { get; }
        public ulong[] Bits { get; } = Array.Empty<ulong>();

        /// <summary>Placed parts already merged into <see cref="Bits"/>.</summary>
        public int Applied { get; set; }
    }
}
