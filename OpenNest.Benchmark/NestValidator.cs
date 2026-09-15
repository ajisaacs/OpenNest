using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Benchmark
{
    public class ValidationResult
    {
        public bool Valid => Violations.Count == 0;
        public List<string> Violations { get; } = new();
    }

    /// <summary>
    /// Validates a placed layout against the benchmark rules: every part must lie
    /// within the plate's work area, every pair of parts must be at least
    /// PartSpacing apart, and no drawing may have more parts placed than requested.
    /// Geometry checks work on arbitrary (concave, holed) polygons by reusing the
    /// same world-space extraction Part.Intersects uses internally, so no engine
    /// gets an advantage or penalty from shape complexity.
    /// </summary>
    public static class NestValidator
    {
        public static ValidationResult Validate(List<Part> parts, Plate plate, BenchmarkJob job)
        {
            var result = new ValidationResult();

            if (parts == null || parts.Count == 0)
                return result;

            ValidateQuantities(parts, job, result);
            ValidateBounds(parts, plate, result);
            ValidateSpacing(parts, plate.PartSpacing, result);

            return result;
        }

        private static void ValidateQuantities(List<Part> parts, BenchmarkJob job, ValidationResult result)
        {
            var allowed = job.Requests.ToDictionary(r => r.Drawing.Id, r => r.Quantity);
            var placedCounts = parts
                .GroupBy(p => p.BaseDrawing.Id)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var (drawingId, placed) in placedCounts)
            {
                if (!allowed.TryGetValue(drawingId, out var max))
                {
                    result.Violations.Add($"Placed drawing id={drawingId} which was not requested for this job");
                    continue;
                }

                if (placed > max)
                {
                    var name = parts.First(p => p.BaseDrawing.Id == drawingId).BaseDrawing.Name;
                    result.Violations.Add($"'{name}': placed {placed} but only {max} were requested");
                }
            }
        }

        private static void ValidateBounds(List<Part> parts, Plate plate, ValidationResult result)
        {
            var workArea = plate.WorkArea();

            foreach (var part in parts)
            {
                var bb = part.BoundingBox;

                var outLeft = bb.Left < workArea.X - Tolerance.Epsilon;
                var outBottom = bb.Bottom < workArea.Y - Tolerance.Epsilon;
                var outRight = bb.Right > workArea.Right + Tolerance.Epsilon;
                var outTop = bb.Top > workArea.Top + Tolerance.Epsilon;

                if (outLeft || outBottom || outRight || outTop)
                {
                    result.Violations.Add(
                        $"'{part.BaseDrawing.Name}' at ({part.Location.X:F2},{part.Location.Y:F2}) falls outside the work area");
                }
            }
        }

        private static void ValidateSpacing(List<Part> parts, double spacing, ValidationResult result)
        {
            var worldPolygons = new Polygon[parts.Count];
            var inflatedPolygons = new Polygon[parts.Count];

            for (var i = 0; i < parts.Count; i++)
            {
                worldPolygons[i] = WorldPolygon(parts[i], 0);
                inflatedPolygons[i] = spacing > Tolerance.Epsilon ? WorldPolygon(parts[i], spacing) : worldPolygons[i];
            }

            for (var i = 0; i < parts.Count; i++)
            {
                if (worldPolygons[i] == null || inflatedPolygons[i] == null)
                    continue;

                for (var j = i + 1; j < parts.Count; j++)
                {
                    if (worldPolygons[j] == null)
                        continue;

                    if (Collision.HasOverlap(inflatedPolygons[i], worldPolygons[j]))
                    {
                        result.Violations.Add(
                            $"'{parts[i].BaseDrawing.Name}' and '{parts[j].BaseDrawing.Name}' are closer than the required spacing ({spacing:F3})");
                    }
                }
            }
        }

        /// <summary>
        /// Extracts a part's perimeter as a world-space polygon, optionally inflated
        /// outward by the given spacing, mirroring Part.Intersects' own geometry
        /// extraction (part.Program is already rotated; only a Location offset is needed).
        /// </summary>
        private static Polygon WorldPolygon(Part part, double inflateBy)
        {
            var entities = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid)
                .ToList();

            if (entities.Count == 0)
                return null;

            var perimeter = new ShapeProfile(entities).Perimeter;

            if (perimeter == null)
                return null;

            if (inflateBy > Tolerance.Epsilon)
                perimeter = perimeter.OffsetOutward(inflateBy) ?? perimeter;

            var polygon = perimeter.ToPolygon();

            if (polygon == null)
                return null;

            polygon.Offset(part.Location);
            return polygon;
        }
    }
}
