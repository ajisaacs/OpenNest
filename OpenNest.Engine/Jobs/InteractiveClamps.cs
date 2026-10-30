using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// Interactive-path clamp keep-outs (Slice 4): the inflated rectangles and static obstacle
/// parts that make the single-plate fill, push and pack surfaces respect
/// <see cref="Plate.ClampZones"/> (mapped into the placement frame) like existing parts. Zones are never material: the fake
/// obstacle parts exist only inside a single call - they are never added to
/// <see cref="Plate.Parts"/>, plates, jobs or files.
/// </summary>
internal static class InteractiveClamps
{
    public static bool HasZones(Plate plate) => plate.ClampZones.Count > 0;

    /// <summary>
    /// Raw zone rectangles in the parts' placement frame (plate zones are stored in sheet
    /// coordinates, see <see cref="ClampZoneFrame"/>), detached copies, capture order.
    /// </summary>
    public static IReadOnlyList<Box> ZoneRectangles(Plate plate)
    {
        if (plate.ClampZones.Count == 0)
            return Array.Empty<Box>();
        var zones = ClampZoneFrame.ToPlacement(plate);
        var boxes = new Box[zones.Count];
        for (var i = 0; i < boxes.Length; i++)
            boxes[i] = Copy(zones[i].Bounds);
        return boxes;
    }

    /// <summary>
    /// Placement keep-outs inflated by the full <see cref="Plate.ClampClearance"/> - the same
    /// convention the Rectangles lane blocks and engines honor, so fill and pack produce
    /// placements with at least that clearance from the raw zone.
    /// </summary>
    public static IReadOnlyList<Box> ClearanceRectangles(Plate plate) =>
        InflateZones(plate, plate.ClampClearance);

    /// <summary>
    /// The validator's exact C1 rectangle (NestLayoutCheck: clamp clearance less the shared
    /// spacing slack). Used by the service safety net so flush engine-clear placements are
    /// never filtered by rounding.
    /// </summary>
    public static IReadOnlyList<Box> ValidationKeepOuts(Plate plate) =>
        InflateZones(plate, System.Math.Max(0, plate.ClampClearance - NestTolerances.SpacingSlack));

    private static IReadOnlyList<Box> InflateZones(Plate plate, double clearance)
    {
        if (plate.ClampZones.Count == 0)
            return Array.Empty<Box>();
        var zones = ClampZoneFrame.ToPlacement(plate);
        var boxes = new Box[zones.Count];
        for (var i = 0; i < boxes.Length; i++)
            boxes[i] = zones[i].Bounds.Offset(clearance);
        return boxes;
    }

    /// <summary>
    /// Static obstacle parts for the directional push kernels: each zone grown square by
    /// <see cref="Plate.ClampClearance"/>, the layout check's keep-out, which the kernels then
    /// hold at zero spacing (never the part spacing). A round spacing offset would let a part
    /// tuck into the keep-out's corners.
    /// </summary>
    public static List<Part> ObstacleParts(Plate plate) =>
        ObstaclePartsFor(ZoneRectangles(plate), plate.ClampClearance);

    /// <summary>Expander walls use the clamp clearance as a fixed floor,
    /// independent of the separation target or its final achieved spacing.</summary>
    public static List<Part> WallParts(Plate plate) =>
        ObstaclePartsFor(ZoneRectangles(plate), plate.ClampClearance);

    private static List<Part> ObstaclePartsFor(IReadOnlyList<Box> rectangles, double inflate)
    {
        var parts = new List<Part>(rectangles.Count);
        foreach (var box in rectangles)
        {
            var inflated = box.Offset(inflate);
            var drawing = RectangleDrawing(inflated.Length, inflated.Width);
            parts.Add(new Part(drawing, new Vector(inflated.Left, inflated.Bottom)));
        }
        return parts;
    }

    /// <summary>
    /// Exact material overlap oracle for the service safety net: part outline with its
    /// holes subtracted against the keep-out rectangle, matching the C1 checker's verdict
    /// (positive-area overlap only - flush contact is clear).
    /// </summary>
    public static bool MaterialInKeepOut(Part part, Box keepOut)
    {
        var partProfile = new ShapeProfile(PartGeometry.GetPartEntities(part));
        if (partProfile.Perimeter == null)
            return true;
        // A keep-out below the polygon tolerance collapses in the tolerant test and would
        // read as clear (PM b0062c73); judge it tolerance-free instead.
        if (KeepOutOverlap.NeedsToleranceFreeTest(keepOut))
            return KeepOutOverlap.ProfileEnters(partProfile, new Vector(0, 0), keepOut);
        var zoneProfile = new ShapeProfile(RectangleEntities(keepOut));
        if (zoneProfile.Perimeter == null)
            return true;
        return Collision.HasOverlap(
            partProfile.Perimeter.ToPolygonWithTolerance(0.001),
            zoneProfile.Perimeter.ToPolygonWithTolerance(0.001),
            HolePolygons(partProfile),
            HolePolygons(zoneProfile));
    }

    public static bool MaterialsTooClose(Part first, Part second, double spacing)
    {
        var a = new ShapeProfile(PartGeometry.GetPartEntities(first));
        var b = new ShapeProfile(PartGeometry.GetPartEntities(second));
        if (a.Perimeter == null || b.Perimeter == null)
            return true;
        var outerA = a.Perimeter.ToPolygonWithTolerance(0.001);
        var outerB = b.Perimeter.ToPolygonWithTolerance(0.001);
        var holesA = HolePolygons(a);
        var holesB = HolePolygons(b);
        if (Collision.HasOverlap(outerA, outerB, holesA, holesB))
            return true;
        if (spacing <= NestTolerances.SpacingSlack)
            return false;
        // The profiles are already in world space. Facets at the polygons' tolerance understate
        // an arc gap by more than the slack, which would reject a pair placed at the spacing.
        return NativeContours.Of(a, new Vector(0, 0))
            .TooClose(NativeContours.Of(b, new Vector(0, 0)), spacing);
    }

    private static List<Polygon> HolePolygons(ShapeProfile profile)
    {
        if (profile.Cutouts.Count == 0)
            return null;
        var holes = new List<Polygon>(profile.Cutouts.Count);
        foreach (var cutout in profile.Cutouts)
            holes.Add(cutout.ToPolygonWithTolerance(0.001));
        return holes;
    }

    private static List<Entity> RectangleEntities(Box box)
    {
        var program = new Program();
        program.MoveTo(box.Left, box.Bottom);
        program.LineTo(box.Right, box.Bottom);
        program.LineTo(box.Right, box.Top);
        program.LineTo(box.Left, box.Top);
        program.LineTo(box.Left, box.Bottom);
        return ConvertProgram.ToGeometry(program);
    }

    private static Box Copy(Box box) => new(box.X, box.Y, box.Length, box.Width);

    // Rectangular obstacle drawings are immutable and shared across calls; rail clamps
    // all share one size, so this stays tiny. Key equality implies identical geometry.
    private static readonly ConcurrentDictionary<(double Length, double Width), Drawing> Drawings = new();

    private static Drawing RectangleDrawing(double length, double width) =>
        Drawings.GetOrAdd((length, width), size =>
        {
            var program = new Program();
            program.MoveTo(0, 0);
            program.LineTo(size.Length, 0);
            program.LineTo(size.Length, size.Width);
            program.LineTo(0, size.Width);
            program.LineTo(0, 0);
            return new Drawing("clamp-zone-obstacle", program);
        });
}
