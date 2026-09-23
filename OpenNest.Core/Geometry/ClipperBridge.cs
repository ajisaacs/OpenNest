using System.Collections.Generic;
using Clipper2Lib;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Region offsetting through Clipper2, for CPU-side preparation only: work done
    /// once per drawing, rotation or spacing whose output is cached and fed to hot
    /// loops. Per-pair tests (<see cref="Collision"/>) stay hand-rolled so they can
    /// be ported to a GPU kernel.
    /// </summary>
    public static class ClipperBridge
    {
        /// <summary>
        /// Decimal places Clipper keeps (1e-4 in either inches or mm).
        /// </summary>
        public const int Precision = 4;

        private const double MiterLimit = 2.0;

        private const double ConservativeJoinFactor = 0.25;

        private const double ValidationJoinFactor = 0.1;

        /// <summary>
        /// Converts a polygon to a Clipper path, dropping the closing vertex and
        /// orienting it positive (CCW) or negative (CW).
        /// </summary>
        public static PathD ToPath(Polygon polygon, bool positive)
        {
            var path = ToPath(polygon, new Vector());

            if (path.Count >= 3 && Clipper.IsPositive(path) != positive)
                path.Reverse();

            return path;
        }

        /// <summary>
        /// Converts a polygon to a Clipper path with an optional offset, dropping the
        /// closing vertex and keeping the polygon's own winding.
        /// </summary>
        public static PathD ToPath(Polygon polygon, Vector offset)
        {
            var verts = polygon.Vertices;
            var n = verts.Count;

            if (n > 1 && verts[0].X == verts[n - 1].X && verts[0].Y == verts[n - 1].Y)
                n--;

            var path = new PathD(n);

            for (var i = 0; i < n; i++)
                path.Add(new PointD(verts[i].X + offset.X, verts[i].Y + offset.Y));

            return path;
        }

        /// <summary>
        /// Converts a Clipper path to a closed polygon with updated bounds.
        /// </summary>
        public static Polygon ToPolygon(PathD path)
        {
            var polygon = new Polygon();

            foreach (var pt in path)
                polygon.Vertices.Add(new Vector(pt.x, pt.y));

            polygon.Close();
            polygon.UpdateBounds();
            return polygon;
        }

        /// <summary>
        /// Flattens a profile into a Clipper region: perimeter positive, cutouts negative.
        /// </summary>
        public static PathsD ToRegion(ShapeProfile profile, double tolerance, bool circumscribe)
        {
            var region = new PathsD(profile.Cutouts.Count + 1);
            AddShape(region, profile.Perimeter, tolerance, circumscribe, positive: true);

            // A cutout is flattened the opposite way: circumscribing it would shrink the
            // material around it, so inscribe instead to keep the region conservative.
            foreach (var cutout in profile.Cutouts)
                AddShape(region, cutout, tolerance, !circumscribe, positive: false);

            return region;
        }

        /// <summary>
        /// Offsets a part region outward by <paramref name="distance"/>: the perimeter
        /// grows and the cutouts shrink. Features narrower than twice the distance
        /// collapse, and cutouts that close up disappear. Joins are round, with chords
        /// no more than <paramref name="tolerance"/> from the true arc.
        /// </summary>
        /// <param name="circumscribe">
        /// When true, the result never under-estimates the offset: perimeter arcs are
        /// flattened outside the true curve, cutout arcs inside it, and the inflation is
        /// padded by the round-join chord error and Clipper's rounding.
        /// </param>
        public static OffsetRegion Offset(
            ShapeProfile profile,
            double distance,
            double tolerance,
            bool circumscribe = false
        )
        {
            var region = ToRegion(profile, tolerance, circumscribe);
            return Offset(region, distance, tolerance, circumscribe);
        }

        /// <summary>
        /// Offsets a single closed shape outward, ignoring any cutouts. A perimeter that
        /// curls back on itself (a C shape with a narrow mouth) can gain holes.
        /// </summary>
        public static OffsetRegion OffsetPerimeter(
            Shape perimeter,
            double distance,
            double tolerance,
            bool circumscribe = false
        )
        {
            var polygon = Flatten(perimeter, tolerance, circumscribe);
            return OffsetPerimeter(polygon, distance, tolerance, circumscribe);
        }

        /// <summary>
        /// Offsets a closed polygon outward, whatever its winding.
        /// </summary>
        public static OffsetRegion OffsetPerimeter(
            Polygon perimeter,
            double distance,
            double tolerance,
            bool circumscribe = false
        )
        {
            var region = new PathsD(1);
            AddPolygon(region, perimeter, positive: true);
            return Offset(region, distance, tolerance, circumscribe);
        }

        /// <summary>
        /// Offsets an already-flattened region (outers positive, holes negative).
        /// A distance of zero only unions the region, with no conservative padding.
        /// </summary>
        public static OffsetRegion Offset(
            PathsD region,
            double distance,
            double tolerance,
            bool circumscribe = false
        )
        {
            // Round joins put their vertices on the true arc, so each chord sits inside
            // it by up to the join tolerance. In conservative mode, joins use a finer
            // tolerance and the inflation is padded by it (plus Clipper's rounding).
            var delta = distance;
            var joinTolerance = tolerance;

            if (circumscribe && distance > 0)
            {
                joinTolerance = tolerance * ConservativeJoinFactor;
                delta += joinTolerance + 0.5 * System.Math.Pow(10, -Precision);
            }

            return Inflate(region, delta, joinTolerance);
        }

        /// <summary>
        /// Offset for checking a finished layout against its spacing. Arcs are flattened
        /// as in conservative mode (perimeter arcs circumscribed, cutout arcs inscribed),
        /// but round joins use a tenth of the tolerance and nothing is padded, so a layout
        /// exactly at the spacing passes. The only under-estimate is the join chord error
        /// at convex corners, at most a tenth of <paramref name="tolerance"/>.
        /// </summary>
        public static OffsetRegion OffsetForValidation(
            ShapeProfile profile,
            double distance,
            double tolerance
        )
        {
            var region = ToRegion(profile, tolerance, circumscribe: true);
            return Inflate(region, distance, tolerance * ValidationJoinFactor);
        }

        private static OffsetRegion Inflate(PathsD region, double delta, double joinTolerance)
        {
            var inflated =
                delta <= 0
                    ? Union(region)
                    : Clipper.InflatePaths(
                        region,
                        delta,
                        JoinType.Round,
                        EndType.Polygon,
                        MiterLimit,
                        Precision,
                        joinTolerance
                    );

            var result = new OffsetRegion(new List<Polygon>(), new List<Polygon>());

            foreach (var path in inflated)
            {
                if (path.Count < 3)
                    continue;

                if (Clipper.IsPositive(path))
                    result.Outers.Add(ToPolygon(path));
                else
                    result.Holes.Add(ToPolygon(path));
            }

            return result;
        }

        /// <summary>
        /// Miter-offsets a closed polygon by <paramref name="delta"/> (positive grows it,
        /// negative shrinks it). Returns the largest resulting polygon (CCW), or null
        /// when the polygon collapses.
        /// </summary>
        public static Polygon OffsetMiter(Polygon polygon, double delta)
        {
            var path = ToPath(polygon, positive: true);

            if (path.Count < 3)
                return null;

            var inflated = Clipper.InflatePaths(
                new PathsD { path },
                delta,
                JoinType.Miter,
                EndType.Polygon,
                MiterLimit,
                Precision
            );

            PathD largest = null;
            var largestArea = 0.0;

            foreach (var candidate in inflated)
            {
                var area = Clipper.Area(candidate);

                if (area > largestArea)
                {
                    largest = candidate;
                    largestArea = area;
                }
            }

            return largest == null ? null : ToPolygon(largest);
        }

        /// <summary>
        /// Flattens a closed shape to a polygon whose chords stay within
        /// <paramref name="tolerance"/> of every arc. Inscribed, the vertices lie on the
        /// arcs. Circumscribed, arc endpoints stay on the arc and the interior vertices sit
        /// on tangent intersections, so the polygon never falls inside the curve and never
        /// pokes past the straight edges an arc meets.
        /// </summary>
        public static Polygon Flatten(Shape shape, double tolerance, bool circumscribe)
        {
            var polygon = new Polygon();

            foreach (var entity in shape.Entities)
            {
                switch (entity)
                {
                    case Line line:
                        polygon.Vertices.Add(line.StartPoint);
                        polygon.Vertices.Add(line.EndPoint);
                        break;

                    case Arc arc:
                        AddArc(polygon.Vertices, arc, tolerance, circumscribe);
                        break;

                    case Circle circle:
                        AddCircle(polygon.Vertices, circle, tolerance, circumscribe);
                        break;
                }
            }

            polygon.Close();
            polygon.Cleanup();
            polygon.UpdateBounds();
            return polygon;
        }

        private static void AddArc(List<Vector> points, Arc arc, double tolerance, bool circumscribe)
        {
            if (!circumscribe)
            {
                points.AddRange(arc.ToPoints(arc.SegmentsForTolerance(tolerance)));
                return;
            }

            var sweep = arc.SweepAngle();
            var segments = CircumscribedSegments(arc.Radius, sweep, tolerance);
            var step = (arc.IsReversed ? -sweep : sweep) / segments;
            var r = arc.Radius / System.Math.Cos(System.Math.Abs(step) / 2);

            points.Add(arc.StartPoint());

            for (var i = 0; i < segments; i++)
            {
                var angle = arc.StartAngle + step * (i + 0.5);
                points.Add(
                    new Vector(
                        arc.Center.X + r * System.Math.Cos(angle),
                        arc.Center.Y + r * System.Math.Sin(angle)
                    )
                );
            }

            points.Add(arc.EndPoint());
        }

        private static void AddCircle(
            List<Vector> points,
            Circle circle,
            double tolerance,
            bool circumscribe
        )
        {
            if (!circumscribe)
            {
                points.AddRange(circle.ToPoints(circle.SegmentsForTolerance(tolerance)));
                return;
            }

            var segments = CircumscribedSegments(circle.Radius, Angle.TwoPI, tolerance);
            var step = Angle.TwoPI / segments;
            var r = circle.Radius / System.Math.Cos(step / 2);

            for (var i = 0; i < segments; i++)
            {
                points.Add(
                    new Vector(
                        circle.Center.X + r * System.Math.Cos(step * i),
                        circle.Center.Y + r * System.Math.Sin(step * i)
                    )
                );
            }
        }

        /// <summary>
        /// Segments for a circumscribed arc: a tangent-intersection vertex sits
        /// radius / cos(step / 2) from the center, so keep that within the tolerance, and
        /// keep each step at 90 degrees or less so the tangents meet close to the arc.
        /// </summary>
        private static int CircumscribedSegments(double radius, double sweep, double tolerance)
        {
            var maxHalfStep = System.Math.Acos(radius / (radius + tolerance));
            var segments = (int)System.Math.Ceiling(System.Math.Abs(sweep) / (2 * maxHalfStep));
            var quarters = (int)System.Math.Ceiling(System.Math.Abs(sweep) / Angle.HalfPI);

            return System.Math.Max(1, System.Math.Max(segments, quarters));
        }

        private static PathsD Union(PathsD region)
        {
            var clipper = new ClipperD(Precision);
            clipper.AddSubject(region);

            var solution = new PathsD();
            clipper.Execute(ClipType.Union, FillRule.NonZero, solution);
            return solution;
        }

        private static void AddShape(
            PathsD region,
            Shape shape,
            double tolerance,
            bool circumscribe,
            bool positive
        )
        {
            AddPolygon(region, Flatten(shape, tolerance, circumscribe), positive);
        }

        private static void AddPolygon(PathsD region, Polygon polygon, bool positive)
        {
            if (polygon.Vertices.Count < 3)
                return;

            var path = ToPath(polygon, positive);

            if (path.Count >= 3)
                region.Add(path);
        }
    }

    /// <summary>
    /// Result of <see cref="ClipperBridge.Offset(ShapeProfile, double, double, bool)"/>:
    /// outer boundaries (CCW) and holes (CW), as closed polygons.
    /// </summary>
    public sealed record OffsetRegion(List<Polygon> Outers, List<Polygon> Holes)
    {
        /// <summary>
        /// The outer boundary with the largest area, or null when the region is empty.
        /// </summary>
        public Polygon LargestOuter()
        {
            Polygon best = null;
            var bestArea = 0.0;

            foreach (var outer in Outers)
            {
                var area = outer.Area();

                if (best == null || area > bestArea)
                {
                    best = outer;
                    bestArea = area;
                }
            }

            return best;
        }
    }
}
