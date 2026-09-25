using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// Computes the rotation that maps a drawing to its canonical (MBR-axis-aligned) frame.
    /// Lives in OpenNest.Core so Drawing.Program setter can invoke it directly without
    /// a circular dependency on OpenNest.Engine.
    /// </summary>
    public static class CanonicalAngle
    {
        /// <summary>Angles with |v| below this (radians) are snapped to 0.</summary>
        public const double SnapToZero = 0.001;

        /// <summary>Centroid offsets below this fraction of the MBR extent count as symmetric.</summary>
        private const double SymmetryTolerance = 1e-6;

        /// <summary>Angular margin (radians) keeping axis-aligned centroid offsets off the edge of the preferred quadrant.</summary>
        private const double PreferenceMargin = 0.001;

        /// <summary>
        /// Derives the canonical angle from a pre-computed MBR. Used both by Compute (which
        /// computes the MBR itself) and by PartClassifier (which already has one). Single formula
        /// across both callers.
        /// </summary>
        public static double FromMbr(BoundingRectangleResult mbr)
        {
            if (mbr.Area <= OpenNest.Math.Tolerance.Epsilon)
                return 0.0;

            // The MBR edge angle can represent any of four equivalent orientations
            // (edge-i, edge-i + π/2, edge-i + π, edge-i - π/2) depending on which hull
            // edge the algorithm happened to pick. Normalize -mbr.Angle to the
            // representative in [-π/4, π/4] so snap-to-zero works for inputs near
            // ANY of the equivalent orientations.
            var angle = -mbr.Angle;
            const double halfPi = System.Math.PI / 2.0;
            angle -= halfPi * System.Math.Round(angle / halfPi);

            if (System.Math.Abs(angle) < SnapToZero)
                return 0.0;

            return angle;
        }

        public static double Compute(Drawing drawing)
        {
            if (drawing?.Program == null)
                return 0.0;

            var entities = ConvertProgram
                .ToGeometry(drawing.Program)
                .Where(e => SpecialLayers.IsMaterial(e.Layer));

            var shapes = ShapeBuilder.GetShapes(entities);
            if (shapes.Count == 0)
                return 0.0;

            var perimeter = shapes[0];
            var perimeterArea = perimeter.Area();
            for (var i = 1; i < shapes.Count; i++)
            {
                var area = shapes[i].Area();
                if (area > perimeterArea)
                {
                    perimeter = shapes[i];
                    perimeterArea = area;
                }
            }

            var polygon = perimeter.ToPolygonWithTolerance(0.1);
            if (polygon == null || polygon.Vertices.Count < 3)
                return 0.0;

            var hull = ConvexHull.Compute(polygon.Vertices);
            if (hull.Vertices.Count < 3)
                return 0.0;

            var mbr = RotatingCalipers.MinimumBoundingRectangle(hull);
            var angle = FromMbr(mbr);
            if (mbr.Area <= OpenNest.Math.Tolerance.Epsilon)
                return angle;

            var quarterTurns = PreferredQuarterTurns(polygon, hull, angle);
            if (quarterTurns == 0)
                return angle;

            return NormalizeSigned(angle + quarterTurns * System.Math.PI / 2.0);
        }

        /// <summary>
        /// The MBR only fixes the frame modulo 90°, leaving four equivalent orientations. Nest
        /// results are not 90°-symmetric, so pick one deterministically: the quarter-turn count
        /// that puts the perimeter's centroid toward the lower-left of its MBR. Shapes with no
        /// centroid offset (rectangles, circles) are symmetric and keep the MBR orientation.
        /// </summary>
        private static int PreferredQuarterTurns(Polygon polygon, Polygon hull, double angle)
        {
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            foreach (var vertex in hull.Vertices)
            {
                var rotated = vertex.Rotate(angle);
                minX = System.Math.Min(minX, rotated.X);
                minY = System.Math.Min(minY, rotated.Y);
                maxX = System.Math.Max(maxX, rotated.X);
                maxY = System.Math.Max(maxY, rotated.Y);
            }

            var centroid = Centroid(polygon).Rotate(angle);
            var dx = centroid.X - (minX + maxX) / 2.0;
            var dy = centroid.Y - (minY + maxY) / 2.0;

            var extent = System.Math.Max(maxX - minX, maxY - minY);
            if (System.Math.Sqrt(dx * dx + dy * dy) <= SymmetryTolerance * extent)
                return 0;

            // Choose k so the offset direction lands in [PI - margin, 3PI/2 - margin). The margin
            // keeps offsets lying exactly on an axis (mirror-symmetric parts) away from the
            // interval edge so floating-point noise cannot flip the choice.
            var halfPi = System.Math.PI / 2.0;
            var direction = System.Math.Atan2(dy, dx);
            for (var turns = 0; turns < 4; turns++)
            {
                var relative = direction + turns * halfPi - (System.Math.PI - PreferenceMargin);
                relative -= 2.0 * System.Math.PI * System.Math.Floor(relative / (2.0 * System.Math.PI));
                if (relative < halfPi)
                    return turns;
            }

            return 0;
        }

        private static Vector Centroid(Polygon polygon)
        {
            var vertices = polygon.Vertices;
            var doubleArea = 0.0;
            var cx = 0.0;
            var cy = 0.0;
            for (var i = 0; i < vertices.Count; i++)
            {
                var p = vertices[i];
                var q = vertices[(i + 1) % vertices.Count];
                var cross = p.X * q.Y - q.X * p.Y;
                doubleArea += cross;
                cx += (p.X + q.X) * cross;
                cy += (p.Y + q.Y) * cross;
            }

            if (System.Math.Abs(doubleArea) <= OpenNest.Math.Tolerance.Epsilon)
                return new Vector(vertices.Average(v => v.X), vertices.Average(v => v.Y));

            return new Vector(cx / (3.0 * doubleArea), cy / (3.0 * doubleArea));
        }

        private static double NormalizeSigned(double angle)
        {
            var twoPi = 2.0 * System.Math.PI;
            angle -= twoPi * System.Math.Floor((angle + System.Math.PI) / twoPi);
            return angle;
        }
    }
}
