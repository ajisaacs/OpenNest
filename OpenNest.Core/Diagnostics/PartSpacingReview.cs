using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Diagnostics;

/// <summary>
/// Pairs of placed parts closer than the plate's part spacing. Candidates are found exactly as Auto
/// Nest's layout check finds them: each part's clean nesting material
/// (<see cref="NestingMaterial.ForNesting"/>, leads excluded) outlined by <see cref="SpacingOutline"/>,
/// the leftmost of a pair inflated by <see cref="SpacingOutline.InflationFor"/>. That outline
/// circumscribes arcs by up to its chord tolerance, more than the slack, and interactive fills
/// place native arcs exactly at the spacing. So each candidate is confirmed on
/// <see cref="NativeContours"/>, as the layout check confirms it, and reported only when that gap
/// is short of the spacing by more than the slack. Zero spacing enforces nothing, so contact stays
/// valid.
/// Pairs whose material already overlaps, and parts the overlap check could not read, are left to
/// that check. The sheet edge and clamps are not spacing-checked here.
/// </summary>
internal static class PartSpacingReview
{
    internal static void Add(Plate plate, int plateNumber, IReadOnlyList<Part> parts,
        IReadOnlyList<int> partNumbers, PlateOverlapReport overlap, List<PostVerificationFinding> findings,
        CancellationToken cancellationToken)
    {
        var spacing = plate.PartSpacing;
        if (!double.IsFinite(spacing) || spacing < 0)
        {
            findings.Add(new(PostVerificationKind.Incomplete, plateNumber, null, null,
                $"Part spacing check: plate part spacing {spacing} is not a finite, non-negative value."));
            return;
        }
        var inflateBy = SpacingOutline.InflationFor(spacing);
        if (inflateBy <= Tolerance.Epsilon || parts.Count < 2)
            return;

        var unreadable = overlap.Issues.Where(issue => issue.PartBId == null)
            .Select(issue => issue.PartAId).ToHashSet();
        var overlapping = overlap.Pairs.Select(pair => Key(pair.PartAId, pair.PartBId)).ToHashSet();
        var profiles = new Dictionary<(Program, long), ShapeProfile>();
        var shapes = new ShapeProfile[parts.Count];
        var fine = new NativeContours[parts.Count];
        var raw = new SpacingOutline[parts.Count];
        var inflated = new SpacingOutline[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (unreadable.Contains(i))
                continue;
            try
            {
                var profile = Profile(parts[i], profiles);
                if (profile == null)
                    continue;
                shapes[i] = profile;
                raw[i] = SpacingOutline.Build(profile, parts[i].Location, 0);
                inflated[i] = SpacingOutline.Build(profile, parts[i].Location, inflateBy);
                if (raw[i] == null || inflated[i] == null)
                    throw new ArgumentException("Part material has no closed perimeter.");
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                raw[i] = inflated[i] = null;
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, partNumbers[i], null,
                    "Part spacing check: " + exception.Message));
            }
        }

        var order = Enumerable.Range(0, parts.Count)
            .Where(i => raw[i] != null)
            .OrderBy(i => raw[i].Perimeter.BoundingBox.Left)
            .ToList();
        for (var a = 0; a < order.Count; a++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var i = order[a];
            var reach = inflated[i].Perimeter.BoundingBox;
            for (var b = a + 1; b < order.Count; b++)
            {
                var j = order[b];
                var other = raw[j].Perimeter.BoundingBox;
                // Sorted by Left, so nothing further along can reach part i either.
                if (other.Left > reach.Right + Tolerance.Epsilon)
                    break;
                if (!BoxesTouch(reach, other) || overlapping.Contains(Key(i, j))
                    || !inflated[i].Overlaps(raw[j]))
                    continue;
                double gap;
                Vector marker;
                try
                {
                    fine[i] ??= NativeContours.Of(shapes[i], parts[i].Location);
                    fine[j] ??= NativeContours.Of(shapes[j], parts[j].Location);
                    gap = fine[i].GapTo(fine[j], spacing, out marker);
                }
                catch (Exception exception) when (IsInvalid(exception))
                {
                    findings.Add(new(PostVerificationKind.Incomplete, plateNumber,
                        System.Math.Min(partNumbers[i], partNumbers[j]),
                        System.Math.Max(partNumbers[i], partNumbers[j]),
                        "Part spacing check: " + exception.Message));
                    continue;
                }
                if (!(gap < spacing - SpacingOutline.SpacingSlack))
                    continue;
                var (first, second) = partNumbers[i] < partNumbers[j] ? (i, j) : (j, i);
                findings.Add(new(PostVerificationKind.PartSpacing, plateNumber, partNumbers[first],
                    partNumbers[second],
                    $"'{Name(parts[first])}' and '{Name(parts[second])}' are {gap:F3} apart, closer than the "
                    + $"plate's part spacing ({spacing:F3}).")
                { Location = marker });
            }
        }
    }

    /// <summary>Clean drawing material, rotated to the part's pose; null when the part has none.
    /// Converted once per program and rotation.</summary>
    private static ShapeProfile Profile(Part part, Dictionary<(Program, long), ShapeProfile> profiles)
    {
        var program = part.BaseDrawing.Program;
        var rotation = part.Rotation - program.Rotation;
        if (!double.IsFinite(rotation))
            throw new ArgumentException("Part pose must be finite.");
        var key = (program, BitConverter.DoubleToInt64Bits(rotation));
        if (profiles.TryGetValue(key, out var cached))
            return cached;
        // Conversion creates fresh geometry, so rotating it never touches the live program.
        var entities = ConvertProgram.ToGeometry(SeamOvercut.Trimmed(program))
            .Where(entity => entity.Layer != SpecialLayers.Leadin && entity.Layer != SpecialLayers.Leadout)
            .ForNesting();
        if (rotation != 0)
            foreach (var entity in entities)
                entity.Rotate(rotation);
        var profile = entities.Count == 0 ? null : new ShapeProfile(entities);
        if (profile != null && profile.Perimeter == null)
            throw new ArgumentException("Part material has no closed perimeter.");
        profiles[key] = profile;
        return profile;
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    private static string Name(Part part) => part.BaseDrawing.Name;

    private static bool BoxesTouch(Box a, Box b) =>
        a.Left <= b.Right + Tolerance.Epsilon
        && b.Left <= a.Right + Tolerance.Epsilon
        && a.Bottom <= b.Top + Tolerance.Epsilon
        && b.Bottom <= a.Top + Tolerance.Epsilon;

    private static bool IsInvalid(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;
}
