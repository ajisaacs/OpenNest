using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest;

/// <summary>Where an expected remnant comes from.</summary>
public enum PlateRemnantSource
{
    /// <summary>Material a full-span cut-off cuts free of the parts.</summary>
    CutOff,

    /// <summary>The largest full-width or full-length edge strip outside the parts, as salvage measures it.</summary>
    Offcut,
}

/// <summary>
/// The rectangular drop a plate is expected to leave, in plate coordinates (the quadrant frame
/// of its parts and cut-offs). Derived from the current layout; never stored on the plate.
/// </summary>
public sealed class PlateRemnant
{
    private PlateRemnant(Box box, PlateRemnantSource source)
    {
        Box = box;
        Source = source;
    }

    public Box Box { get; }

    public PlateRemnantSource Source { get; }

    /// <summary>
    /// The drop <paramref name="plate"/> is expected to leave, or null when it has no parts or no
    /// rectangle has both sides at least <paramref name="minimumDimension"/>. A full-span cut-off
    /// (no limit short of either sheet edge) with no part material beyond it defines the drop: the
    /// sheet past its nominal position, less the plate's kerf for each bounding cut-off line. It
    /// wins over a larger edge offcut. Otherwise the drop is the largest edge offcut that
    /// <see cref="LargestOffcut"/> finds. Material bounds ignore cut-off parts and marks. A plate
    /// with an <see cref="Plate.Outline"/> reports none.
    /// </summary>
    public static PlateRemnant Expected(Plate plate, double minimumDimension)
    {
        // No rectangular strip is guaranteed inside an outlined sheet.
        if (plate.Outline != null)
            return null;
        // A part with no material (marks only) bounds nothing.
        var bounds = plate.Parts
            .Where(p => !p.BaseDrawing.IsCutOff && p.Program != null)
            .Select(p => ConvertProgram.ToGeometry(p.Program).Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList()
                is { Count: > 0 } material
                    ? material.GetBoundingBox().Translate(p.Location)
                    : null)
            .Where(b => b != null)
            .ToList();
        if (bounds.Count == 0)
            return null;

        var minimum = System.Math.Max(0, minimumDimension);
        var cutOff = CutOffDrop(plate, bounds, minimum);
        if (cutOff != null)
            return new PlateRemnant(cutOff, PlateRemnantSource.CutOff);

        var offcut = LargestOffcut(plate.WorkArea(), plate.PartSpacing,
            bounds.Min(b => b.Left), bounds.Min(b => b.Bottom), bounds.Max(b => b.Right), bounds.Max(b => b.Top),
            minimum);
        return offcut == null ? null : new PlateRemnant(offcut, PlateRemnantSource.Offcut);
    }

    /// <summary>
    /// The largest full-width/full-length edge strip of <paramref name="workArea"/> clear of the
    /// material envelope (<paramref name="left"/> to <paramref name="top"/>) by
    /// <paramref name="partSpacing"/>, with both sides at least <paramref name="minimum"/>; null
    /// when none qualifies. Ties keep the first of bottom, top, left, right. This is the salvage
    /// offcut whole-job costing credits.
    /// </summary>
    public static Box LargestOffcut(Box workArea, double partSpacing,
        double left, double bottom, double right, double top, double minimum)
    {
        var work = workArea;
        var gap = partSpacing;
        var candidates = new[]
        {
            new Box(work.Left, work.Bottom, work.Length, bottom - work.Bottom - gap),
            new Box(work.Left, top + gap, work.Length, work.Top - top - gap),
            new Box(work.Left, work.Bottom, left - work.Left - gap, work.Width),
            new Box(right + gap, work.Bottom, work.Right - right - gap, work.Width),
        };
        Box best = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Length >= minimum && candidate.Width >= minimum
                && (best == null || candidate.Length * candidate.Width > best.Length * best.Width))
                best = candidate;
        }
        return best;
    }

    /// <summary>Analytic world-space material bounds; surface marks never bound material.</summary>
    public static Box MaterialBounds(Part part) =>
        ConvertProgram.ToGeometry(part.Program)
            .Where(e => SpecialLayers.IsMaterial(e.Layer))
            .GetBoundingBox()
            .Translate(part.Location);

    /// <summary>
    /// The largest piece between full-span cut-off lines (or a line and the sheet edge) that no
    /// part material reaches into, with both sides at least <paramref name="minimum"/>.
    /// </summary>
    private static Box CutOffDrop(Plate plate, IReadOnlyList<Box> material, double minimum)
    {
        var sheet = plate.BoundingBox(includeParts: false);
        var kerf = plate.CuttingParameters?.Kerf is double k && double.IsFinite(k) && k > 0 ? k : 0;
        Box best = null;
        foreach (var vertical in new[] { true, false })
        {
            var low = vertical ? sheet.Left : sheet.Bottom;
            var high = vertical ? sheet.Right : sheet.Top;
            var lines = plate.CutOffs
                .Where(c => (c.Axis == CutOffAxis.Vertical) == vertical && SpansSheet(c, sheet))
                .Select(c => vertical ? c.Position.X : c.Position.Y)
                .Where(at => at > low + Tolerance.Epsilon && at < high - Tolerance.Epsilon)
                .Distinct()
                .OrderBy(at => at)
                .ToList();
            if (lines.Count == 0)
                continue;

            // Each piece runs from one boundary to the next; at least one is a cut-off line.
            var boundaries = new List<(double At, bool Cut)> { (low, false) };
            boundaries.AddRange(lines.Select(at => (at, true)));
            boundaries.Add((high, false));
            for (var i = 0; i + 1 < boundaries.Count; i++)
            {
                var (from, fromCut) = boundaries[i];
                var (to, toCut) = boundaries[i + 1];
                if (material.Any(b => Reaches(b, vertical, from, to)))
                    continue;
                var start = fromCut ? from + kerf : from;
                var end = toCut ? to - kerf : to;
                var piece = vertical
                    ? new Box(start, sheet.Bottom, end - start, sheet.Width)
                    : new Box(sheet.Left, start, sheet.Length, end - start);
                if (piece.Length >= minimum && piece.Width >= minimum
                    && (best == null || piece.Length * piece.Width > best.Length * best.Width))
                    best = piece;
            }
        }
        return best;
    }

    /// <summary>
    /// Whether the nominal line runs edge to edge with no gap on the sheet. A head-locked
    /// overcut only extends the program past the far edge, so the nominal limits decide.
    /// </summary>
    private static bool SpansSheet(CutOff cutOff, Box sheet)
    {
        var vertical = cutOff.Axis == CutOffAxis.Vertical;
        var alongMin = vertical ? sheet.Bottom : sheet.Left;
        var alongMax = vertical ? sheet.Top : sheet.Right;
        return (cutOff.StartLimit is not double start || start <= alongMin + Tolerance.Epsilon)
            && (cutOff.EndLimit is not double end || end >= alongMax - Tolerance.Epsilon)
            && !cutOff.Gaps.Any(gap => gap.End > alongMin + Tolerance.Epsilon && gap.Start < alongMax - Tolerance.Epsilon);
    }

    private static bool Reaches(Box material, bool vertical, double from, double to)
    {
        var low = vertical ? material.Left : material.Bottom;
        var high = vertical ? material.Right : material.Top;
        return high > from + Tolerance.Epsilon && low < to - Tolerance.Epsilon;
    }
}
