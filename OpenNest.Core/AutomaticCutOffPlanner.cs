using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest;

public sealed class AutomaticCutOffOptions
{
    /// <summary>
    /// Nominal distance between vertical cut lines in model units. Must be finite and
    /// greater than AutomaticCutOffPlanner.MinimumSpacing; Create also bounds candidate count.
    /// </summary>
    public double Spacing { get; set; }
}

public enum AutomaticCutOffDiagnosticCode
{
    ExistingCutOff,
    LimitedCutOffConflict,
    EmptyCut,
    SegmentedCut,
    NoSafeTailSeparator,
}

public sealed record AutomaticCutOffDiagnostic(
    AutomaticCutOffDiagnosticCode Code, string Message, bool IsBlocking = false, double? X = null);

/// <summary>
/// Detached proposal. Collections are read-only; contained definitions/preview parts belong
/// to the caller. Never accept a blocked plan or a preview made for an older layout/settings.
/// </summary>
public sealed class AutomaticCutOffPlan
{
    /// <summary>Only new, usable definitions; existing equivalent definitions are not returned.</summary>
    public IReadOnlyList<CutOff> Definitions { get; internal set; } = Array.Empty<CutOff>();

    /// <summary>Detached display parts, in the same order as Definitions. Not for acceptance.</summary>
    public IReadOnlyList<Part> PreviewParts { get; internal set; } = Array.Empty<Part>();

    public IReadOnlyList<AutomaticCutOffDiagnostic> Diagnostics { get; internal set; } =
        Array.Empty<AutomaticCutOffDiagnostic>();

    /// <summary>Furthest real-part X distance from the origin, excluding cut-off parts.</summary>
    public double OccupiedSpan { get; internal set; }

    /// <summary>
    /// Distance to the verified separator, or full sheet length when no separated tail can
    /// be claimed. Zero on empty sheets. This is not a disconnected-scrap-size guarantee.
    /// </summary>
    public double UsedSpan { get; internal set; }

    /// <summary>Length beyond a verified separator, otherwise zero (also on empty sheets).</summary>
    public double TailLength { get; internal set; }

    /// <summary>Signed X coordinate of a verified new or equivalent existing separator.</summary>
    public double? TailSeparatorX { get; internal set; }

    /// <summary>True only for a verified full-width separator, never just a nominal boundary.</summary>
    public bool HasSeparatedTail => TailSeparatorX.HasValue;

    public bool HasBlockingDiagnostics => Diagnostics.Any(d => d.IsBlocking);
}

/// <summary>Pure proposals for vertical scrap cuts, measured in the plate's model units.</summary>
public static class AutomaticCutOffPlanner
{
    public const double GeometryTolerance = Tolerance.Epsilon;
    public const double MinimumSpacing = 2 * GeometryTolerance;
    public const int MaximumCandidateCount = 10000;

    /// <summary>
    /// Plans without changing the plate, its parts, or existing cut-off definitions/programs.
    /// Invalid inputs throw ArgumentException. Blocking diagnostics return no definitions.
    /// Accept by adding Definitions to Plate.CutOffs and calling RegenerateCutOffs with the
    /// same settings, only while the layout is unchanged. Do not add PreviewParts to the plate.
    /// Nominal spacing is not a guarantee of fully disconnected, hopper-sized scrap.
    /// </summary>
    public static AutomaticCutOffPlan Create(Plate plate, AutomaticCutOffOptions options,
        CutOffSettings settings)
    {
        ArgumentNullException.ThrowIfNull(plate);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateInputs(plate, options, settings);
        var bounds = plate.BoundingBox(false);
        Require(ValidBox(bounds) && double.IsFinite(bounds.Top + settings.Overtravel),
            "Physical sheet bounds and overtravel must be finite.", nameof(plate));

        var sign = plate.Quadrant is 2 or 3 ? -1 : 1;
        var occupied = 0.0;
        var hasParts = false;
        foreach (var part in plate.Parts)
        {
            Require(part?.BaseDrawing != null, "Every part must have a drawing.", nameof(plate));
            if (part.BaseDrawing.IsCutOff)
                continue;

            occupied = System.Math.Max(occupied, ValidatePart(part, bounds, sign));
            hasParts = true;
        }
        if (!hasParts)
            return new AutomaticCutOffPlan();

        // Use distances from the coordinate origin, not the sheet's lower-left corner.
        var length = plate.Size.Length;
        occupied = System.Math.Min(occupied, length);
        var separator = occupied + System.Math.Max(plate.PartSpacing, settings.PartClearance)
            + GeometryTolerance;
        var hasTailCandidate = double.IsFinite(separator) && separator < length - GeometryTolerance;
        var usedSpan = hasTailCandidate ? separator : length;
        var candidateCount = System.Math.Ceiling(usedSpan / options.Spacing);
        Require(double.IsFinite(candidateCount) && candidateCount <= MaximumCandidateCount,
            $"Spacing would generate more than {MaximumCandidateCount} cut-off candidates.", nameof(options));

        // Validate and bound the candidate count before preparing geometry or allocating lines.
        ValidateExisting(plate, bounds, settings);
        var cache = Plate.BuildPerimeterCache(plate);
        var definitions = new List<CutOff>();
        var diagnostics = new List<AutomaticCutOffDiagnostic>();
        var separated = false;
        var separatorX = (double?)null;

        // Multiplication by an integer avoids drift from repeated floating-point addition.
        for (var index = 1; index <= (int)candidateCount; index++)
        {
            var distance = index * options.Spacing;
            if (distance >= usedSpan - GeometryTolerance)
                break;
            AddCandidate(sign * distance, false);
        }
        if (hasTailCandidate)
            AddCandidate(sign * separator, true);

        if (diagnostics.Any(d => d.IsBlocking))
        {
            // A partial proposal must not accidentally be accepted around a manual conflict.
            definitions.Clear();
            separated = false;
            separatorX = null;
        }

        return new AutomaticCutOffPlan
        {
            Definitions = definitions.AsReadOnly(),
            PreviewParts = definitions.Select(c => new Part(c.Drawing)).ToList().AsReadOnly(),
            Diagnostics = diagnostics.AsReadOnly(),
            OccupiedSpan = occupied,
            UsedSpan = separated ? System.Math.Abs(separatorX.Value) : length,
            TailLength = separated ? length - System.Math.Abs(separatorX.Value) : 0,
            TailSeparatorX = separatorX,
        };

        void AddCandidate(double x, bool isSeparator)
        {
            var matches = plate.CutOffs.Where(c => c.Axis == CutOffAxis.Vertical &&
                Near(c.Position.X, x)).ToList();
            if (matches.Any(c => !FullSpanLimits(c, bounds, settings)))
            {
                diagnostics.Add(new AutomaticCutOffDiagnostic(
                    AutomaticCutOffDiagnosticCode.LimitedCutOffConflict,
                    "A same-line manual cut has different limits. Manual review is required; no cuts may be applied.",
                    true, x));
                if (isSeparator)
                    WarnNoSeparator(x);
                return;
            }

            // Even a duplicate must be regenerated detached with CURRENT settings. Its live
            // drawing can be stale, and a tolerance-close line can intersect a part at the tail.
            var existing = matches.FirstOrDefault();
            var candidate = existing == null
                ? new CutOff(new Vector(x, 0), CutOffAxis.Vertical)
                : new CutOff(existing.Position, existing.Axis)
                { StartLimit = existing.StartLimit, EndLimit = existing.EndLimit };
            candidate.Regenerate(plate, settings, cache);
            var program = candidate.Drawing.Program;
            var usable = HasUsableSegments(program);
            if (existing != null)
                diagnostics.Add(new AutomaticCutOffDiagnostic(
                    AutomaticCutOffDiagnosticCode.ExistingCutOff,
                    "An equivalent full-span cut-off already exists; no duplicate was added.", X: x));
            if (!usable)
                diagnostics.Add(new AutomaticCutOffDiagnostic(
                    AutomaticCutOffDiagnosticCode.EmptyCut,
                    "The line has no usable cut segments after part clearance and minimum-length filtering; it is not a partition.",
                    X: x));

            if (isSeparator)
            {
                if (!usable || !IsFullSpanProgram(program, bounds))
                {
                    WarnNoSeparator(x);
                    return;
                }
                separated = true;
                separatorX = candidate.Position.X;
            }
            else if (usable && !IsFullSpanProgram(program, bounds))
                diagnostics.Add(new AutomaticCutOffDiagnostic(
                    AutomaticCutOffDiagnosticCode.SegmentedCut,
                    "Part clearance or segment filtering interrupts this line; nominal spacing does not guarantee disconnected scrap.",
                    X: x));

            if (usable && existing == null)
                definitions.Add(candidate);
        }

        void WarnNoSeparator(double x) => diagnostics.Add(new AutomaticCutOffDiagnostic(
            AutomaticCutOffDiagnosticCode.NoSafeTailSeparator,
            "No safe full-width tail separator survives the current settings. No separated tail is claimed; review manually.",
            X: x));
    }

    private static void ValidateInputs(Plate plate, AutomaticCutOffOptions options, CutOffSettings settings)
    {
        Require(double.IsFinite(options.Spacing) && options.Spacing > MinimumSpacing,
            $"Spacing must be finite and greater than {MinimumSpacing} model units.", nameof(options));
        Require(double.IsFinite(plate.Size.Length) && plate.Size.Length > 0 &&
            double.IsFinite(plate.Size.Width) && plate.Size.Width > 0,
            "Sheet length and width must be positive and finite.", nameof(plate));
        Require(plate.Quadrant is >= 1 and <= 4, "Quadrant must be 1 through 4.", nameof(plate));
        Require(Nonnegative(plate.PartSpacing), "Part spacing must be finite and nonnegative.", nameof(plate));
        Require(Nonnegative(settings.PartClearance) && Nonnegative(settings.MinSegmentLength) &&
            Nonnegative(settings.Overtravel), "Cut-off settings must be finite and nonnegative.", nameof(settings));
        Require(Enum.IsDefined(settings.CutDirection), "Unknown cut direction.", nameof(settings));
        Require(plate.Parts != null && plate.CutOffs != null,
            "Plate parts and cut-off collections are required.", nameof(plate));
    }

    private static double ValidatePart(Part part, Box sheet, int sign)
    {
        Require(Finite(part.Location) && double.IsFinite(part.Rotation),
            "Part pose must be finite.", "plate");
        ValidateProgram(part.Program, new HashSet<Program>());
        var box = part.Program.BoundingBox();
        box.Offset(part.Location);
        Require(ValidBox(box) && box.Length > 0 && box.Width > 0,
            "Real parts must have finite, nonempty geometry.", "plate");
        var cached = part.BoundingBox;
        Require(ValidBox(cached) && Near(box.Left, cached.Left) && Near(box.Right, cached.Right) &&
            Near(box.Bottom, cached.Bottom) && Near(box.Top, cached.Top),
            "Part geometry has stale bounds; update it before planning.", "plate");
        Require(Inside(box, sheet), "Part geometry extends outside the physical sheet.", "plate");

        // Checking raw coordinates above prevents NaNs being hidden by min/max comparisons.
        // Checking converted entities catches overflowing incremental moves and curve bounds.
        var hasMaterial = false;
        var occupied = sign > 0 ? box.Right : -box.Left;
        var roundoff = CutOff.GetBoundsRoundoff(part);
        foreach (var entity in ConvertProgram.ToGeometry(part.Program))
        {
            var entityBox = entity.BoundingBox;
            Require(ValidBox(entityBox), "Part contains invalid converted geometry.", "plate");
            if (!SpecialLayers.IsMaterial(entity.Layer))
                continue;
            entityBox = entityBox.Translate(part.Location);
            // Permit only bounded floating-point roundoff, with the same conservative
            // padding in CutOff's broad phase and fallback. Geometry-scale protrusions
            // (including refitted arc centers) are still rejected, even below epsilon.
            Require(entityBox.Left >= cached.Left - roundoff && entityBox.Right <= cached.Right + roundoff &&
                entityBox.Bottom >= cached.Bottom - roundoff && entityBox.Top <= cached.Top + roundoff,
                "Converted material extends outside cached part bounds; repair it before planning.", "plate");
            Require(Inside(entityBox, sheet), "Part geometry extends outside the physical sheet.", "plate");
            occupied = System.Math.Max(occupied, sign > 0 ? entityBox.Right : -entityBox.Left);
            hasMaterial |= entityBox.Length > 0 || entityBox.Width > 0;
        }
        Require(hasMaterial, "Real parts must contain material geometry.", "plate");
        return occupied;
    }

    private static void ValidateProgram(Program program, HashSet<Program> path)
    {
        Require(program?.Codes != null && path.Count < 64 && path.Add(program),
            "Part program is missing, recursive, or nested too deeply.", "plate");
        foreach (var code in program.Codes)
        {
            Require(code != null, "Part program contains a missing instruction.", "plate");
            if (code is Motion motion)
                Require(Finite(motion.EndPoint), "Part motion coordinates must be finite.", "plate");
            if (code is ArcMove arc)
                Require(Finite(arc.CenterPoint) && Enum.IsDefined(arc.Rotation),
                    "Part arc geometry must be finite with a valid direction.", "plate");
            if (code is SubProgramCall call)
            {
                Require(Finite(call.Offset) && double.IsFinite(call.Rotation),
                    "Part sub-program pose must be finite.", "plate");
                ValidateProgram(call.Program, path);
            }
        }
        path.Remove(program);
    }

    private static void ValidateExisting(Plate plate, Box bounds, CutOffSettings settings)
    {
        foreach (var cut in plate.CutOffs)
        {
            Require(cut != null && Enum.IsDefined(cut.Axis) && Finite(cut.Position) &&
                (!cut.StartLimit.HasValue || double.IsFinite(cut.StartLimit.Value)) &&
                (!cut.EndLimit.HasValue || double.IsFinite(cut.EndLimit.Value)),
                "Existing cut-off definitions must be finite with a valid axis.", nameof(plate));
            var start = cut.StartLimit ?? (cut.Axis == CutOffAxis.Vertical ? bounds.Bottom : bounds.Left);
            var end = cut.EndLimit ?? ((cut.Axis == CutOffAxis.Vertical ? bounds.Top : bounds.Right)
                + settings.Overtravel);
            Require(double.IsFinite(end) && start < end,
                "Existing cut-off limits must be finite and ordered.", nameof(plate));
        }
    }

    private static bool FullSpanLimits(CutOff cut, Box bounds, CutOffSettings settings) =>
        Near(cut.StartLimit ?? bounds.Bottom, bounds.Bottom) &&
        Near(cut.EndLimit ?? (bounds.Top + settings.Overtravel), bounds.Top + settings.Overtravel);

    private static bool HasUsableSegments(Program program)
    {
        if (program.Codes.Count == 0 || program.Codes.Count % 2 != 0)
            return false;
        for (var i = 0; i < program.Codes.Count; i += 2)
        {
            if (program.Codes[i] is not RapidMove from || program.Codes[i + 1] is not LinearMove to ||
                !Finite(from.EndPoint) || !Finite(to.EndPoint) ||
                !Near(from.EndPoint.X, to.EndPoint.X) ||
                System.Math.Abs(from.EndPoint.Y - to.EndPoint.Y) <= GeometryTolerance)
                return false;
        }
        return true;
    }

    private static bool IsFullSpanProgram(Program program, Box bounds)
    {
        // No gaps, bridges, or filtered middle segments can separate the tail.
        if (program.Codes.Count != 2 || program.Codes[0] is not RapidMove from ||
            program.Codes[1] is not LinearMove to)
            return false;
        return System.Math.Min(from.EndPoint.Y, to.EndPoint.Y) <= bounds.Bottom + GeometryTolerance &&
            System.Math.Max(from.EndPoint.Y, to.EndPoint.Y) >= bounds.Top - GeometryTolerance;
    }

    private static bool ValidBox(Box box) => box != null && Finite(box.Location) &&
        Nonnegative(box.Length) && Nonnegative(box.Width) &&
        double.IsFinite(box.Right) && double.IsFinite(box.Top);

    private static bool Inside(Box box, Box sheet) =>
        box.Left >= sheet.Left - GeometryTolerance && box.Right <= sheet.Right + GeometryTolerance &&
        box.Bottom >= sheet.Bottom - GeometryTolerance && box.Top <= sheet.Top + GeometryTolerance;

    private static bool Finite(Vector point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
    private static bool Near(double a, double b)
    {
        // An exact tolerance-sized offset can round just above epsilon after subtraction.
        // Allow two representational steps, not a geometry-scale relative tolerance.
        var magnitude = System.Math.Max(System.Math.Abs(a), System.Math.Abs(b));
        var step = System.Math.BitIncrement(magnitude) - magnitude;
        return System.Math.Abs(a - b) <= GeometryTolerance + (double.IsFinite(step) ? 2 * step : 0);
    }

    private static void Require(bool valid, string message, string parameter)
    {
        if (!valid)
            throw new ArgumentException(message, parameter);
    }
}
