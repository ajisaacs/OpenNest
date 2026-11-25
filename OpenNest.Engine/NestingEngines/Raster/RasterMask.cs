#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using BitOperations = System.Numerics.BitOperations;

namespace OpenNest.Engine.NestingEngines.Raster;

/// <summary>
/// One rotation of a part as a bit-packed raster: cells whose center lies in the part's material
/// or within <c>dilate</c> of its outline. Cutouts stay open, so other parts may nest in them.
/// Bit i of row j is cell (i, j); row j covers Y from <see cref="OriginY"/> + j * cell.
/// </summary>
internal sealed class RasterMask
{
    /// <summary>Requested type index.</summary>
    public int Type;

    /// <summary>Rotation in radians about the snapshot origin.</summary>
    public double Angle;

    /// <summary>Rotation kinds this pose belongs to (see <see cref="RotationKinds"/>).</summary>
    public int Kinds;

    /// <summary>Outline bounds in the rotated snapshot frame.</summary>
    public double MinX, MinY, Length, Width;

    /// <summary>Lower-left corner of cell (0, 0) in the rotated snapshot frame.</summary>
    public double OriginX, OriginY;

    public int W, H, Words;
    public ulong[] Bits = Array.Empty<ulong>();

    /// <summary>Set runs per row, for building no-fit maps (coarse masks only).</summary>
    public (int Start, int End)[][] Runs = Array.Empty<(int, int)[]>();

    /// <summary>Table[k] bit x of a row: OR of the mask's bits x - 2^k + 1 .. x (coarse masks only).</summary>
    public ulong[][] Table = Array.Empty<ulong[]>();
    public int TableWords;

    /// <summary>The same pose on the fine grid (bits only), or this mask for a single grid.</summary>
    public RasterMask Fine = null!;

    /// <summary>
    /// Rasterizes <paramref name="outline"/> (the part's spacing outline at zero inflation,
    /// already rotated by <paramref name="angle"/>). Null when the outline is empty.
    /// </summary>
    /// <remarks>
    /// Two parts whose material comes closer than 2 * (<paramref name="dilate"/> - cell / sqrt 2)
    /// always share a cell: the cell holding the midpoint of their closest points has its center
    /// within that distance of both. The engine sets dilate to half the part spacing plus
    /// 0.75 cell, so disjoint masks keep the spacing.
    /// </remarks>
    public static RasterMask? Build(int type, double angle, SpacingOutline outline, double cell, double dilate)
    {
        var polygons = new List<Polygon> { outline.Perimeter };
        polygons.AddRange(outline.Holes);
        var points = outline.Perimeter.Vertices;
        if (points.Count < 3)
            return null;
        double minX = points.Min(v => v.X), maxX = points.Max(v => v.X);
        double minY = points.Min(v => v.Y), maxY = points.Max(v => v.Y);
        var m = new RasterMask
        {
            Type = type,
            Angle = angle,
            MinX = minX,
            MinY = minY,
            Length = maxX - minX,
            Width = maxY - minY,
            OriginX = minX - dilate,
            OriginY = minY - dilate,
        };
        m.W = System.Math.Max(1, (int)System.Math.Ceiling((m.Length + 2 * dilate) / cell - 1e-9));
        m.H = System.Math.Max(1, (int)System.Math.Ceiling((m.Width + 2 * dilate) / cell - 1e-9));
        m.Words = (m.W + 63) / 64;
        m.Bits = new ulong[m.H * m.Words];

        // Interior: even-odd over every contour, so cutouts stay open.
        var xs = new List<double>();
        for (var j = 0; j < m.H; j++)
        {
            var y = m.OriginY + (j + 0.5) * cell;
            xs.Clear();
            foreach (var polygon in polygons)
                foreach (var (p, q) in Edges(polygon))
                {
                    if ((p.Y <= y) == (q.Y <= y))
                        continue;
                    xs.Add(p.X + (y - p.Y) / (q.Y - p.Y) * (q.X - p.X));
                }
            xs.Sort();
            for (var k = 0; k + 1 < xs.Count; k += 2)
            {
                var i0 = (int)System.Math.Ceiling((xs[k] - m.OriginX) / cell - 0.5);
                var i1 = (int)System.Math.Floor((xs[k + 1] - m.OriginX) / cell - 0.5);
                for (var i = i0; i <= i1; i++)
                    m.Set(i, j);
            }
        }

        // Band: every cell whose center lies within the dilation of an edge.
        if (dilate > 0)
            foreach (var polygon in polygons)
                foreach (var (p, q) in Edges(polygon))
                {
                    var i0 = (int)System.Math.Floor((System.Math.Min(p.X, q.X) - dilate - m.OriginX) / cell);
                    var i1 = (int)System.Math.Ceiling((System.Math.Max(p.X, q.X) + dilate - m.OriginX) / cell);
                    var j0 = (int)System.Math.Floor((System.Math.Min(p.Y, q.Y) - dilate - m.OriginY) / cell);
                    var j1 = (int)System.Math.Ceiling((System.Math.Max(p.Y, q.Y) + dilate - m.OriginY) / cell);
                    double ex = q.X - p.X, ey = q.Y - p.Y, length2 = ex * ex + ey * ey;
                    for (var j = j0; j <= j1; j++)
                        for (var i = i0; i <= i1; i++)
                        {
                            double cx = m.OriginX + (i + 0.5) * cell - p.X, cy = m.OriginY + (j + 0.5) * cell - p.Y;
                            var t = length2 > 0 ? System.Math.Clamp((cx * ex + cy * ey) / length2, 0, 1) : 0;
                            double dx = cx - t * ex, dy = cy - t * ey;
                            if (dx * dx + dy * dy <= dilate * dilate)
                                m.Set(i, j);
                        }
                }
        return m;
    }

    private static IEnumerable<(Vector P, Vector Q)> Edges(Polygon polygon)
    {
        var v = polygon.Vertices;
        for (var k = 0; k < v.Count; k++)
        {
            var q = v[(k + 1) % v.Count];
            if (v[k] != q)
                yield return (v[k], q);
        }
    }

    private void Set(int i, int j)
    {
        if (i >= 0 && i < W && j >= 0 && j < H)
            Bits[j * Words + (i >> 6)] |= 1UL << (i & 63);
    }

    /// <summary>Prepares the run lists and the shifted-OR table no-fit maps are built from;
    /// <paramref name="maxWidth"/> is the widest coarse mask of the grid.</summary>
    public void PrepareForNoFit(int maxWidth)
    {
        Runs = new (int, int)[H][];
        var runs = new List<(int, int)>();
        for (var j = 0; j < H; j++)
        {
            runs.Clear();
            var start = -1;
            for (var i = 0; i <= W; i++)
            {
                var on = i < W && (Bits[j * Words + (i >> 6)] >> (i & 63) & 1) != 0;
                if (on && start < 0)
                    start = i;
                else if (!on && start >= 0)
                {
                    runs.Add((start, i - 1));
                    start = -1;
                }
            }
            Runs[j] = runs.ToArray();
        }

        TableWords = (W + maxWidth + 63) / 64 + 1;
        var levels = 1;
        while ((1 << levels) <= maxWidth)
            levels++;
        Table = new ulong[levels][];
        Table[0] = new ulong[H * TableWords];
        for (var j = 0; j < H; j++)
            Array.Copy(Bits, j * Words, Table[0], j * TableWords, Words);
        for (var k = 1; k < levels; k++)
        {
            var previous = Table[k - 1];
            var current = Table[k] = (ulong[])previous.Clone();
            for (var j = 0; j < H; j++)
                Bits64.OrShift(current, j * TableWords, TableWords, previous, j * TableWords, TableWords, 1 << (k - 1));
        }
    }
}

/// <summary>Rotation kinds a variant may use.</summary>
internal static class RotationKinds
{
    public const int QuarterTurns = 1;
    public const int EighthTurns = 2;
    public const int HullAligned = 4;
    public const int All = QuarterTurns | EighthTurns | HullAligned;
}

/// <summary>
/// Raster no-fit map of mask B around mask A: bit X of row Y is set when B, placed
/// (X - (B.W - 1), Y - (B.H - 1)) cells from A's cell (0, 0), shares a cell with A.
/// </summary>
internal sealed class NoFitMap
{
    public ulong[] Bits = Array.Empty<ulong>();
    public int W, H, Words;

    public static NoFitMap Build(RasterMask a, RasterMask b)
    {
        int w = a.W + b.W - 1, h = a.H + b.H - 1, words = (w + 63) / 64;
        var bits = new ulong[h * words];
        for (var y = 0; y < h; y++)
            for (var j = System.Math.Max(0, b.H - 1 - y); j < b.H; j++)
            {
                var row = y - (b.H - 1) + j;
                if (row >= a.H)
                    break;
                foreach (var (start, end) in b.Runs[j])
                {
                    // Every shift t0..t1 of A's row: the OR of two overlapping power-of-two spans.
                    int t0 = b.W - 1 - end, t1 = b.W - 1 - start;
                    var k = BitOperations.Log2((uint)(t1 - t0 + 1));
                    var table = a.Table[k];
                    Bits64.OrShift(bits, y * words, words, table, row * a.TableWords, a.TableWords, t0);
                    var t = t1 - (1 << k) + 1;
                    if (t != t0)
                        Bits64.OrShift(bits, y * words, words, table, row * a.TableWords, a.TableWords, t);
                }
            }
        return new NoFitMap { Bits = bits, W = w, H = h, Words = words };
    }
}

internal static class Bits64
{
    /// <summary>dest bit (x + shift) |= src bit x, for a signed shift.</summary>
    public static void OrShift(ulong[] dest, int destAt, int destWords, ulong[] src, int srcAt, int srcWords, int shift)
    {
        if (shift >= 0)
        {
            int q = shift >> 6, r = shift & 63;
            for (var w = q; w < destWords; w++)
            {
                var s = w - q;
                ulong v = 0;
                if (s < srcWords)
                    v = src[srcAt + s] << r;
                if (r > 0 && s - 1 >= 0 && s - 1 < srcWords)
                    v |= src[srcAt + s - 1] >> (64 - r);
                if (v != 0)
                    dest[destAt + w] |= v;
            }
        }
        else
        {
            int q = -shift >> 6, r = -shift & 63;
            for (var w = 0; w < destWords; w++)
            {
                var s = w + q;
                if (s >= srcWords)
                    break;
                var v = src[srcAt + s] >> r;
                if (r > 0 && s + 1 < srcWords)
                    v |= src[srcAt + s + 1] << (64 - r);
                if (v != 0)
                    dest[destAt + w] |= v;
            }
        }
    }

    /// <summary>First clear bit at or after <paramref name="from"/> in a row of
    /// <paramref name="width"/> bits, or -1.</summary>
    public static int NextClear(ulong[] bits, int at, int words, int width, int from)
    {
        if (from >= width)
            return -1;
        for (var w = from >> 6; w < words; w++)
        {
            var free = ~bits[at + w];
            if (w == from >> 6)
                free &= ulong.MaxValue << (from & 63);
            if (free == 0)
                continue;
            var x = (w << 6) + BitOperations.TrailingZeroCount(free);
            return x < width ? x : -1;
        }
        return -1;
    }
}
