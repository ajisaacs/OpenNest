using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine
{
    public enum PartType { Rectangle, Circle, Irregular }

    public struct ClassificationResult
    {
        public PartType Type;
        public double Rectangularity;
        public double Circularity;
        public double PerimeterRatio;
        public double PrimaryAngle;
    }

    public static class PartClassifier
    {
        public const double RectangularityThreshold = 0.92;
        public const double PerimeterRatioThreshold = 0.85;
        public const double CircularityThreshold = 0.95;

        public static ClassificationResult Classify(Drawing drawing)
        {
            var result = new ClassificationResult();

            var entities = ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);

            var shapes = ShapeBuilder.GetShapes(entities);

            if (shapes.Count == 0)
                return result;

            // Find the largest shape (outer perimeter).
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

            // Convert to polygon for hull/MBR computation.
            var polygon = perimeter.ToPolygonWithTolerance(0.1);

            if (polygon == null || polygon.Vertices.Count < 3)
                return result;

            // Compute convex hull.
            var hull = ConvexHull.Compute(polygon.Vertices);
            var hullArea = hull.Area();

            // Compute MBR via rotating calipers.
            var mbr = RotatingCalipers.MinimumBoundingRectangle(hull);
            var mbrArea = mbr.Area;
            var mbrPerimeter = 2 * (mbr.Width + mbr.Height);

            // Store primary angle (negated to align MBR with axes, same as RotationAnalysis).
            result.PrimaryAngle = -mbr.Angle;

            // Drawing perimeter for circularity and perimeter ratio.
            var drawingPerimeter = polygon.Perimeter();

            // Circularity: 4*PI*area / perimeter^2. Circles ~ 1.0.
            if (drawingPerimeter > Tolerance.Epsilon)
                result.Circularity = 4 * System.Math.PI * perimeterArea / (drawingPerimeter * drawingPerimeter);

            // Check circle first (rotationally invariant).
            if (result.Circularity >= CircularityThreshold)
            {
                result.Type = PartType.Circle;
                return result;
            }

            // Rectangularity: hull area / MBR area.
            if (mbrArea > Tolerance.Epsilon)
                result.Rectangularity = hullArea / mbrArea;

            // Perimeter ratio: MBR perimeter / drawing perimeter.
            if (drawingPerimeter > Tolerance.Epsilon)
                result.PerimeterRatio = mbrPerimeter / drawingPerimeter;

            // Rectangle: both metrics pass thresholds.
            if (result.Rectangularity >= RectangularityThreshold
                && result.PerimeterRatio >= PerimeterRatioThreshold)
            {
                result.Type = PartType.Rectangle;
                return result;
            }

            result.Type = PartType.Irregular;
            return result;
        }
    }
}
