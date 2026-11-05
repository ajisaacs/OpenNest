using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clipper2Lib;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Diagnostics;

/// <summary>
/// Parts that leave the sheet or come closer to its edges than the plate's edge spacing.
/// <para>
/// <b>Off the plate</b>: any cutting, lead or scribe motion of the placed program (its native
/// curve bounds) past the sheet's bounding box, or, on an outlined sheet, clean material outside
/// the outline or over one of its obstacles. A pierce or cut off the sheet cuts nothing, or the
/// table.
/// </para>
/// <para>
/// <b>Edge spacing</b>: clean nesting material (leads excluded, as the part-spacing check reads it)
/// outside the plate's work area, or, on an outlined sheet, closer than the largest edge spacing to
/// the outline or an obstacle: Auto Nest's layout check judges bounds exactly this way, so a layout
/// it accepted stays clear. Shortfalls up to <see cref="SpacingOutline.SpacingSlack"/> pass. Leads
/// may run into the edge spacing; only off the sheet do they warn.
/// </para>
/// Cut-off parts are exempt: they run to the sheet edge by design.
/// </summary>
internal static class PlateEdgeReview
{
    private const double Slack = SpacingOutline.SpacingSlack;

    internal static void Add(Plate plate, int plateNumber, List<PostVerificationFinding> findings,
        CancellationToken cancellationToken)
    {
        if (plate.Parts.Count == 0)
            return;
        var size = plate.Size;
        if (!(size.Length > 0) || !(size.Width > 0) || !double.IsFinite(size.Length) || !double.IsFinite(size.Width))
        {
            findings.Add(new(PostVerificationKind.Incomplete, plateNumber, null, null,
                $"Plate edge check: plate size {size} is not a positive, finite size."));
            return;
        }
        var edges = plate.EdgeSpacing;
        if (!new[] { edges.Left, edges.Bottom, edges.Right, edges.Top }.All(e => double.IsFinite(e) && e >= 0))
        {
            findings.Add(new(PostVerificationKind.Incomplete, plateNumber, null, null,
                "Plate edge check: plate edge spacing is not finite and non-negative on every side."));
            return;
        }
        var sheet = plate.BoundingBox(includeParts: false);
        var work = plate.WorkArea();
        var outline = plate.Outline;
        var origin = new Vector(sheet.X, sheet.Y);
        var inset = System.Math.Max(System.Math.Max(edges.Left, edges.Right), System.Math.Max(edges.Top, edges.Bottom));
        var profiles = new Dictionary<(Program, long), ShapeProfile>();

        for (var index = 0; index < plate.Parts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var part = plate.Parts[index];
            var partNumber = index + 1;
            if (part?.BaseDrawing == null || part.BaseDrawing.IsCutOff)
                continue;
            try
            {
                if (PastSheet(part, sheet, cancellationToken) is { } past)
                {
                    findings.Add(new(PostVerificationKind.OffPlate, plateNumber, partNumber, null,
                        $"'{part.BaseDrawing.Name}' runs {past.Distance:F3} past the plate's {past.Edge} edge.")
                    { Location = past.Marker });
                    continue;
                }

                var profile = PartSpacingReview.Profile(part, profiles);
                if (profile == null)
                    continue;
                PathsD region = null;
                if (outline != null)
                {
                    var material = SpacingOutline.Build(profile, part.Location, 0)
                        ?? throw new ArgumentException("Part material has no closed perimeter.");
                    region = new PathsD { ClipperBridge.ToPath(material.Perimeter, positive: true) };
                    if (!outline.Holds(region, 0, origin, Slack))
                    {
                        findings.Add(new(PostVerificationKind.OffPlate, plateNumber, partNumber, null,
                            $"'{part.BaseDrawing.Name}' lies outside the plate's outline or over one of its obstacles.")
                        { Location = Center(profile, part.Location) });
                        continue;
                    }
                }

                var bounds = profile.Perimeter.BoundingBox.Translate(part.Location);
                if (Shortest(bounds, sheet, work) is { } close)
                {
                    findings.Add(new(PostVerificationKind.EdgeSpacing, plateNumber, partNumber, null,
                        $"'{part.BaseDrawing.Name}' is {close.Gap:F3} from the plate's {close.Edge} edge, closer than "
                        + $"its edge spacing ({close.Spacing:F3}).")
                    { Location = close.Marker });
                    continue;
                }
                if (region != null && inset > 0 && !outline.Holds(region, inset, origin, Slack))
                    findings.Add(new(PostVerificationKind.EdgeSpacing, plateNumber, partNumber, null,
                        $"'{part.BaseDrawing.Name}' is closer than the edge spacing ({inset:F3}) to the plate's outline "
                        + "or one of its obstacles.")
                    { Location = Center(profile, part.Location) });
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, partNumber, null,
                    "Plate edge check: " + exception.Message));
            }
        }
    }

    private readonly record struct Past(string Edge, double Distance, Vector Marker);

    private readonly record struct Close(string Edge, double Gap, double Spacing, Vector Marker);

    /// <summary>The furthest any non-rapid motion of the placed program runs past the sheet, or null.</summary>
    private static Past? PastSheet(Part part, Box sheet, CancellationToken cancellationToken)
    {
        Past? worst = null;
        foreach (var move in ExecutionMotionReader.Read(part.Program, part.Location, null, cancellationToken).Motions)
        {
            if (move.Rapid || move.Curve == null)
                continue;
            var box = move.Curve.ToEntity().BoundingBox;
            var middle = move.Curve.Midpoint;
            Consider("left", sheet.Left - box.Left, new Vector(sheet.Left, Clamp(middle.Y, sheet.Bottom, sheet.Top)));
            Consider("bottom", sheet.Bottom - box.Bottom, new Vector(Clamp(middle.X, sheet.Left, sheet.Right), sheet.Bottom));
            Consider("right", box.Right - sheet.Right, new Vector(sheet.Right, Clamp(middle.Y, sheet.Bottom, sheet.Top)));
            Consider("top", box.Top - sheet.Top, new Vector(Clamp(middle.X, sheet.Left, sheet.Right), sheet.Top));
        }
        return worst;

        void Consider(string edge, double distance, Vector marker)
        {
            if (!double.IsFinite(distance))
                throw new ArgumentException("Placed program geometry must be finite.");
            if (distance > Slack && (worst == null || distance > worst.Value.Distance))
                worst = new Past(edge, distance, marker);
        }
    }

    /// <summary>The side whose edge spacing the material falls furthest short of, or null.</summary>
    private static Close? Shortest(Box bounds, Box sheet, Box work)
    {
        Close? worst = null;
        var shortfall = 0.0;
        var center = bounds.Center;
        Consider("left", work.Left - bounds.Left, bounds.Left - sheet.Left, work.Left - sheet.Left,
            new Vector((sheet.Left + bounds.Left) / 2, center.Y));
        Consider("bottom", work.Bottom - bounds.Bottom, bounds.Bottom - sheet.Bottom, work.Bottom - sheet.Bottom,
            new Vector(center.X, (sheet.Bottom + bounds.Bottom) / 2));
        Consider("right", bounds.Right - work.Right, sheet.Right - bounds.Right, sheet.Right - work.Right,
            new Vector((sheet.Right + bounds.Right) / 2, center.Y));
        Consider("top", bounds.Top - work.Top, sheet.Top - bounds.Top, sheet.Top - work.Top,
            new Vector(center.X, (sheet.Top + bounds.Top) / 2));
        return worst;

        void Consider(string edge, double by, double gap, double spacing, Vector marker)
        {
            if (by > Slack && by > shortfall)
            {
                shortfall = by;
                worst = new Close(edge, System.Math.Max(0, gap), spacing, marker);
            }
        }
    }

    private static Vector Center(ShapeProfile profile, Vector location) =>
        profile.Perimeter.BoundingBox.Translate(location).Center;

    private static double Clamp(double value, double low, double high) =>
        double.IsFinite(value) ? System.Math.Clamp(value, low, high) : low;

    private static bool IsInvalid(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;
}
