#nullable enable
using System;
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Immutable triangulation of a simple, closed, lines-only perimeter and its holes.
    /// Cached triangles use the reference Collision clipping and hole-subtraction rules.
    /// Translation is a parameter; preparation never retains mutable input polygons.
    /// Scratch arrays and hole-piece lists are allocated per query, with a bounded
    /// thread-local buffer pool. Null means the caller must use Collision.HasOverlap.
    /// </summary>
    public sealed class TriangulatedRegion
    {
        // Flat vertex pool (local frame) and triangle index triples (CCW).
        private readonly double[] X;
        private readonly double[] Y;

        private readonly int[] _ia;
        private readonly int[] _ib;
        private readonly int[] _ic;
        private readonly double[] _tMinX;
        private readonly double[] _tMinY;
        private readonly double[] _tMaxX;
        private readonly double[] _tMaxY;

        private double MinX { get; }
        private double MinY { get; }
        private double MaxX { get; }
        private double MaxY { get; }

        /// <summary>Triangulated holes in the same local frame (null when none).</summary>
        private readonly TriangulatedRegion?[]? Holes;

        // Scratch bound: clipped convex pieces stay small; anything larger bails.
        private const int MaxClipVertices = 48;
        private const int MaxPieces = 2048;

        private TriangulatedRegion(
            double[] x,
            double[] y,
            int[] ia,
            int[] ib,
            int[] ic,
            double[] tMinX,
            double[] tMinY,
            double[] tMaxX,
            double[] tMaxY,
            TriangulatedRegion?[]? holes
        )
        {
            X = x;
            Y = y;
            _ia = ia;
            _ib = ib;
            _ic = ic;
            _tMinX = tMinX;
            _tMinY = tMinY;
            _tMaxX = tMaxX;
            _tMaxY = tMaxY;
            Holes = holes;

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            for (var i = 0; i < x.Length; i++)
            {
                if (x[i] < minX)
                    minX = x[i];
                if (x[i] > maxX)
                    maxX = x[i];
                if (y[i] < minY)
                    minY = y[i];
                if (y[i] > maxY)
                    maxY = y[i];
            }
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>
        /// Ear-clips a polygon ring into cached triangles. Returns null when
        /// triangulation yields nothing usable - the caller falls back to Polygon gates.
        /// </summary>
        public static TriangulatedRegion? Build(Polygon perimeter, IReadOnlyList<Polygon>? holes = null)
        {
            try
            {
                var tris = ConvexDecomposition.Triangulate(perimeter);
                var count = tris.Count;
                if (count == 0)
                    return null;

                var xs = new double[count * 3];
                var ys = new double[count * 3];
                var ia = new int[count];
                var ib = new int[count];
                var ic = new int[count];
                var minXA = new double[count];
                var minYA = new double[count];
                var maxXA = new double[count];
                var maxYA = new double[count];

                var k = 0;
                for (var t = 0; t < count; t++)
                {
                    var v = tris[t].Vertices; // closed: prev, curr, next, prev
                    ia[t] = k;
                    xs[k] = v[0].X;
                    ys[k] = v[0].Y;
                    k++;
                    ib[t] = k;
                    xs[k] = v[1].X;
                    ys[k] = v[1].Y;
                    k++;
                    ic[t] = k;
                    xs[k] = v[2].X;
                    ys[k] = v[2].Y;
                    k++;
                    minXA[t] = System.Math.Min(v[0].X, System.Math.Min(v[1].X, v[2].X));
                    minYA[t] = System.Math.Min(v[0].Y, System.Math.Min(v[1].Y, v[2].Y));
                    maxXA[t] = System.Math.Max(v[0].X, System.Math.Max(v[1].X, v[2].X));
                    maxYA[t] = System.Math.Max(v[0].Y, System.Math.Max(v[1].Y, v[2].Y));
                }

                TriangulatedRegion[]? holeSets = null;
                if (holes != null && holes.Count > 0)
                {
                    holeSets = new TriangulatedRegion[holes.Count];
                    for (var h = 0; h < holes.Count; h++)
                    {
                        var holeTris = ConvexDecomposition.Triangulate(holes[h]);
                        if (holeTris.Count == 0)
                            continue;
                        var hx = new double[holeTris.Count * 3];
                        var hy = new double[holeTris.Count * 3];
                        var hia = new int[holeTris.Count];
                        var hib = new int[holeTris.Count];
                        var hic = new int[holeTris.Count];
                        var hminX = new double[holeTris.Count];
                        var hminY = new double[holeTris.Count];
                        var hmaxX = new double[holeTris.Count];
                        var hmaxY = new double[holeTris.Count];
                        var hk = 0;
                        for (var t = 0; t < holeTris.Count; t++)
                        {
                            var v = holeTris[t].Vertices;
                            hia[t] = hk;
                            hx[hk] = v[0].X;
                            hy[hk] = v[0].Y;
                            hk++;
                            hib[t] = hk;
                            hx[hk] = v[1].X;
                            hy[hk] = v[1].Y;
                            hk++;
                            hic[t] = hk;
                            hx[hk] = v[2].X;
                            hy[hk] = v[2].Y;
                            hk++;
                            hminX[t] = System.Math.Min(v[0].X, System.Math.Min(v[1].X, v[2].X));
                            hminY[t] = System.Math.Min(v[0].Y, System.Math.Min(v[1].Y, v[2].Y));
                            hmaxX[t] = System.Math.Max(v[0].X, System.Math.Max(v[1].X, v[2].X));
                            hmaxY[t] = System.Math.Max(v[0].Y, System.Math.Max(v[1].Y, v[2].Y));
                        }
                        holeSets[h] = new TriangulatedRegion(hx, hy, hia, hib, hic, hminX, hminY, hmaxX, hmaxY, null);
                    }
                }

                return new TriangulatedRegion(xs, ys, ia, ib, ic, minXA, minYA, maxXA, maxYA, holeSets);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Positive shared area (surviving both polygons' hole sets) between this
        /// translated by (adx, ady) and other translated by (bdx, bdy). Returns null
        /// when the scratch bounds are exceeded and the question cannot be decided.
        /// Inputs and translations must have finite coordinates.
        /// </summary>
        public bool? Overlaps(TriangulatedRegion other, double adx, double ady, double bdx, double bdy)
        {
            // Same bbox rule as Collision.BoundingBoxesOverlap: overlap must exceed
            // Tolerance.Epsilon on both axes, so a hairline box overlap never reaches the
            // clip stage.
            var eps = OpenNest.Math.Tolerance.Epsilon;
            var overlapX =
                System.Math.Min(MaxX + adx, other.MaxX + bdx) - System.Math.Max(MinX + adx, other.MinX + bdx);
            var overlapY =
                System.Math.Min(MaxY + ady, other.MaxY + bdy) - System.Math.Max(MinY + ady, other.MinY + bdy);
            if (overlapX <= eps || overlapY <= eps)
                return false;

            var areaFloor = 2 * OpenNest.Math.Tolerance.Epsilon;
            var clipA = new double[MaxClipVertices * 2];
            var clipB = new double[MaxClipVertices * 2];
            var piece = new double[MaxClipVertices * 2];

            for (var ta = 0; ta < _ia.Length; ta++)
            {
                var aMinX = _tMinX[ta] + adx;
                var aMaxX = _tMaxX[ta] + adx;
                var aMinY = _tMinY[ta] + ady;
                var aMaxY = _tMaxY[ta] + ady;
                for (var tb = 0; tb < other._ia.Length; tb++)
                {
                    var bMinX = other._tMinX[tb] + bdx;
                    var bMaxX = other._tMaxX[tb] + bdx;
                    var bMinY = other._tMinY[tb] + bdy;
                    var bMaxY = other._tMaxY[tb] + bdy;
                    if (
                        System.Math.Min(aMaxX, bMaxX) - System.Math.Max(aMinX, bMinX) <= eps
                        || System.Math.Min(aMaxY, bMaxY) - System.Math.Max(aMinY, bMinY) <= eps
                    )
                        continue;

                    var count = ClipTriangle(
                        ta, adx, ady, other, tb, bdx, bdy, clipA, clipB, piece
                    );
                    if (count >= MaxClipVertices)
                        return null;
                    if (count < 3)
                        continue;
                    if (TwiceArea(piece, count) <= areaFloor)
                        continue;

                    var (hasHoles, undecided, survived) = SubtractAllHoles(
                        other, adx, ady, bdx, bdy, piece, count, areaFloor
                    );
                    if (undecided)
                        return null;
                    if (hasHoles)
                    {
                        if (survived)
                            return true;
                    }
                    else
                    {
                        return true; // no holes on either side: the clipped region is overlap
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Subtracts both polygons' hole triangles from one clipped region, mirroring
        /// Collision.SubtractHoles: for every hole triangle, every surviving piece is
        /// split per edge into outside pieces (survivors) and the inside remainder
        /// (consumed). True means a positive-area piece survived ALL holes.
        /// </summary>
        [ThreadStatic]
        private static List<double[]>? s_pool;

        [ThreadStatic]
        private static double[]? s_tmpA;

        [ThreadStatic]
        private static double[]? s_tmpB;

        private static double[] AcquireBuffer()
        {
            var pool = s_pool ??= new List<double[]>();
            var n = pool.Count;
            if (n == 0)
                return new double[MaxClipVertices * 2];
            var buf = pool[n - 1];
            pool.RemoveAt(n - 1);
            return buf;
        }

        private static void ReleaseBuffer(double[] buf)
        {
            var pool = s_pool ??= new List<double[]>();
            if (pool.Count < 64)
                pool.Add(buf);
        }

        private static (double[] Tmp, double[] Inside) ScratchPair()
        {
            s_tmpA ??= new double[MaxClipVertices * 2];
            s_tmpB ??= new double[MaxClipVertices * 2];
            return (s_tmpA, s_tmpB);
        }

        private (bool hasHoles, bool undecided, bool survived) SubtractAllHoles(
            TriangulatedRegion other,
            double adx,
            double ady,
            double bdx,
            double bdy,
            double[] piece,
            int count,
            double areaFloor
        )
        {
            var allHoles = 0;
            if (Holes != null)
                allHoles += Holes.Length;
            if (other.Holes != null)
                allHoles += other.Holes.Length;
            if (allHoles == 0)
                return (false, false, false);

            // pieces[0] is the caller's own buffer - never release it back to the pool.
            var pieces = new List<(double[] Buf, int Count)> { (piece, count) };
            var owned = new HashSet<double[]>();

            bool SubtractOwner(TriangulatedRegion owner, double odx, double ody)
            {
                if (owner.Holes == null)
                    return true;
                for (var h = 0; h < owner.Holes.Length && pieces.Count > 0; h++)
                {
                    var hole = owner.Holes[h];
                    if (hole == null)
                        continue; // untriangulatable hole: nothing to subtract
                    for (var t = 0; t < hole._ia.Length && pieces.Count > 0; t++)
                    {
                        var hMinX = hole._tMinX[t] + odx;
                        var hMaxX = hole._tMaxX[t] + odx;
                        var hMinY = hole._tMinY[t] + ody;
                        var hMaxY = hole._tMaxY[t] + ody;

                        var next = new List<(double[], int)>();
                        for (var p = 0; p < pieces.Count; p++)
                        {
                            var (buf, pc) = pieces[p];

                            // Piece bbox (built-in uses <=: touching skips subtraction).
                            var pMinX = double.MaxValue;
                            var pMinY = double.MaxValue;
                            var pMaxX = double.MinValue;
                            var pMaxY = double.MinValue;
                            for (var v = 0; v < pc; v++)
                            {
                                var px = buf[v * 2];
                                var py = buf[v * 2 + 1];
                                if (px < pMinX)
                                    pMinX = px;
                                if (px > pMaxX)
                                    pMaxX = px;
                                if (py < pMinY)
                                    pMinY = py;
                                if (py > pMaxY)
                                    pMaxY = py;
                            }
                            if (pMaxX <= hMinX || hMaxX <= pMinX || pMaxY <= hMinY || hMaxY <= pMinY)
                            {
                                if (next.Count >= MaxPieces)
                                    return false;
                                next.Add((buf, pc));
                                continue;
                            }

                            // Clip the piece against the hole triangle's three edges: the
                            // outside of each edge survives as its own piece; the inside
                            // remainder continues into the next edge. The remainder inside
                            // all three edges is consumed (the hole ate it).
                            var rem = AcquireBuffer();
                            owned.Add(rem);
                            Array.Copy(buf, rem, pc * 2);
                            var remCount = pc;
                            var (tmp, insideBuf) = ScratchPair();
                            for (var e = 0; e < 3 && remCount >= 3; e++)
                            {
                                var ei = e == 0 ? hole._ia[t] : e == 1 ? hole._ib[t] : hole._ic[t];
                                var ej = e == 0 ? hole._ib[t] : e == 1 ? hole._ic[t] : hole._ia[t];
                                var sx = hole.X[ei] + odx;
                                var sy = hole.Y[ei] + ody;
                                var ex = hole.X[ej] + odx;
                                var ey = hole.Y[ej] + ody;

                                var outCount =
                                    ClipHalfSpace(rem, remCount, sx, sy, ex, ey, false, tmp);
                                if (outCount >= MaxClipVertices)
                                    return false;
                                if (outCount >= 3 && TwiceArea(tmp, outCount) > areaFloor)
                                {
                                    if (next.Count >= MaxPieces)
                                        return false; // undecided
                                    var keep = AcquireBuffer();
                                    owned.Add(keep);
                                    Array.Copy(tmp, keep, outCount * 2);
                                    next.Add((keep, outCount));
                                }
                                remCount =
                                    ClipHalfSpace(rem, remCount, sx, sy, ex, ey, true, insideBuf);
                                if (remCount >= MaxClipVertices)
                                    return false; // undecided
                                Array.Copy(insideBuf, rem, remCount * 2);
                            }
                            // The inside-all-edges remainder is consumed by the hole: drop it.
                            owned.Remove(rem);
                            ReleaseBuffer(rem);
                            if (owned.Remove(buf))
                                ReleaseBuffer(buf);
                        }
                        pieces = next;
                    }
                }
                return true;
            }

            try
            {
                if (!SubtractOwner(this, adx, ady) || !SubtractOwner(other, bdx, bdy))
                    return (true, true, false);

                foreach (var (buf, pc) in pieces)
                    if (pc >= 3 && TwiceArea(buf, pc) > areaFloor)
                        return (true, false, true);
                return (true, false, false);
            }
            finally
            {
                foreach (var buffer in owned)
                    ReleaseBuffer(buffer);
            }
        }

        /// <summary>Clip this' triangle against other's triangle; returns count into piece.</summary>
        private int ClipTriangle(
            int ta,
            double adx,
            double ady,
            TriangulatedRegion other,
            int tb,
            double bdx,
            double bdy,
            double[] bufA,
            double[] bufB,
            double[] piece
        )
        {
            var ia = _ia[ta];
            var ib = _ib[ta];
            var ic = _ic[ta];
            bufA[0] = X[ia] + adx;
            bufA[1] = Y[ia] + ady;
            bufA[2] = X[ib] + adx;
            bufA[3] = Y[ib] + ady;
            bufA[4] = X[ic] + adx;
            bufA[5] = Y[ic] + ady;
            var count = 3;

            for (var e = 0; e < 3 && count >= 3; e++)
            {
                var ei = e == 0 ? other._ia[tb] : e == 1 ? other._ib[tb] : other._ic[tb];
                var ej = e == 0 ? other._ib[tb] : e == 1 ? other._ic[tb] : other._ia[tb];
                var sx = other.X[ei] + bdx;
                var sy = other.Y[ei] + bdy;
                var ex = other.X[ej] + bdx;
                var ey = other.Y[ej] + bdy;
                count = ClipHalfSpace(bufA, count, sx, sy, ex, ey, true, bufB);
                if (count >= MaxClipVertices)
                    return count;
                for (var v = 0; v < count * 2; v++)
                    bufA[v] = bufB[v];
            }
            for (var v = 0; v < System.Math.Min(count, MaxClipVertices) * 2; v++)
                piece[v] = bufA[v];
            return count;
        }

        /// <summary>
        /// Sutherland-Hodgman clip against one directed edge's half-plane; identical
        /// classification, interpolation and dedupe to Collision.ClipHalfSpace.
        /// </summary>
        private static int ClipHalfSpace(
            double[] verts,
            int count,
            double sx,
            double sy,
            double ex,
            double ey,
            bool inside,
            double[] outBuf
        )
        {
            var kept = 0;
            var cap = outBuf.Length / 2;
            var edgeX = ex - sx;
            var edgeY = ey - sy;
            for (var i = 0; i < count; i++)
            {
                var j = (i + 1) % count;
                var cx = verts[i * 2];
                var cy = verts[i * 2 + 1];
                var nx = verts[j * 2];
                var ny = verts[j * 2 + 1];
                var cd = edgeX * (cy - sy) - edgeY * (cx - sx);
                var nd = edgeX * (ny - sy) - edgeY * (nx - sx);
                if (inside ? cd >= 0 : cd <= 0)
                {
                    if (kept >= cap)
                        return cap; // overflow: caller treats as undecided
                    kept = AddDistinct(outBuf, kept, cx, cy);
                }
                if ((cd < 0 && nd > 0) || (cd > 0 && nd < 0))
                {
                    if (kept >= cap)
                        return cap; // overflow
                    var t = cd / (cd - nd);
                    kept = AddDistinct(
                        outBuf, kept, cx + t * (nx - cx), cy + t * (ny - cy)
                    );
                }
            }
            if (kept > 1 && outBuf[0] == outBuf[(kept - 1) * 2] && outBuf[1] == outBuf[(kept - 1) * 2 + 1])
                kept--;
            return kept;
        }

        private static int AddDistinct(double[] buf, int count, double x, double y)
        {
            if (count > 0 && buf[(count - 1) * 2] == x && buf[(count - 1) * 2 + 1] == y)
                return count;
            buf[count * 2] = x;
            buf[count * 2 + 1] = y;
            return count + 1;
        }

        /// <summary>Twice the area, relative to vertex 0 (cancellation-safe).</summary>
        private static double TwiceArea(double[] verts, int count)
        {
            var twiceArea = 0.0;
            for (var i = 1; i + 1 < count; i++)
                twiceArea +=
                    (verts[i * 2] - verts[0]) * (verts[(i + 1) * 2 + 1] - verts[1])
                    - (verts[i * 2 + 1] - verts[1]) * (verts[(i + 1) * 2] - verts[0]);
            return System.Math.Abs(twiceArea);
        }
    }
}
