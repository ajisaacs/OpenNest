using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Desktop.Rendering;

/// <summary>
/// The world geometry of what the plate view draws over and around the parts, computed by the
/// Core code the WinForms renderer calls, so each desktop app only strokes and fills it. Results
/// are in plate (world) coordinates.
/// </summary>
public static class PlateOverlays
{
    /// <summary>The sheet rectangle, placed in the plate's quadrant about the origin.</summary>
    public static Box Sheet(Plate plate)
    {
        var left = plate.Quadrant is 2 or 3 ? -plate.Size.Length : 0;
        var bottom = plate.Quadrant is 3 or 4 ? -plate.Size.Width : 0;
        return new Box(left, bottom, plate.Size.Length, plate.Size.Width);
    }

    /// <summary>The sheet less its edge spacing on each side.</summary>
    public static Box EdgeSpacing(Plate plate)
    {
        var sheet = Sheet(plate);
        var spacing = plate.EdgeSpacing;
        return new Box(sheet.X + spacing.Left, sheet.Y + spacing.Bottom,
            plate.Size.Length - spacing.Left - spacing.Right, plate.Size.Width - spacing.Bottom - spacing.Top);
    }

    /// <summary>
    /// An irregular sheet's usable area outlines (the outline eroded by the largest edge spacing),
    /// placed in the plate's quadrant; empty for a rectangular sheet or no edge spacing.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Vector>> UsableOutline(Plate plate)
    {
        var outline = plate.Outline;
        var spacing = plate.EdgeSpacing;
        var edge = System.Math.Max(System.Math.Max(spacing.Left, spacing.Right),
            System.Math.Max(spacing.Top, spacing.Bottom));
        if (outline == null || !(edge > 0))
            return new List<IReadOnlyList<Vector>>();
        return outline.Usable(edge, outline.PlacementOrigin(plate.Quadrant))
            .Select(path => (IReadOnlyList<Vector>)path.Select(p => new Vector(p.x, p.y)).ToList())
            .ToList();
    }

    /// <summary>A polygon of an irregular sheet placed in the plate's quadrant.</summary>
    public static IReadOnlyList<Vector> PlaceOutline(Plate plate, IReadOnlyList<Vector> points)
    {
        var origin = plate.Outline.PlacementOrigin(plate.Quadrant);
        return points.Select(p => new Vector(p.X + origin.X, p.Y + origin.Y)).ToList();
    }

    /// <summary>The active clamp zones, placed with the sheet.</summary>
    public static IReadOnlyList<Box> Clamps(Plate plate)
    {
        var sheet = Sheet(plate);
        return plate.ClampZones.Select(zone => zone.Bounds.Translate(sheet.X, sheet.Y)).ToList();
    }

    /// <summary>The rail's clamps taken off this sheet, placed with the sheet.</summary>
    public static IReadOnlyList<Box> RemovedClamps(Plate plate)
    {
        if (plate.ClampRail == null || plate.RemovedClamps.Count == 0)
            return new List<Box>();
        var sheet = Sheet(plate);
        return plate.ClampRail.ResolveFor(plate.Size, plate.Quadrant)
            .Where(zone => plate.RemovedClamps.Contains(zone.Id))
            .Select(zone => zone.Bounds.Translate(sheet.X, sheet.Y))
            .ToList();
    }

    /// <summary>Each cut-off definition's cut strokes, including a head-locked overcut past the sheet.</summary>
    public static IReadOnlyList<Line> CutOffStrokes(Plate plate)
    {
        var strokes = new List<Line>();
        if (plate.CutOffs == null)
            return strokes;
        foreach (var cutoff in plate.CutOffs)
        {
            var program = cutoff.Drawing?.Program;
            if (program == null || program.Codes.Count == 0)
                continue;
            strokes.AddRange(CutOff.CutStrokes(program, Vector.Zero));
        }
        return strokes;
    }

    /// <summary>The rapid moves in cutting order, cut-off segments included.</summary>
    public static IReadOnlyList<RapidEnumerator.Segment> Rapids(Plate plate) =>
        RapidEnumerator.Enumerate(plate.Parts);

    /// <summary>
    /// Where each rapid ends: the pierce points of every part, sub-program calls placed at their
    /// offsets, as the WinForms Draw Pierce Points marks them.
    /// </summary>
    public static IReadOnlyList<Vector> PiercePoints(Plate plate)
    {
        var points = new List<Vector>();
        foreach (var part in plate.Parts)
        {
            var position = part.Location;
            AddPiercePoints(points, part.Program, part.Location, ref position);
        }
        return points;
    }

    private static void AddPiercePoints(List<Vector> points, Program program, Vector basePosition, ref Vector position)
    {
        for (var i = 0; i < program.Length; ++i)
        {
            var code = program[i];
            if (code is SubProgramCall call)
            {
                if (call.Program != null)
                {
                    var holeBase = basePosition + call.Offset;
                    position = holeBase;
                    AddPiercePoints(points, call.Program, holeBase, ref position);
                }
                continue;
            }

            if (code is not Motion motion)
                continue;
            var end = program.Mode == Mode.Incremental ? motion.EndPoint + position : motion.EndPoint + basePosition;
            if (motion is RapidMove)
                points.Add(end);
            position = end;
        }
    }

    /// <summary>
    /// Cut-direction arrow positions and travel directions along every part's moves, one every
    /// <paramref name="spacing"/> world units (the WinForms view uses 60 screen pixels).
    /// </summary>
    public static IReadOnlyList<ContourSample> CutDirection(Plate plate, double spacing)
    {
        var samples = new List<ContourSample>();
        foreach (var part in plate.Parts)
            ContourSampler.ProgramMoves(part.Program, part.Location, part.Location, spacing, samples);
        return samples;
    }

    /// <summary>The box around <paramref name="parts"/>, or null when there are none.</summary>
    public static Box SelectionBounds(IEnumerable<Part> parts)
    {
        var list = parts.ToList();
        return list.Count == 0 ? null : list.GetBoundingBox();
    }

    /// <summary>
    /// The part-spacing outline of <paramref name="part"/> in its program frame: its moves (all but
    /// rapids) offset by <paramref name="spacing"/>, as View &gt; Draw Offset shows it.
    /// </summary>
    public static IReadOnlyList<Polygon> OffsetOutline(Part part, double spacing, double tolerance)
    {
        var entities = ConvertProgram.ToGeometry(part.Program);
        var profile = new ShapeProfile(entities.Where(e => e.Layer != SpecialLayers.Rapid).ToList());
        var offset = ClipperBridge.Offset(profile, spacing, tolerance);
        return offset.Outers.Concat(offset.Holes).ToList();
    }

    /// <summary>
    /// The inside-corner clearance support tab points keep: the plate's tab size, or the live
    /// plate's when <paramref name="plate"/> is a proposal with no cutting parameters of its own.
    /// </summary>
    public static double SupportClearance(Plate plate, Plate livePlate = null)
    {
        var parameters = plate.CuttingParameters ?? livePlate?.CuttingParameters;
        return SupportAnalyzer.InsideCornerClearance(parameters?.TabConfig?.Size ?? 0);
    }
}

/// <summary>
/// Support analyses for View &gt; Draw Support, kept per part until its program, pose, the bed, the
/// quadrant or the tab clearance changes. Not thread-safe; use one per view.
/// </summary>
public sealed class SupportAnalysisCache
{
    private readonly ConditionalWeakTable<Part, Entry> cache = new();

    private sealed record Entry(Program Program, Vector Location, double Rotation, SupportBed Bed, int Quadrant,
        double Clearance, PartSupport Support);

    /// <summary>The part's support on <paramref name="bed"/>; cut-offs are never analysed (null).</summary>
    public PartSupport Analyze(Part part, SupportBed bed, int quadrant, double clearance)
    {
        if (part?.BaseDrawing == null || part.BaseDrawing.IsCutOff)
            return null;
        if (cache.TryGetValue(part, out var entry) && ReferenceEquals(entry.Program, part.Program)
            && entry.Location == part.Location && entry.Rotation == part.Rotation && Equals(entry.Bed, bed)
            && entry.Quadrant == quadrant && entry.Clearance == clearance)
            return entry.Support;
        var support = SupportAnalyzer.Analyze(bed, quadrant, part, clearance);
        cache.AddOrUpdate(part, new Entry(part.Program, part.Location, part.Rotation, bed, quadrant, clearance,
            support));
        return support;
    }
}
