using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.BestFit
{
    public static class PolygonHelper
    {
        public static PolygonExtractionResult ExtractPerimeterPolygon(
            Drawing drawing,
            double halfSpacing
        )
        {
            var entities = ConvertProgram
                .ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid)
                .ToList();

            if (entities.Count == 0)
                return new PolygonExtractionResult(null, Vector.Zero);

            var definedShape = new ShapeProfile(entities);
            var perimeter = definedShape.Perimeter;

            if (perimeter == null)
                return new PolygonExtractionResult(null, Vector.Zero);

            // Circumscribe so the polygon never under-estimates the part (or its offset).
            var polygon =
                halfSpacing > 0
                    ? ClipperBridge
                        .OffsetPerimeter(perimeter, halfSpacing, 0.01, circumscribe: true)
                        .LargestOuter()
                    : perimeter.ToPolygonWithTolerance(0.01, circumscribe: true);

            if (polygon == null || polygon.Vertices.Count < 3)
                return new PolygonExtractionResult(null, Vector.Zero);

            // Normalize: move polygon to origin.
            polygon.UpdateBounds();
            var bb = polygon.BoundingBox;
            polygon.Offset(-bb.Left, -bb.Bottom);

            // No correction needed: BestFitFinder always pairs the same drawing with
            // itself, so the polygon-to-part offset is identical for both parts and
            // cancels out in the NFP displacement.
            return new PolygonExtractionResult(polygon, Vector.Zero);
        }

        public static Polygon RotatePolygon(Polygon polygon, double angle, bool reNormalize = true)
        {
            if (angle.IsEqualTo(0))
                return polygon;

            var result = new Polygon();
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);

            foreach (var v in polygon.Vertices)
            {
                result.Vertices.Add(new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos));
            }

            if (reNormalize)
            {
                // Re-normalize to origin.
                result.UpdateBounds();
                var bb = result.BoundingBox;
                result.Offset(-bb.Left, -bb.Bottom);
            }

            return result;
        }
    }

    public record PolygonExtractionResult(Polygon Polygon, Vector Correction);
}
