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
/// that check. Clamps are not spacing-checked here; <see cref="PlateEdgeReview"/> checks the sheet edge. The pre-post review and the
/// desktop overlap label share this scan, so they cannot disagree.
/// </summary>
internal static class PartSpacingReview
{
    /// <summary>One part to scan: <see cref="Id"/> is its position in the overlap check's input.
    /// The profile loads inside the scan's error boundary; null means no material.</summary>
    internal sealed record SpacingInput(int Id, Func<ShapeProfile> Profile, Vector Location);

    /// <summary>True when <paramref name="spacing"/> requires any gap at all.</summary>
    internal static bool Enforces(double spacing) =>
        double.IsFinite(spacing) && SpacingOutline.InflationFor(spacing) > Tolerance.Epsilon;

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
        var profiles = new Dictionary<(Program, long), ShapeProfile>();
        var inputs = parts.Select((part, id) => new SpacingInput(id, () => Profile(part, profiles), part.Location))
            .ToList();
        Scan(inputs, spacing, overlap.Pairs, overlap.Issues,
            (a, b, message) => findings.Add(new(PostVerificationKind.Incomplete, plateNumber, partNumbers[a],
                b is { } other ? partNumbers[other] : null, "Part spacing check: " + message)),
            (a, b, gap, marker) => findings.Add(new(PostVerificationKind.PartSpacing, plateNumber, partNumbers[a],
                partNumbers[b],
                $"'{Name(parts[a])}' and '{Name(parts[b])}' are {gap:F3} apart, closer than the "
                + $"plate's part spacing ({spacing:F3}).")
            { Location = marker }),
            cancellationToken);
    }

    /// <summary>
    /// The desktop label's check: the same scan over a snapshot's owned material, so it runs on
    /// the overlap worker and never reads a live part. Pairs and issues keep input ids.
    /// </summary>
    internal static void Add(PlateOverlapSnapshot snapshot, IReadOnlyList<PlateOverlapPair> overlaps,
        IReadOnlyList<PlateOverlapIssue> overlapIssues, List<PlateSpacingPair> pairs,
        List<PlateOverlapIssue> issues, CancellationToken cancellationToken)
    {
        var spacing = snapshot.PartSpacing;
        if (!Enforces(spacing))
            return;
        var names = snapshot.Parts.ToDictionary(part => part.Id, part => part.Name);
        var profiles = new Dictionary<(OverlapSource, long), ShapeProfile>();
        var inputs = snapshot.Parts
            .Select(part => new SpacingInput(part.Id, () => Profile(part, profiles), part.Location)).ToList();
        Scan(inputs, spacing, overlaps, overlapIssues,
            (a, b, message) => issues.Add(new PlateOverlapIssue(a, b, message)),
            (a, b, gap, marker) => pairs.Add(new PlateSpacingPair(a, b, names[a], names[b], gap, marker)),
            cancellationToken);
    }

    /// <summary>
    /// Reports each pair by input id, lower id first. <paramref name="incomplete"/> receives a
    /// part or pair that could not be read; <paramref name="tooClose"/> a confirmed gap and its marker.
    /// </summary>
    private static void Scan(IReadOnlyList<SpacingInput> parts, double spacing,
        IEnumerable<PlateOverlapPair> overlaps, IEnumerable<PlateOverlapIssue> overlapIssues,
        Action<int, int?, string> incomplete, Action<int, int, double, Vector> tooClose,
        CancellationToken cancellationToken)
    {
        var inflateBy = SpacingOutline.InflationFor(spacing);
        if (inflateBy <= Tolerance.Epsilon || parts.Count < 2)
            return;

        var unreadable = overlapIssues.Where(issue => issue.PartBId == null)
            .Select(issue => issue.PartAId).ToHashSet();
        var overlapping = overlaps.Select(pair => Key(pair.PartAId, pair.PartBId)).ToHashSet();
        var outlines = new Dictionary<ShapeProfile, (SpacingOutline Raw, SpacingOutline Inflated)>();
        var shapes = new ShapeProfile[parts.Count];
        var fine = new NativeContours[parts.Count];
        var raw = new SpacingOutline[parts.Count];
        var inflated = new SpacingOutline[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (unreadable.Contains(parts[i].Id))
                continue;
            try
            {
                var profile = parts[i].Profile();
                if (profile == null)
                    continue;
                shapes[i] = profile;
                // Repeated parts share a profile, so each shape is offset once.
                if (!outlines.TryGetValue(profile, out var outline))
                {
                    outline = (SpacingOutline.Build(profile, Vector.Zero, 0),
                        SpacingOutline.Build(profile, Vector.Zero, inflateBy));
                    outlines[profile] = outline;
                }
                if (outline.Raw == null || outline.Inflated == null)
                    throw new ArgumentException("Part material has no closed perimeter.");
                raw[i] = outline.Raw.MovedTo(parts[i].Location);
                inflated[i] = outline.Inflated.MovedTo(parts[i].Location);
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                raw[i] = inflated[i] = null;
                incomplete(parts[i].Id, null, exception.Message);
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
                var (first, second) = Key(parts[i].Id, parts[j].Id);
                if (!BoxesTouch(reach, other) || overlapping.Contains((first, second))
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
                    incomplete(first, second, exception.Message);
                    continue;
                }
                if (gap < spacing - SpacingOutline.SpacingSlack)
                    tooClose(first, second, gap, marker);
            }
        }
    }

    /// <summary>Clean drawing material, rotated to the part's pose; null when the part has none.
    /// Converted once per program and rotation.</summary>
    internal static ShapeProfile Profile(Part part, Dictionary<(Program, long), ShapeProfile> profiles)
    {
        var program = part.BaseDrawing.Program;
        var rotation = part.Rotation - program.Rotation;
        if (!double.IsFinite(rotation))
            throw new ArgumentException("Part pose must be finite.");
        var key = (program, BitConverter.DoubleToInt64Bits(rotation));
        if (profiles.TryGetValue(key, out var cached))
            return cached;
        // Conversion creates fresh geometry, so rotating it never touches the live program.
        var profile = Rotated(ConvertProgram.ToGeometry(SeamOvercut.Trimmed(program))
            .Where(entity => entity.Layer != SpecialLayers.Leadin && entity.Layer != SpecialLayers.Leadout),
            rotation);
        profiles[key] = profile;
        return profile;
    }

    /// <summary>A snapshot source's material at a part's pose, from clones: sources are shared.</summary>
    private static ShapeProfile Profile(CapturedOverlapPart part,
        Dictionary<(OverlapSource, long), ShapeProfile> profiles)
    {
        var key = (part.Source, BitConverter.DoubleToInt64Bits(part.Rotation));
        if (profiles.TryGetValue(key, out var cached))
            return cached;
        var profile = Rotated(part.Source.Entities.Select(entity => entity.Clone()), part.Rotation);
        profiles[key] = profile;
        return profile;
    }

    private static ShapeProfile Rotated(IEnumerable<Entity> owned, double rotation)
    {
        var entities = owned.ForNesting();
        if (rotation != 0)
            foreach (var entity in entities)
                entity.Rotate(rotation);
        var profile = entities.Count == 0 ? null : new ShapeProfile(entities);
        if (profile != null && profile.Perimeter == null)
            throw new ArgumentException("Part material has no closed perimeter.");
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
