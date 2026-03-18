using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    internal static class RotationAnalysis
    {
        /// <summary>
        /// Finds the rotation angle that minimizes the bounding rectangle of a drawing's
        /// largest shape, constrained by the NestItem's rotation range.
        /// </summary>
        public static double FindBestRotation(NestItem item)
        {
            var entities = ConvertProgram.ToGeometry(item.Drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);

            var shapes = ShapeBuilder.GetShapes(entities);

            if (shapes.Count == 0)
                return 0;

            // Find the largest shape (outer profile).
            var largest = shapes[0];
            var largestArea = largest.Area();

            for (var i = 1; i < shapes.Count; i++)
            {
                var area = shapes[i].Area();
                if (area > largestArea)
                {
                    largest = shapes[i];
                    largestArea = area;
                }
            }

            // Convert to polygon so arcs are properly represented as line segments.
            // Shape.FindBestRotation() uses Entity cardinal points which are incorrect
            // for arcs that don't sweep through all 4 cardinal directions.
            var polygon = largest.ToPolygonWithTolerance(0.1);

            BoundingRectangleResult result;

            if (item.RotationStart.IsEqualTo(0) && item.RotationEnd.IsEqualTo(0))
                result = polygon.FindBestRotation();
            else
                result = polygon.FindBestRotation(item.RotationStart, item.RotationEnd);

            // Negate the angle to align the minimum bounding rectangle with the axes.
            return -result.Angle;
        }

        /// <summary>
        /// Computes the convex hull of the parts' geometry and returns the unique
        /// edge angles, suitable for use as candidate rotation angles.
        /// </summary>
        public static List<double> FindHullEdgeAngles(List<Part> parts)
        {
            var points = new List<Vector>();

            foreach (var part in parts)
            {
                var entities = ConvertProgram.ToGeometry(part.Program)
                    .Where(e => e.Layer != SpecialLayers.Rapid);

                var shapes = ShapeBuilder.GetShapes(entities);

                foreach (var shape in shapes)
                {
                    var polygon = shape.ToPolygonWithTolerance(0.1);

                    foreach (var vertex in polygon.Vertices)
                        points.Add(vertex + part.Location);
                }
            }

            if (points.Count < 3)
                return new List<double> { 0 };

            var hull = ConvexHull.Compute(points);
            return GetHullEdgeAngles(hull);
        }

        public static List<double> GetHullEdgeAngles(Polygon hull)
        {
            var vertices = hull.Vertices;
            var n = hull.IsClosed() ? vertices.Count - 1 : vertices.Count;

            // Collect edges with their squared length so we can sort by longest first.
            var edges = new List<(double angle, double lengthSq)>();

            for (var i = 0; i < n; i++)
            {
                var next = (i + 1) % n;
                var dx = vertices[next].X - vertices[i].X;
                var dy = vertices[next].Y - vertices[i].Y;
                var lengthSq = dx * dx + dy * dy;

                if (lengthSq < Tolerance.Epsilon)
                    continue;

                var angle = -System.Math.Atan2(dy, dx);

                if (!edges.Any(e => e.angle.IsEqualTo(angle)))
                    edges.Add((angle, lengthSq));
            }

            // Longest edges first — they produce the flattest tiling rows.
            edges.Sort((a, b) => b.lengthSq.CompareTo(a.lengthSq));

            var angles = new List<double>(edges.Count + 1) { 0 };

            foreach (var (angle, _) in edges)
            {
                if (!angles.Any(a => a.IsEqualTo(angle)))
                    angles.Add(angle);
            }

            return angles;
        }
    }
}
