#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Converters;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Rectangles;

/// <summary>One allowed rotation of a part, reduced to its analytic material bounding box.</summary>
/// <param name="Angle">Rotation in radians about the snapshot origin.</param>
/// <param name="Width">Material X extent after rotation.</param>
/// <param name="Height">Material Y extent after rotation.</param>
/// <param name="OffsetX">Rotated material bounds' left edge relative to the snapshot origin.</param>
/// <param name="OffsetY">Rotated material bounds' bottom edge relative to the snapshot origin.</param>
internal sealed record BoxOrientation(double Angle, double Width, double Height, double OffsetX, double OffsetY);

/// <summary>A requested part type: every instance shares the same orientations.</summary>
internal sealed record BoxType(
    int Index,
    NestJobPart Part,
    IReadOnlyList<BoxOrientation> Orientations,
    double MaterialArea)
{
    public string Id => Part.Id;
    public int Priority => Part.Priority;

    /// <summary>Smallest bounding-box area over the allowed orientations.</summary>
    public double BoxArea => Orientations.Count == 0 ? 0 : Orientations.Min(o => o.Width * o.Height);

    /// <summary>Shortest side over all orientations; free space narrower than this is useless.</summary>
    public double MinSide => Orientations.Count == 0 ? double.MaxValue
        : Orientations.Min(o => System.Math.Min(o.Width, o.Height));
}

/// <summary>
/// Reduces every requested part to the axis-aligned boxes of its useful rotations. Only material
/// contours count (rapids and scribe/etch marks are excluded), exactly as the layout check's
/// bounds test does. Orientations are the host rotation candidates whose box area is within a
/// hair of the minimum (the minimum-area bounding rectangle plus its right-angle turn), with
/// duplicate box shapes removed. Unreadable geometry yields a type with no orientations.
/// </summary>
internal static class BoxCatalog
{
    private const double AreaTieRelative = 1e-6;
    private const double DimensionTie = 1e-7;

    public static IReadOnlyList<BoxType> Build(NestJob job)
    {
        var types = new List<BoxType>(job.Parts.Count);
        for (var i = 0; i < job.Parts.Count; i++)
            types.Add(Read(i, job.Parts[i]));
        return types;
    }

    private static BoxType Read(int index, NestJobPart part)
    {
        var geometry = JobPartGeometry.TryRead(part.Geometry);
        if (geometry == null)
            return new BoxType(index, part, Array.Empty<BoxOrientation>(), 0);

        var candidates = new List<BoxOrientation>();
        foreach (var angle in RotationCandidates.ForShape(part.Rotation, geometry.Perimeter))
        {
            var bounds = RotatedMaterialBounds(part.Geometry, angle);
            if (bounds is not { } b || !(b.Width > 0) || !(b.Height > 0))
                continue;
            candidates.Add(new BoxOrientation(angle, b.Width, b.Height, b.Left, b.Bottom));
        }

        if (candidates.Count == 0)
            return new BoxType(index, part, Array.Empty<BoxOrientation>(), geometry.MaterialArea);

        var minArea = candidates.Min(c => c.Width * c.Height);
        var kept = new List<BoxOrientation>();
        foreach (var c in candidates)
        {
            if (c.Width * c.Height > minArea * (1 + AreaTieRelative))
                continue;
            if (kept.Any(k => System.Math.Abs(k.Width - c.Width) <= DimensionTie
                    && System.Math.Abs(k.Height - c.Height) <= DimensionTie))
                continue;
            kept.Add(c);
        }
        return new BoxType(index, part, kept, geometry.MaterialArea);
    }

    /// <summary>
    /// Material bounds after rotation, as the layout check will see them. The check flattens
    /// perimeter arcs circumscribed and snaps to a 1e-4 Clipper grid, so a curved extreme reads
    /// slightly outside the true arc. Each side takes the larger of the analytic bound and the
    /// check's own outline (ClipperBridge.OffsetForValidation at zero inflation, flattened in the
    /// same local frame), and any side where the outline sticks out gets one more grid unit.
    /// Straight edges are unchanged, so rectangles still pack at exactly the part spacing.
    /// </summary>
    private static (double Left, double Bottom, double Width, double Height)? RotatedMaterialBounds(
        PartGeometrySnapshot snapshot, double angle)
    {
        var entities = ConvertProgram.ToGeometry(DrawingJobMapper.ToProgram(snapshot))
            .Where(e => SpecialLayers.IsMaterial(e.Layer))
            .ToList();
        if (entities.Count == 0)
            return null;
        foreach (var entity in entities)
            entity.Rotate(angle);
        var left = entities.Min(e => e.Left);
        var bottom = entities.Min(e => e.Bottom);
        var right = entities.Max(e => e.Right);
        var top = entities.Max(e => e.Top);
        if (!double.IsFinite(left) || !double.IsFinite(bottom) || !double.IsFinite(right) || !double.IsFinite(top))
            return null;

        var profile = new ShapeProfile(entities);
        var outline = profile.Perimeter == null ? null
            : ClipperBridge.OffsetForValidation(profile, 0, NestTolerances.ValidationOutline).LargestOuter();
        if (outline != null && outline.Vertices.Count >= 3)
        {
            left = Widen(left, outline.Vertices.Min(v => v.X), -1);
            bottom = Widen(bottom, outline.Vertices.Min(v => v.Y), -1);
            right = Widen(right, outline.Vertices.Max(v => v.X), +1);
            top = Widen(top, outline.Vertices.Max(v => v.Y), +1);
        }
        return (left, bottom, right - left, top - bottom);
    }

    /// <summary>
    /// Pushes a side out to the check's outline plus one grid unit when the outline sticks out by
    /// more than a quarter of the spacing slack. Smaller differences are grid rounding (at most half
    /// a unit per vertex) or negligible bulge: two facing sides then lose under 0.00035 in total,
    /// inside NestTolerances.SpacingSlack, and ignoring them keeps rotated rectangles exact.
    /// </summary>
    private static double Widen(double analytic, double outline, int direction)
    {
        var grid = System.Math.Pow(10, -NestTolerances.ClipperPrecision);
        var beyond = (outline - analytic) * direction;
        return beyond > NestTolerances.SpacingSlack / 4 ? outline + direction * grid : analytic;
    }
}
