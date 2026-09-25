#nullable enable
using System;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Immutable flat-array polygon with a uniform edge grid, used as an outer-shell
    /// clearance prefilter. Two closed polygons share positive area only when an edge pair
    /// crosses/touches or one polygon's vertex lies strictly inside the other; neither
    /// happening certifies the two closed regions (hence any materials inside them) are
    /// clear. <see cref="Relate"/> returns Clear only in that certified case and Unknown
    /// for uncertain contacts, so it can only ever skip the exact <see cref="Collision"/>
    /// gate when the exact gate would also find no overlap - the exact gate triangulates
    /// both polygons per call and dominates runtime on finely flattened arc geometry.
    /// <para>
    /// A <see cref="EdgeGridPolygonTemplate"/> holds the shared geometry; <see cref="Translated"/>
    /// produces a placement in world coordinates in O(1) - translation leaves the grid and
    /// all cell indices unchanged, only the predicate coordinates shift.
    /// </para>
    /// </summary>
    public sealed class EdgeGridPolygon
    {
        /// <summary>Vertex-on-segment / collinearity tolerance for conservative touches.</summary>
        private const double TouchEps = 1e-9;

        private readonly EdgeGridPolygonTemplate _template;

        /// <summary>Translation applied to the shared template geometry.</summary>
        private readonly double Dx;

        private readonly double Dy;

        private EdgeGridPolygon(EdgeGridPolygonTemplate template, double dx, double dy)
        {
            _template = template;
            Dx = dx;
            Dy = dy;
        }

        private double MinX => _template.MinX + Dx;
        private double MinY => _template.MinY + Dy;
        private double MaxX => _template.MaxX + Dx;
        private double MaxY => _template.MaxY + Dy;

        /// <summary>
        /// Builds from a closed <see cref="Polygon"/> (last vertex may repeat the first).
        /// Returns null when the polygon has no usable ring - callers treat that as
        /// "no information" and fall through to the exact gate.
        /// </summary>
        public static EdgeGridPolygon? From(Polygon polygon)
        {
            var template = EdgeGridPolygonTemplate.Build(polygon);
            return template == null ? null : new EdgeGridPolygon(template, 0, 0);
        }

        /// <summary>Returns a placement sharing immutable geometry, with an added translation.</summary>
        public EdgeGridPolygon Translated(double dx, double dy) => new(_template, Dx + dx, Dy + dy);

        private double X(int i) => _template.X[i] + Dx;
        private double Y(int i) => _template.Y[i] + Dy;

        /// <summary>
        /// Certifies disjoint filled perimeters. Any crossing, containment or uncertain
        /// boundary contact returns Unknown and must defer to the exact collision test.
        /// Holes need not be supplied: removing material cannot invalidate Clear.
        /// </summary>
        public static ShellRelation Relate(EdgeGridPolygon a, EdgeGridPolygon b)
        {
            if (
                a.MaxX <= b.MinX
                || b.MaxX <= a.MinX
                || a.MaxY <= b.MinY
                || b.MaxY <= a.MinY
            )
                return ShellRelation.Clear; // disjoint bounding boxes

            // One walk per direction reports the strongest edge relation: a transversal
            // crossing shares a positive-area wedge (overlap); a mere touch shares zero
            // area but may hide a crossing in near-degenerate coordinates (unknown).
            var edge = EdgeRelation(a, b);
            if (edge < 2)
            {
                var back = EdgeRelation(b, a);
                if (back > edge)
                    edge = back;
            }
            if (edge == 2)
                return ShellRelation.Unknown;

            // Only fully disjoint boundaries can certify clearance. Point touches and
        // collinear/near-degenerate contacts always defer to the reference test.
        switch (edge)
            {
                case 0:
                    if (ContainsPointStrictly(a, b.X(0), b.Y(0)))
                        return ShellRelation.Unknown;
                    if (ContainsPointStrictly(b, a.X(0), a.Y(0)))
                        return ShellRelation.Unknown;
                    return ShellRelation.Clear;
                default:
                    return ShellRelation.Unknown;
            }
        }

        /// <summary>
        /// Classifies whether any edge of <paramref name="q"/> crosses or touches the boundary of
        /// <paramref name="p"/>. Walks p's grid using each query edge's own bbox cells.
        /// p's grid lives in p's LOCAL frame (the template's own coordinates), so the
        /// query edge is converted by subtracting p's translation first.
        /// </summary>
        private static int EdgeRelation(EdgeGridPolygon p, EdgeGridPolygon q)
        {
            var t = p._template;
            var n = t.Count;
            Span<int> seen = n <= 1024 ? stackalloc int[n] : new int[n];
            seen.Clear();
            var head = t.Head;
            var nodeEdge = t.NodeEdge;
            var nodeNext = t.NodeNext;
            var no = q._template.Count;
            var strongest = 0;

            for (var e = 0; e < no; e++)
            {
                // Stamp per QUERY edge: a grid edge may need testing against every query
                // edge; the dedupe only collapses cells an individual query edge crosses
                // more than once.
                var stamp = e + 1;
                var i2 = (e + 1) % no;
                var p0x = q.X(e) - p.Dx;
                var p0y = q.Y(e) - p.Dy;
                var p1x = q.X(i2) - p.Dx;
                var p1y = q.Y(i2) - p.Dy;

                var c0 = ColLow(t, p0x, p1x);
                if (c0 > ColHigh(t, p0x, p1x))
                    continue;
                var c1 = ColHigh(t, p0x, p1x);
                var r0 = RowLow(t, p0y, p1y);
                if (r0 > RowHigh(t, p0y, p1y))
                    continue;
                var r1 = RowHigh(t, p0y, p1y);

                for (var r = r0; r <= r1; r++)
                    for (var c = c0; c <= c1; c++)
                        for (var nIdx = head[r * t.Cols + c]; nIdx >= 0; nIdx = nodeNext[nIdx])
                        {
                            var ea = nodeEdge[nIdx];
                            if (seen[ea] == stamp)
                                continue;
                            seen[ea] = stamp;
                            var a2 = (ea + 1) % n;
                            var relation = SegmentRelation(
                                t.X[ea], t.Y[ea], t.X[a2], t.Y[a2], p0x, p0y, p1x, p1y
                            );
                            if (relation == 2)
                                return 2; // transversal crossing
                            if (relation > strongest)
                                strongest = relation;
                        }
            }
            return strongest;
        }

        /// <summary>
        /// Segment-pair relation: 2 = transversal crossing (strict sign flips on both
        /// orientations - the regions share a positive-area wedge); 1 = a clean endpoint
        /// touch (zero shared area by itself; callers decide via interior-vertex tests);
        /// 3 = collinear or near-degenerate contact (a shared boundary segment can hide
        /// either a same-side positive overlap or an opposite-side tangency, so it must
        /// defer to the exact gate); 0 = disjoint.
        /// </summary>
        private static int SegmentRelation(
            double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy
        )
        {
            var rx = bx - ax;
            var ry = by - ay;
            var sx = dx - cx;
            var sy = dy - cy;
            var d1 = rx * (cy - ay) - ry * (cx - ax);
            var d2 = rx * (dy - ay) - ry * (dx - ax);
            var d3 = sx * (ay - cy) - sy * (ax - cx);
            var d4 = sx * (by - cy) - sy * (bx - cx);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return 2; // proper crossing

            // A near-zero orientation means the configuration is collinear or too close to
            // classify; only exact-zero orientations get the clean point-touch verdict.
            var scale = System.Math.Max(
                1e-30,
                System.Math.Max(System.Math.Abs(rx) + System.Math.Abs(ry), System.Math.Abs(sx) + System.Math.Abs(sy))
            );
            var eps = TouchEps * scale;
            var nearDegenerate =
                (System.Math.Abs(d1) <= eps && d1 != 0)
                || (System.Math.Abs(d2) <= eps && d2 != 0)
                || (System.Math.Abs(d3) <= eps && d3 != 0)
                || (System.Math.Abs(d4) <= eps && d4 != 0);
            var exactDegenerate = d1 == 0 || d2 == 0 || d3 == 0 || d4 == 0;

            var touch =
                (d1 == 0 && PointOnSegment(cx, cy, ax, ay, bx, by))
                || (d2 == 0 && PointOnSegment(dx, dy, ax, ay, bx, by))
                || (d3 == 0 && PointOnSegment(ax, ay, cx, cy, dx, dy))
                || (d4 == 0 && PointOnSegment(bx, by, cx, cy, dx, dy));

            if (nearDegenerate)
                return 3;
            if (exactDegenerate)
                // Collinear: contact along a segment (or too close to tell) must defer to
                // the exact gate; collinear but disjoint edges simply do not touch.
                return touch ? 3 : 0;
            if (touch)
                return 1;
            return 0;
        }

        private static bool PointOnSegment(
            double px, double py, double ax, double ay, double bx, double by
        ) =>
            System.Math.Min(ax, bx) - TouchEps <= px
            && px <= System.Math.Max(ax, bx) + TouchEps
            && System.Math.Min(ay, by) - TouchEps <= py
            && py <= System.Math.Max(ay, by) + TouchEps;

        /// <summary>Strict ray-cast containment (boundary touches are excluded upstream).</summary>
        private static bool ContainsPointStrictly(EdgeGridPolygon poly, double px, double py)
        {
            var t = poly._template;
            var inside = false;
            var n = t.Count;
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                var yi = poly.Y(i);
                var yj = poly.Y(j);
                if ((yi > py) != (yj > py))
                {
                    var xAt = poly.X(i) + (py - yi) / (yj - yi) * (poly.X(j) - poly.X(i));
                    if (px < xAt)
                        inside = !inside;
                }
            }
            return inside;
        }

        private static int ColLow(EdgeGridPolygonTemplate t, double a, double b) =>
            System.Math.Clamp((int)System.Math.Floor((System.Math.Min(a, b) - t.MinX) / t.CellSize), 0, t.Cols);

        private static int ColHigh(EdgeGridPolygonTemplate t, double a, double b) =>
            System.Math.Clamp((int)System.Math.Floor((System.Math.Max(a, b) - t.MinX) / t.CellSize), -1, t.Cols - 1);

        private static int RowLow(EdgeGridPolygonTemplate t, double a, double b) =>
            System.Math.Clamp((int)System.Math.Floor((System.Math.Min(a, b) - t.MinY) / t.CellSize), 0, t.Rows);

        private static int RowHigh(EdgeGridPolygonTemplate t, double a, double b) =>
            System.Math.Clamp((int)System.Math.Floor((System.Math.Max(a, b) - t.MinY) / t.CellSize), -1, t.Rows - 1);

        /// <summary>
        /// Shared, immutable grid geometry for <see cref="EdgeGridPolygon"/>; the grid is defined
        /// relative to the shape's own local coordinates, so translated instances reuse it.
        /// Per-query deduplication scratch is local, so placements may be queried concurrently.
        /// </summary>
        private sealed class EdgeGridPolygonTemplate
        {
            internal readonly double[] X;
            internal readonly double[] Y;
            internal readonly int Count;
            internal readonly double MinX;
            internal readonly double MinY;
            internal readonly double MaxX;
            internal readonly double MaxY;

            internal readonly double CellSize;

            internal readonly int Cols;
            internal readonly int Rows;
            internal readonly int[] Head;

            /// <summary>
            /// Grid nodes as parallel (edge, next) arrays: an edge spanning several cells gets
            /// one node PER cell - a single next-per-edge chain would corrupt the other cells'
            /// chains and silently drop edges from the walk.
            /// </summary>
            internal readonly int[] NodeEdge;

            internal readonly int[] NodeNext;


            private EdgeGridPolygonTemplate(
                double[] x,
                double[] y,
                int count,
                double minX,
                double minY,
                double maxX,
                double maxY
            )
            {
                X = x;
                Y = y;
                Count = count;
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;

                var extentX = System.Math.Max(maxX - minX, 1e-9);
                var extentY = System.Math.Max(maxY - minY, 1e-9);
                CellSize = System.Math.Max(System.Math.Max(extentX, extentY) / 16.0, 1e-9);
                Cols = System.Math.Clamp((int)System.Math.Ceiling(extentX / CellSize) + 1, 1, 48);
                Rows = System.Math.Clamp((int)System.Math.Ceiling(extentY / CellSize) + 1, 1, 48);
                Head = new int[Cols * Rows];
                Array.Fill(Head, -1);

                // Pass 1: count nodes; pass 2: fill (edge, next) node arrays.
                var cellsPerEdge = new int[count];
                var total = 0;
                for (var e = 0; e < count; e++)
                {
                    var i2 = (e + 1) % count;
                    var c0 = ClampCol(System.Math.Min(x[e], x[i2]) - minX);
                    var c1 = ClampCol(System.Math.Max(x[e], x[i2]) - minX);
                    var r0 = ClampRow(System.Math.Min(y[e], y[i2]) - minY);
                    var r1 = ClampRow(System.Math.Max(y[e], y[i2]) - minY);
                    cellsPerEdge[e] = (c1 - c0 + 1) * (r1 - r0 + 1);
                    total += cellsPerEdge[e];
                }
                NodeEdge = new int[total];
                NodeNext = new int[total];
                var node = 0;
                for (var e = 0; e < count; e++)
                {
                    var i2 = (e + 1) % count;
                    var c0 = ClampCol(System.Math.Min(x[e], x[i2]) - minX);
                    var c1 = ClampCol(System.Math.Max(x[e], x[i2]) - minX);
                    var r0 = ClampRow(System.Math.Min(y[e], y[i2]) - minY);
                    var r1 = ClampRow(System.Math.Max(y[e], y[i2]) - minY);
                    for (var r = r0; r <= r1; r++)
                        for (var c = c0; c <= c1; c++)
                        {
                            var cell = r * Cols + c;
                            NodeEdge[node] = e;
                            NodeNext[node] = Head[cell];
                            Head[cell] = node;
                            node++;
                        }
                }
            }

            private int ClampCol(double dx) =>
                System.Math.Clamp((int)System.Math.Floor(dx / CellSize), 0, Cols - 1);

            private int ClampRow(double dy) =>
                System.Math.Clamp((int)System.Math.Floor(dy / CellSize), 0, Rows - 1);

            internal static EdgeGridPolygonTemplate? Build(Polygon polygon)
            {
                var vertices = polygon.Vertices;
                var n = vertices.Count;
                if (n >= 2 && vertices[0].X == vertices[n - 1].X && vertices[0].Y == vertices[n - 1].Y)
                    n--;
                if (n < 3)
                    return null;
                var xs = new double[n];
                var ys = new double[n];
                var minX = double.MaxValue;
                var minY = double.MaxValue;
                var maxX = double.MinValue;
                var maxY = double.MinValue;
                for (var i = 0; i < n; i++)
                {
                    var vx = vertices[i].X;
                    var vy = vertices[i].Y;
                    xs[i] = vx;
                    ys[i] = vy;
                    if (vx < minX)
                        minX = vx;
                    if (vx > maxX)
                        maxX = vx;
                    if (vy < minY)
                        minY = vy;
                    if (vy > maxY)
                        maxY = vy;
                }
                return new EdgeGridPolygonTemplate(xs, ys, n, minX, minY, maxX, maxY);
            }
        }
    }

    /// <summary>Conservative result of an outer-perimeter prefilter.</summary>
    public enum ShellRelation
    {
        /// <summary>Filled perimeters, and therefore their material, are disjoint.</summary>
        Clear,
        /// <summary>Run an exact collision test; the prefilter cannot certify clearance.</summary>
        Unknown,
    }
}
