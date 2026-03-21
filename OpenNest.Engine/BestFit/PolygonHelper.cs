using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Linq;

namespace OpenNest.Engine.BestFit
{
    public static class PolygonHelper
    {
        public static PolygonExtractionResult ExtractPerimeterPolygon(Drawing drawing, double halfSpacing)
        {
            var entities = ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid)
                .ToList();

            if (entities.Count == 0)
                return new PolygonExtractionResult(null, Vector.Zero);

            var definedShape = new ShapeProfile(entities);
            var perimeter = definedShape.Perimeter;

            if (perimeter == null)
                return new PolygonExtractionResult(null, Vector.Zero);

            // Compute the perimeter bounding box before inflation for coordinate correction.
            perimeter.UpdateBounds();
            var perimeterBb = perimeter.BoundingBox;

            // Inflate by half-spacing if spacing is non-zero.
            var inflated = halfSpacing > 0
                ? (perimeter.OffsetEntity(halfSpacing, OffsetSide.Left) as Shape ?? perimeter)
                : perimeter;

            // Convert to polygon with circumscribed arcs for tight nesting.
            var polygon = inflated.ToPolygonWithTolerance(0.01, circumscribe: true);

            if (polygon.Vertices.Count < 3)
                return new PolygonExtractionResult(null, Vector.Zero);

            // Compute correction: difference between program origin and perimeter origin.
            // Part.CreateAtOrigin normalizes to program bbox; polygon normalizes to perimeter bbox.
            var programBb = drawing.Program.BoundingBox();
            var correction = new Vector(
                perimeterBb.Left - programBb.Location.X,
                perimeterBb.Bottom - programBb.Location.Y);

            // Normalize: move reference point to origin.
            polygon.UpdateBounds();
            var bb = polygon.BoundingBox;
            polygon.Offset(-bb.Left, -bb.Bottom);

            return new PolygonExtractionResult(polygon, correction);
        }

        public static Polygon RotatePolygon(Polygon polygon, double angle)
        {
            if (angle.IsEqualTo(0))
                return polygon;

            var result = new Polygon();
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);

            foreach (var v in polygon.Vertices)
            {
                result.Vertices.Add(new Vector(
                    v.X * cos - v.Y * sin,
                    v.X * sin + v.Y * cos));
            }

            // Re-normalize to origin.
            result.UpdateBounds();
            var bb = result.BoundingBox;
            result.Offset(-bb.Left, -bb.Bottom);

            return result;
        }
    }

    public record PolygonExtractionResult(Polygon Polygon, Vector Correction);
}
