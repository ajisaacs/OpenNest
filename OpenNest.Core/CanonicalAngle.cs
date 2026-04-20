using OpenNest.Converters;
using OpenNest.Geometry;
using System.Linq;

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

            var entities = ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);

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
            return FromMbr(mbr);
        }
    }
}
