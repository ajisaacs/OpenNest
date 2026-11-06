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
/// <b>Outside the work area</b>: clean nesting material (leads excluded, as the part-spacing check reads it)
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

    /// <summary>
    /// The sheet a plate's parts must stay on, owned and immutable, so the desktop label's worker
    /// never reads the live plate. <see cref="Sheet"/> and <see cref="Work"/> are in the parts'
    /// signed placement frame; <see cref="Inset"/> is the outline's clearance, the largest edge spacing.
    /// </summary>
    internal sealed record SheetFrame(Box Sheet, Box Work, SheetOutline Outline, Vector Origin, double Inset)
    {
        /// <summary>The plate's frame; throws <see cref="ArgumentException"/> for an unusable size or spacing.</summary>
        internal static SheetFrame Of(Plate plate)
        {
            var size = plate.Size;
            if (!(size.Length > 0) || !(size.Width > 0) || !double.IsFinite(size.Length) || !double.IsFinite(size.Width))
                throw new ArgumentException($"plate size {size} is not a positive, finite size.");
            var edges = plate.EdgeSpacing;
            if (!new[] { edges.Left, edges.Bottom, edges.Right, edges.Top }.All(e => double.IsFinite(e) && e >= 0))
                throw new ArgumentException("plate edge spacing is not finite and non-negative on every side.");
            var sheet = plate.BoundingBox(includeParts: false);
            return new SheetFrame(sheet, plate.WorkArea(), plate.Outline, new Vector(sheet.X, sheet.Y),
                System.Math.Max(System.Math.Max(edges.Left, edges.Right), System.Math.Max(edges.Top, edges.Bottom)));
        }
    }

    /// <summary>What is wrong with one part; <see cref="Detail"/> follows its quoted name.</summary>
    internal readonly record struct Verdict(bool OffPlate, string Detail, Vector Marker);

    internal static void Add(Plate plate, int plateNumber, List<PostVerificationFinding> findings,
        CancellationToken cancellationToken)
    {
        if (plate.Parts.Count == 0)
            return;
        SheetFrame frame;
        try
        {
            frame = SheetFrame.Of(plate);
        }
        catch (ArgumentException exception)
        {
            findings.Add(new(PostVerificationKind.Incomplete, plateNumber, null, null,
                "Plate edge check: " + exception.Message));
            return;
        }
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
                // The placed program first: its leads and scribes may leave a sheet its material stays on.
                var verdict = PastSheet(part, frame.Sheet, cancellationToken)
                    ?? (PartSpacingReview.Profile(part, profiles) is { } profile
                        ? Check(profile, part.Location, frame)
                        : null);
                if (verdict is { } found)
                    findings.Add(new(found.OffPlate ? PostVerificationKind.OffPlate : PostVerificationKind.EdgeSpacing,
                        plateNumber, partNumber, null, $"'{part.BaseDrawing.Name}' {found.Detail}")
                    { Location = found.Marker });
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                findings.Add(new(PostVerificationKind.Incomplete, plateNumber, partNumber, null,
                    "Plate edge check: " + exception.Message));
            }
        }
    }

    /// <summary>
    /// The desktop label's check: clean material only (snapshots hold no leads), run on the
    /// overlap worker from owned geometry. Findings and issues keep input ids.
    /// </summary>
    internal static void Add(PlateOverlapSnapshot snapshot, List<PlateEdgeFinding> found,
        List<PlateOverlapIssue> issues, CancellationToken cancellationToken)
    {
        var frame = snapshot.Frame;
        if (frame == null)
            return;
        var profiles = new Dictionary<(OverlapSource, long), ShapeProfile>();
        var unreadable = snapshot.Issues.Where(issue => issue.PartBId == null)
            .Select(issue => issue.PartAId).ToHashSet();
        foreach (var part in snapshot.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (unreadable.Contains(part.Id))
                continue;
            try
            {
                if (PartSpacingReview.Profile(part, profiles) is { } profile
                    && Check(profile, part.Location, frame) is { } verdict)
                    found.Add(new PlateEdgeFinding(part.Id, part.Name, verdict.OffPlate,
                        $"'{part.Name}' {verdict.Detail}", verdict.Marker));
            }
            catch (Exception exception) when (IsInvalid(exception))
            {
                issues.Add(new PlateOverlapIssue(part.Id, null, exception.Message));
            }
        }
    }

    /// <summary>Clean material at <paramref name="location"/> against the sheet; null when it keeps the edge spacing.</summary>
    internal static Verdict? Check(ShapeProfile profile, Vector location, SheetFrame frame)
    {
        var bounds = profile.Perimeter.BoundingBox.Translate(location);
        if (Describe(Furthest(null, bounds, bounds.Center, frame.Sheet)) is { } past)
            return past;
        PathsD region = null;
        if (frame.Outline != null)
        {
            var material = SpacingOutline.Build(profile, location, 0)
                ?? throw new ArgumentException("Part material has no closed perimeter.");
            region = new PathsD { ClipperBridge.ToPath(material.Perimeter, positive: true) };
            if (!frame.Outline.Holds(region, 0, frame.Origin, Slack))
                return new Verdict(true, "lies outside the plate's outline or over one of its obstacles.",
                    bounds.Center);
        }
        if (Shortest(bounds, frame.Sheet, frame.Work) is { } close)
            return new Verdict(false, $"is outside the work area: {close.Gap:F3} from the plate's {close.Edge} "
                + $"edge, inside its {close.Spacing:F3} edge spacing.", close.Marker);
        if (region != null && frame.Inset > 0 && !frame.Outline.Holds(region, frame.Inset, frame.Origin, Slack))
            return new Verdict(false, $"is outside the work area: inside the {frame.Inset:F3} edge spacing of the "
                + "plate's outline or one of its obstacles.", bounds.Center);
        return null;
    }

    private readonly record struct Past(string Edge, double Distance, Vector Marker);

    private readonly record struct Close(string Edge, double Gap, double Spacing, Vector Marker);

    /// <summary>The furthest any non-rapid motion of the placed program runs past the sheet, or null.</summary>
    private static Verdict? PastSheet(Part part, Box sheet, CancellationToken cancellationToken)
    {
        Past? worst = null;
        foreach (var move in ExecutionMotionReader.Read(part.Program, part.Location, null, cancellationToken).Motions)
        {
            if (move.Rapid || move.Curve == null)
                continue;
            worst = Furthest(worst, move.Curve.ToEntity().BoundingBox, move.Curve.Midpoint, sheet);
        }
        return Describe(worst);
    }

    private static Verdict? Describe(Past? past) => past is { } p
        ? new Verdict(true, $"runs {p.Distance:F3} past the plate's {p.Edge} edge.", p.Marker)
        : null;

    private static Past? Furthest(Past? worst, Box box, Vector middle, Box sheet)
    {
        Consider("left", sheet.Left - box.Left, new Vector(sheet.Left, Clamp(middle.Y, sheet.Bottom, sheet.Top)));
        Consider("bottom", sheet.Bottom - box.Bottom, new Vector(Clamp(middle.X, sheet.Left, sheet.Right), sheet.Bottom));
        Consider("right", box.Right - sheet.Right, new Vector(sheet.Right, Clamp(middle.Y, sheet.Bottom, sheet.Top)));
        Consider("top", box.Top - sheet.Top, new Vector(Clamp(middle.X, sheet.Left, sheet.Right), sheet.Top));
        return worst;

        void Consider(string edge, double distance, Vector marker)
        {
            if (!double.IsFinite(distance))
                throw new ArgumentException("Part geometry must be finite.");
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

    private static double Clamp(double value, double low, double high) =>
        double.IsFinite(value) ? System.Math.Clamp(value, low, high) : low;

    private static bool IsInvalid(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;
}
