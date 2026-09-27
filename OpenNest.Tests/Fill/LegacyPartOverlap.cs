#nullable disable
// Frozen, test-only copies from commit 82feb78b0fefdffc9ef9306205eeaf44e80e1d8d, taken before
// fill overlap checks used PartOverlapChecker: the Part.Intersects body (on LegacyCollision),
// FillLinear.HasOverlappingParts and FillHelpers.HasOverlappingParts. Only the member access
// (Program/Location -> self.*), the PerfCounters call and the collision type differ from the
// originals. Do not edit: differential tests use them as the independent pre-change oracle.
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;
using OpenNest.Tests.Geometry;

namespace OpenNest.Tests.Fill;

internal static class LegacyPartOverlap
{
    private const double IntersectsChordTolerance = 0.001;

    public static bool Intersects(Part self, Part part, out List<Vector> pts)
    {
        pts = new List<Vector>();

        var entities1 = ConvertProgram
            .ToGeometry(self.Program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer))
            .ToList();
        var entities2 = ConvertProgram
            .ToGeometry(part.Program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer))
            .ToList();

        if (entities1.Count == 0 || entities2.Count == 0)
            return false;

        var perimeter1 = new ShapeProfile(entities1).Perimeter;
        var perimeter2 = new ShapeProfile(entities2).Perimeter;

        if (perimeter1 == null || perimeter2 == null)
            return false;

        var polygon1 = perimeter1.ToPolygonWithTolerance(IntersectsChordTolerance);
        var polygon2 = perimeter2.ToPolygonWithTolerance(IntersectsChordTolerance);

        if (polygon1 == null || polygon2 == null)
            return false;

        polygon1.Offset(self.Location);
        polygon2.Offset(part.Location);

        var result = LegacyCollision.Check(polygon1, polygon2);
        pts = result.IntersectionPoints.ToList();
        return result.Overlaps;
    }

    /// <summary>World polygon exactly as the pre-change Part.Intersects built it.</summary>
    public static Polygon WorldPolygon(Part part)
    {
        var entities = ConvertProgram
            .ToGeometry(part.Program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer))
            .ToList();
        if (entities.Count == 0)
            return null;
        var perimeter = new ShapeProfile(entities).Perimeter;
        if (perimeter == null)
            return null;
        var polygon = perimeter.ToPolygonWithTolerance(IntersectsChordTolerance);
        polygon.Offset(part.Location);
        return polygon;
    }

    public static bool FillLinearHasOverlappingParts(
        List<Part> parts,
        out int overlapA,
        out int overlapB
    )
    {
        for (var i = 0; i < parts.Count; i++)
        {
            var b1 = parts[i].BoundingBox;

            for (var j = i + 1; j < parts.Count; j++)
            {
                var b2 = parts[j].BoundingBox;

                var overlapX =
                    System.Math.Min(b1.Right, b2.Right) - System.Math.Max(b1.Left, b2.Left);
                var overlapY =
                    System.Math.Min(b1.Top, b2.Top) - System.Math.Max(b1.Bottom, b2.Bottom);

                if (overlapX <= Tolerance.Epsilon || overlapY <= Tolerance.Epsilon)
                    continue;

                if (Intersects(parts[i], parts[j], out _))
                {
                    overlapA = i;
                    overlapB = j;
                    return true;
                }
            }
        }

        overlapA = -1;
        overlapB = -1;
        return false;
    }

    public static bool FillHelpersHasOverlappingParts(List<Part> parts)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            var b1 = parts[i].BoundingBox;

            for (var j = i + 1; j < parts.Count; j++)
            {
                var b2 = parts[j].BoundingBox;

                var overlapX =
                    System.Math.Min(b1.Right, b2.Right) - System.Math.Max(b1.Left, b2.Left);
                var overlapY =
                    System.Math.Min(b1.Top, b2.Top) - System.Math.Max(b1.Bottom, b2.Bottom);

                if (overlapX <= Tolerance.Epsilon || overlapY <= Tolerance.Epsilon)
                    continue;

                if (Intersects(parts[i], parts[j], out _))
                    return true;
            }
        }

        return false;
    }
}
