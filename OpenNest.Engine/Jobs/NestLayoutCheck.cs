using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs;

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
public static class NestLayoutCheck
{
    /// <summary>Checks bounds, spacing, quantities, offered stock and rotation policies.
    /// Requirement IDs are used in messages. Instance indices and fulfillment metadata are
    /// not checked, matching the benchmark contract.</summary>
    public static IReadOnlyList<string> Violations(NestJob job, NestJobResult result)
    {
        var materialized = NestResultMaterializer.Materialize(job, result);
        var requirements = job.Parts.ToDictionary(p => materialized.DrawingsByPartId[p.Id],
            p => (p.Id, p.Quantity));
        var runs = materialized.Nest.Plates.Select(p => (p, p.Parts.ToList())).ToList();
        var violations = Validate(runs, requirements);
        ValidateAgainstJob(job, result, job.Parts.ToDictionary(p => p.Id, p => p.Id), violations);
        return violations;
    }

    /// <summary>Tests material clearance using the benchmark's conservative outlines.
    /// The leftmost raw outline is inflated, matching the full-layout sweep; ties retain
    /// argument order. Geometry is cloned before transformation.</summary>
    public static bool Clears(JobPartGeometry a, NestJobPlacement pa,
        JobPartGeometry b, NestJobPlacement pb, double spacing)
    {
        var ap = Transform(a, pa.Rotation);
        var bp = Transform(b, pb.Rotation);
        var al = new Vector(pa.X, pa.Y);
        var bl = new Vector(pb.X, pb.Y);
        var ar = Outline(ap, al, 0);
        var br = Outline(bp, bl, 0);
        if (ar.Perimeter.BoundingBox.Left > br.Perimeter.BoundingBox.Left)
        {
            (ap, bp) = (bp, ap);
            (al, bl) = (bl, al);
            (ar, br) = (br, ar);
        }
        var inflateBy = InflationFor(spacing);
        var inflated = inflateBy > Tolerance.Epsilon ? Outline(ap, al, inflateBy) : ar;
        return !BoxesTouch(inflated.Perimeter.BoundingBox, br.Perimeter.BoundingBox)
            || !Overlaps(inflated, br);
    }

    private static ShapeProfile Transform(JobPartGeometry geometry, double rotation)
    {
        var shapes = new[] { geometry.Perimeter }.Concat(geometry.Cutouts);
        var entities = shapes.SelectMany(s => s.Entities).Select(e => e.Clone()).ToList();
        foreach (var entity in entities)
            entity.Rotate(rotation);
        return new ShapeProfile(entities);
    }


    /// <summary>
    /// requirements maps each materialized part's BaseDrawing (by reference - materialized
    /// Drawing instances are freshly reconstructed per NestResultMaterializer.Materialize, so
    /// identity must never be inferred from Name, which is only incidentally seeded from the
    /// originating NestJobPart id) to its original quantity limit and display name.
    /// </summary>
    internal static List<string> Validate(
        List<(Plate Plate, List<Part> Parts)> plateRuns,
        IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements
    )
    {
        var result = new List<string>();
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

    /// <summary>
    /// Checks what the materialized layout cannot show: every sheet must be
    /// one of the job's own stock entries (an engine may not invent a sheet
    /// size or loosen its spacing/edge settings, which the layout checks
    /// would otherwise trust), finite stock may not be overdrawn, and every
    /// placement's rotation must satisfy its part's RotationPolicy.
    /// </summary>
    internal static void ValidateAgainstJob(
        NestJob job,
        NestJobResult jobResult,
        IReadOnlyDictionary<string, string> displayNames,
        List<string> result
    )
    {
        var stockById = job.Plates.ToDictionary(s => s.Id);
        var partsById = job.Parts.ToDictionary(p => p.Id);
        var sheetsUsed = new Dictionary<string, int>();

        foreach (var sheet in jobResult.Plates)
        {
            if (
                !stockById.TryGetValue(sheet.Stock.Id, out var stock)
                || !SameSettings(stock, sheet.Stock)
            )
            {
                result.Add(
                    $"Plate {sheet.PlateIndex} uses stock '{sheet.Stock.Id}' ({sheet.Stock.Size}) that does not match any stock offered by the job"
                );
                continue;
            }

            sheetsUsed[stock.Id] = sheetsUsed.GetValueOrDefault(stock.Id) + 1;
        }

        foreach (var (stockId, used) in sheetsUsed)
        {
            var available = stockById[stockId].Quantity;

            if (available.HasValue && used > available.Value)
            {
                result.Add(
                    $"Used {used} sheet(s) of stock '{stockId}' but only {available.Value} are available"
                );
            }
        }

        foreach (var sheet in jobResult.Plates)
        {
            foreach (var placement in sheet.Placements)
            {
                if (!partsById.TryGetValue(placement.PartId, out var part))
                    continue; // reported by ValidateQuantities

                if (!part.Rotation.Allows(placement.Rotation))
                {
                    var name = displayNames.TryGetValue(part.Id, out var n) ? n : part.Id;
                    result.Add(
                        $"'{name}' placed at {Angle.ToDegrees(placement.Rotation):F3}° on plate {sheet.PlateIndex}, "
                            + $"outside its rotation constraint ({Describe(part.Rotation)})"
                    );
                }
            }
        }
    }

    private static bool SameSettings(NestPlateStock expected, NestPlateStock actual) =>
        ReferenceEquals(expected, actual)
        || (
            expected.Size.Equals(actual.Size)
            && expected.PartSpacing.IsEqualTo(actual.PartSpacing)
            && expected.EdgeSpacing.Left.IsEqualTo(actual.EdgeSpacing.Left)
            && expected.EdgeSpacing.Right.IsEqualTo(actual.EdgeSpacing.Right)
            && expected.EdgeSpacing.Top.IsEqualTo(actual.EdgeSpacing.Top)
            && expected.EdgeSpacing.Bottom.IsEqualTo(actual.EdgeSpacing.Bottom)
            && expected.Quadrant == actual.Quadrant
        );

    private static string Describe(RotationPolicy policy) =>
        policy.Kind switch
        {
            RotationPolicyKind.Fixed => $"fixed at {Angle.ToDegrees(policy.Start):F3}°",
            RotationPolicyKind.BoundedSweep =>
                $"{Angle.ToDegrees(policy.Start):F3}° to {Angle.ToDegrees(policy.End):F3}° in {Angle.ToDegrees(policy.Step):F3}° steps",
            _ => "any",
        };

    private static void ValidateQuantities(
        List<Part> parts,
        IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements,
        List<string> result
    )
    {
        var placedCounts = parts
            .GroupBy<Part, Drawing>(p => p.BaseDrawing, ReferenceEqualityComparer.Instance)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (drawing, placed) in placedCounts)
        {
            if (!requirements.TryGetValue(drawing, out var requirement))
            {
                result.Add(
                    $"Placed drawing '{drawing.Name}' which was not requested for this job"
                );
                continue;
            }

            if (placed > requirement.Quantity)
            {
                result.Add(
                    $"'{requirement.Name}': placed {placed} across all plates but only {requirement.Quantity} were requested"
                );
            }
        }
    }

    private static void ValidateBounds(
        List<Part> parts,
        Plate plate,
        IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements,
        List<string> result
    )
    {
        var workArea = plate.WorkArea();

        foreach (var part in parts)
        {
            var bb = MaterialBounds(part);

            var outLeft = bb.Left < workArea.X - Tolerance.Epsilon;
            var outBottom = bb.Bottom < workArea.Y - Tolerance.Epsilon;
            var outRight = bb.Right > workArea.Right + Tolerance.Epsilon;
            var outTop = bb.Top > workArea.Top + Tolerance.Epsilon;

            if (outLeft || outBottom || outRight || outTop)
            {
                result.Add(
                    $"'{DisplayName(part, requirements)}' at ({part.Location.X:F2},{part.Location.Y:F2}) falls outside the work area "
                        + $"of a {plate.Size} plate"
                );
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
    private static void ValidateAreaBudget(
        List<Part> parts,
        Plate plate,
        List<string> result
    )
    {
        var workArea = plate.WorkArea();
        var budget = workArea.Width * workArea.Length;
        var placedArea = parts.Sum(p => p.BaseDrawing.Area);

        if (placedArea > budget + Tolerance.Epsilon)
        {
            result.Add(
                $"Combined placed area ({placedArea:F2}) on a {plate.Size} plate exceeds its work area ({budget:F2}) - "
                    + "parts must overlap even though the polygon overlap check did not flag a pair"
            );
        }
    }

    /// <summary>
    /// Every pair of parts must be at least <paramref name="spacing"/> apart.
    /// Each part's material is inflated by the spacing (perimeter offset
    /// outward, holes shrunk inward) and tested against the other part's raw
    /// material, with holes subtracted on both sides - so a small part
    /// nested inside another part's cutout (part-in-part) is legal as long as
    /// it clears the cutout's edge by the spacing. Pairs are pruned with an
    /// X-sorted sweep over bounding boxes so only neighbours reach the
    /// polygon clipper.
    /// </summary>
    private static void ValidateSpacing(
        List<Part> parts,
        double spacing,
        IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements,
        List<string> result
    )
    {
        var raw = new PartOutline[parts.Count];
        var inflated = new PartOutline[parts.Count];
        var inflateBy = InflationFor(spacing);

        for (var i = 0; i < parts.Count; i++)
        {
            raw[i] = Outline(parts[i], 0);
            inflated[i] = inflateBy > Tolerance.Epsilon ? Outline(parts[i], inflateBy) : raw[i];
        }

        var order = Enumerable
            .Range(0, parts.Count)
            .Where(i => raw[i] != null && inflated[i] != null)
            .OrderBy(i => raw[i].Perimeter.BoundingBox.Left)
            .ToList();

        for (var a = 0; a < order.Count; a++)
        {
            var i = order[a];
            var reach = inflated[i].Perimeter.BoundingBox;

            for (var b = a + 1; b < order.Count; b++)
            {
                var j = order[b];
                var other = raw[j].Perimeter.BoundingBox;

                // Sorted by Left, so nothing further along can reach part i either.
                if (other.Left > reach.Right + Tolerance.Epsilon)
                    break;

                if (!BoxesTouch(reach, other))
                    continue;

                // Inflating one side by the full spacing covers both cases: part j
                // inside part i's (shrunk) cutout, or part i's inflated outline
                // inside part j's raw cutout.
                if (Overlaps(inflated[i], raw[j]))
                {
                    result.Add(
                        $"'{DisplayName(parts[i], requirements)}' and '{DisplayName(parts[j], requirements)}' are closer than the required spacing ({spacing:F3})"
                    );
                }
            }
        }
    }

    /// <summary>Analytic world-space material bounds; surface marks never bound material.</summary>
    internal static Box MaterialBounds(Part part) =>
        ConvertProgram.ToGeometry(part.Program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer))
            .GetBoundingBox()
            .Translate(part.Location);

    /// <summary>Inflation that enforces <paramref name="spacing"/> less the shared slack.</summary>
    private static double InflationFor(double spacing) =>
        System.Math.Max(0, spacing - NestTolerances.SpacingSlack);

    private static bool BoxesTouch(Box a, Box b) =>
        a.Left <= b.Right + Tolerance.Epsilon
        && b.Left <= a.Right + Tolerance.Epsilon
        && a.Bottom <= b.Top + Tolerance.Epsilon
        && b.Bottom <= a.Top + Tolerance.Epsilon;

    /// <summary>Friendly name for a violation message, falling back to the materialized
    /// Drawing's own Name (the raw partId string) if this part wasn't in requirements at all -
    /// that mismatch is already reported by ValidateQuantities, so this is display-only.</summary>
    private static string DisplayName(
        Part part,
        IReadOnlyDictionary<Drawing, (string Name, int Quantity)> requirements
    ) =>
        requirements.TryGetValue(part.BaseDrawing, out var requirement)
            ? requirement.Name
            : part.BaseDrawing.Name;

    private const double OutlineTolerance = NestTolerances.ValidationOutline;

    private static bool Overlaps(PartOutline a, PartOutline b)
    {
        var at = a.Triangles;
        var bt = b.Triangles;
        // Cache the same world-space triangulation the reference would build. Keeping
        // translations at zero also preserves its floating-point operation order.
        return (at != null && bt != null ? at.Overlaps(bt, 0, 0, 0, 0) : null)
            ?? Collision.HasOverlap(a.Perimeter, b.Perimeter, a.Holes, b.Holes);
    }

    private sealed class PartOutline
    {
        public Polygon Perimeter { get; init; }
        public List<Polygon> Holes { get; init; }

        private bool prepared;
        private TriangulatedRegion triangles;

        /// <summary>Prepared on the first candidate pair; null preparation is cached too.</summary>
        public TriangulatedRegion Triangles
        {
            get
            {
                if (!prepared)
                {
                    triangles = TriangulatedRegion.Build(Perimeter, Holes);
                    prepared = true;
                }
                return triangles;
            }
        }
    }

    /// <summary>
    /// Extracts a part's material as world-space polygons - the perimeter and
    /// its cutouts - grown by <paramref name="inflateBy"/> (perimeter offset
    /// outward, cutouts offset inward, in one Clipper region offset). A cutout
    /// that closes up under the offset is dropped, which treats it as solid:
    /// conservative, since it has no room for another part at the required
    /// spacing anyway. Arcs are flattened conservatively (perimeter arcs
    /// circumscribed, cutout arcs inscribed) but nothing is padded. Callers inflate
    /// by the spacing less NestTolerances.SpacingSlack, so a layout exactly at the
    /// spacing passes despite rounding; the only other leniency is the round-join
    /// chord error at convex corners (OutlineTolerance / 10).
    /// part.Program is already rotated; only a Location offset is needed.
    /// </summary>
    private static PartOutline Outline(Part part, double inflateBy)
    {
        var entities = ConvertProgram
            .ToGeometry(part.Program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer))
            .ToList();

        if (entities.Count == 0)
            return null;

        var profile = new ShapeProfile(entities);

        if (profile.Perimeter == null)
            return null;

        return Outline(profile, part.Location, inflateBy);
    }

    private static PartOutline Outline(ShapeProfile profile, Vector location, double inflateBy)
    {
        // Adaptive tolerance instead of Shape.ToPolygon()'s default (up to 1000
        // segments per arc) - arc-heavy real parts otherwise produce thousands
        // of vertices, which is needlessly slow for a spacing check.
        var region = ClipperBridge.OffsetForValidation(
            profile,
            inflateBy > Tolerance.Epsilon ? inflateBy : 0,
            OutlineTolerance
        );

        var perimeter = region.LargestOuter();

        if (perimeter == null)
            return null;

        ToWorld(perimeter, location);

        foreach (var hole in region.Holes)
            ToWorld(hole, location);

        return new PartOutline { Perimeter = perimeter, Holes = region.Holes };
    }

    private static void ToWorld(Polygon polygon, Vector location)
    {
        polygon.Offset(location);
        polygon.UpdateBounds();
    }
}
