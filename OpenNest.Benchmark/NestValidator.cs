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
    /// Validates a (possibly multi-plate) placed layout against the benchmark
    /// rules: on every plate, every part must lie within that plate's work
    /// area and every pair of parts must be at least PartSpacing apart; across
    /// all plates combined, no drawing may have more parts placed than
    /// requested (the quantity limit is a property of the whole order, not of
    /// any one plate). Geometry checks work on arbitrary (concave, holed)
    /// polygons by reusing the same world-space extraction Part.Intersects
    /// uses internally, so no engine gets an advantage or penalty from shape
    /// complexity.
    /// </summary>
    public static class NestValidator
    {
        /// <summary>
        /// requirements maps each materialized part's BaseDrawing (by reference - materialized
        /// Drawing instances are freshly reconstructed per NestResultMaterializer.Materialize, so
        /// identity must never be inferred from Name, which is only incidentally seeded from the
        /// originating NestJobPart id) to its original quantity limit and display name.
        /// </summary>
        public static ValidationResult Validate(List<(Plate Plate, List<Part> Parts)> plateRuns,
            IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements)
        {
            var result = new ValidationResult();
            var allParts = plateRuns.SelectMany(pr => pr.Parts).ToList();

            if (allParts.Count == 0)
                return result;

            ValidateQuantities(allParts, requirements, result);

            foreach (var (plate, parts) in plateRuns)
            {
                if (parts.Count == 0)
                    continue;

                ValidateBounds(parts, plate, requirements, result);
                ValidateAreaBudget(parts, plate, result);
                ValidateSpacing(parts, plate.PartSpacing, requirements, result);
            }

            return result;
        }

        private static void ValidateQuantities(List<Part> parts,
            IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements, ValidationResult result)
        {
            var placedCounts = parts
                .GroupBy<Part, Drawing>(p => p.BaseDrawing, ReferenceEqualityComparer.Instance)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var (drawing, placed) in placedCounts)
            {
                if (!requirements.TryGetValue(drawing, out var requirement))
                {
                    result.Violations.Add($"Placed drawing '{drawing.Name}' which was not requested for this job");
                    continue;
                }

                if (placed > requirement.Quantity)
                {
                    result.Violations.Add(
                        $"'{requirement.Name}': placed {placed} across all plates but only {requirement.Quantity} were requested");
                }
            }
        }

        private static void ValidateBounds(List<Part> parts, Plate plate,
            IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements, ValidationResult result)
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
                        $"'{DisplayName(part, requirements)}' at ({part.Location.X:F2},{part.Location.Y:F2}) falls outside the work area " +
                        $"of a {plate.Size} plate");
                }
            }
        }

        /// <summary>
        /// Hard mathematical backstop: non-overlapping parts confined to the
        /// work area can never have a combined area greater than the work
        /// area itself. This catches overlap that the polygon-based
        /// ValidateSpacing check can miss - Collision.HasOverlap (and
        /// Part.Intersects, which uses the same algorithm) has been observed
        /// to return false negatives on real, complex production geometry, so
        /// this check does not depend on it.
        /// </summary>
        private static void ValidateAreaBudget(List<Part> parts, Plate plate, ValidationResult result)
        {
            var workArea = plate.WorkArea();
            var budget = workArea.Width * workArea.Length;
            var placedArea = parts.Sum(p => p.BaseDrawing.Area);

            if (placedArea > budget + Tolerance.Epsilon)
            {
                result.Violations.Add(
                    $"Combined placed area ({placedArea:F2}) on a {plate.Size} plate exceeds its work area ({budget:F2}) - " +
                    "parts must overlap even though the polygon overlap check did not flag a pair");
            }
        }

        private static void ValidateSpacing(List<Part> parts, double spacing,
            IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements, ValidationResult result)
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
                            $"'{DisplayName(parts[i], requirements)}' and '{DisplayName(parts[j], requirements)}' are closer than the required spacing ({spacing:F3})");
                    }
                }
            }
        }

        /// <summary>Friendly name for a violation message, falling back to the materialized
        /// Drawing's own Name (the raw partId string) if this part wasn't in requirements at all -
        /// that mismatch is already reported by ValidateQuantities, so this is display-only.</summary>
        private static string DisplayName(Part part, IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements) =>
            requirements.TryGetValue(part.BaseDrawing, out var requirement) ? requirement.Name : part.BaseDrawing.Name;

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

            // Adaptive tolerance instead of Shape.ToPolygon()'s default (up to 1000
            // segments per arc) - arc-heavy real parts otherwise produce thousands
            // of vertices, which is needlessly slow for a spacing check.
            var polygon = perimeter.ToPolygonWithTolerance(0.01, circumscribe: true);

            if (polygon == null)
                return null;

            polygon.Offset(part.Location);
            return polygon;
        }
    }
}
