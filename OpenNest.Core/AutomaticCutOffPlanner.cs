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

    /// <summary>
    /// Minimum retained-tail length along X in model units. Zero permits any positive tail.
    /// The desktop default is 12 inches (304.8 mm).
    /// </summary>
    public double MinimumTailLength { get; set; }

    /// <summary>
    /// Minimum strip length in model units. Null or equal to Spacing preserves the fixed grid.
    /// Otherwise must be finite, greater than MinimumSpacing and no greater than Spacing.
    /// </summary>
    public double? MinimumStripLength { get; set; }
}

public enum AutomaticCutOffDiagnosticCode
{
    ExistingCutOff,
    LimitedCutOffConflict,
    EmptyCut,
    SegmentedCut,
    ClampBlockedCut,
    NoSafeTailSeparator,
    TailBelowMinimum,
    ShiftedCut,
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

    /// <summary>Total cut length of proposed new strip lines (excludes the tail separator).</summary>
    public double StripCutLength { get; internal set; }

    /// <summary>Length of the nominal new strip lines in the same gaps. Equal to StripCutLength on the fixed-grid path.</summary>
    public double FixedGridCutLength { get; internal set; }

    public bool HasBlockingDiagnostics => Diagnostics.Any(d => d.IsBlocking);
}

/// <summary>Pure proposals for vertical scrap cuts, measured in the plate's model units.</summary>
public static class AutomaticCutOffPlanner
{
    public const double GeometryTolerance = Tolerance.Epsilon;
    public const double MinimumSpacing = 2 * GeometryTolerance;
    public const int MaximumCandidateCount = 10000;
    public const int MaximumShiftCandidateCount = 20000;

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
        // Stored zones use the sheet's lower-left frame; cut positions are signed.
        var clamps = ClampZoneFrame.ToPlacement(plate);
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
        // A tail too short to keep stays attached to the skeleton, so the strip grid must
        // continue to the sheet end; stopping at the skipped separator would leave a final
        // strip longer than the spacing.
        var keepsTail = hasTailCandidate && length - separator >= options.MinimumTailLength;
        var usedSpan = keepsTail ? separator : length;
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
        var shifted = options.MinimumStripLength.HasValue && options.MinimumStripLength.Value < options.Spacing;
        var stripCutLength = 0.0;
        var fixedGridCutLength = 0.0;
        var measurements = new Dictionary<double, (double Length, int SegmentCount)>();
        var candidates = new HashSet<double>();

        if (shifted)
        {
            // Full-span definitions fix the strip boundaries, even when their emitted
            // programs are segmented. Partial cuts remain obstacles, not boundaries.
            var boundaries = plate.CutOffs
                .Where(c => c.Axis == CutOffAxis.Vertical && FullSpanLimits(c, bounds, settings))
                .Select(c => sign * c.Position.X)
                .Where(distance => distance > GeometryTolerance && distance < usedSpan - GeometryTolerance)
                .Distinct().OrderBy(distance => distance).Append(usedSpan);
            var gaps = new List<(double Start, double End, int Count)>();
            var start = 0.0;
            var nominalCount = 0;
            foreach (var end in boundaries)
            {
                var count = 0;
                for (var index = 1; index <= (int)candidateCount; index++)
                {
                    if (start + index * options.Spacing >= end - GeometryTolerance)
                        break;
                    count++;
                }
                gaps.Add((start, end, count));
                nominalCount += count;
                start = end;
            }
            // The origin-based lattice alone fits within 20,000 positions, but its
            // off-lattice nominal lines need reserved slots too. Budget both before
            // constructing any row so a valid, dense grid is never refused halfway.
            Require(nominalCount < MaximumShiftCandidateCount,
                "Too many nominal strip lines for the shift search.", nameof(options));
            var step = System.Math.Max(options.Spacing / 700,
                usedSpan / (MaximumShiftCandidateCount - nominalCount));
            foreach (var gap in gaps)
                AddShiftedGap(gap.Start, gap.End, gap.Count, step);
        }
        else
        {
            // Keep the legacy loop and certification unchanged when shifting is disabled.
            for (var index = 1; index <= (int)candidateCount; index++)
            {
                var distance = index * options.Spacing;
                if (distance >= usedSpan - GeometryTolerance)
                    break;
                AddCandidate(sign * distance, false);
            }
        }
        if (keepsTail)
            AddCandidate(sign * separator, true);
        else if (hasTailCandidate)
            diagnostics.Add(new AutomaticCutOffDiagnostic(
                AutomaticCutOffDiagnosticCode.TailBelowMinimum,
                "The proposed tail is shorter than the minimum tail length; the final separator was skipped "
                + "and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.",
                X: sign * separator));

        if (diagnostics.Any(d => d.IsBlocking))
        {
            // A partial proposal must not accidentally be accepted around a manual conflict.
            definitions.Clear();
            stripCutLength = 0;
            separated = false;
            separatorX = null;
        }
        // The legacy placement loop stays untouched. Its detached programs are already
        // regenerated, so reporting their actual new strip cutting needs no new search.
        if (!shifted)
            fixedGridCutLength = stripCutLength;

        return new AutomaticCutOffPlan
        {
            Definitions = definitions.AsReadOnly(),
            PreviewParts = definitions.Select(c => new Part(c.Drawing)).ToList().AsReadOnly(),
            Diagnostics = diagnostics.AsReadOnly(),
            OccupiedSpan = occupied,
            UsedSpan = separated ? System.Math.Abs(separatorX.Value) : length,
            TailLength = separated ? length - System.Math.Abs(separatorX.Value) : 0,
            TailSeparatorX = separatorX,
            StripCutLength = stripCutLength,
            FixedGridCutLength = fixedGridCutLength,
        };

        void AddShiftedGap(double start, double end, int count, double step)
        {
            var nominal = new List<double>(count);
            for (var index = 1; index <= count; index++)
            {
                var distance = start + index * options.Spacing;
                nominal.Add(distance);
                fixedGridCutLength += Measure(distance).Length;
            }
            if (count == 0)
                return;
            var positions = AutomaticCutOffStripSearch.FindPositions(start, end, count,
                options.Spacing, options.MinimumStripLength.Value, step, Allowed, Measure) ?? nominal.ToArray();
            for (var index = 0; index < positions.Length; index++)
            {
                var distance = positions[index];
                AddCandidate(sign * distance, false);
                if (!Near(distance, nominal[index]))
                {
                    var difference = Measure(nominal[index]).Length - Measure(distance).Length;
                    var comparison = difference > 0 ? $"{difference:G8} model units less" :
                        difference < 0 ? $"{-difference:G8} model units more" : "the same length";
                    diagnostics.Add(new AutomaticCutOffDiagnostic(AutomaticCutOffDiagnosticCode.ShiftedCut,
                        $"Moved from {nominal[index]:G8} to {distance:G8} model units; "
                        + $"this line cuts {comparison}.", X: sign * distance));
                }
            }
        }

        bool Allowed(double distance)
        {
            RegisterCandidate(distance);
            // Use the certification path's exact Near predicate, including its ULP
            // allowance, so a selected line cannot rediscover a limited-cut conflict.
            var x = sign * distance;
            return !plate.CutOffs.Any(c => c.Axis == CutOffAxis.Vertical && Near(c.Position.X, x)) &&
                !clamps.Any(zone => CrossesClamp(zone, x));
        }

        void RegisterCandidate(double distance)
        {
            if (candidates.Add(distance))
                Require(candidates.Count <= MaximumShiftCandidateCount,
                    $"Shift search would generate more than {MaximumShiftCandidateCount} distinct positions. "
                    + "Increase spacing or the minimum strip length, or use the fixed grid.", nameof(options));
        }

        (double Length, int SegmentCount) Measure(double distance)
        {
            RegisterCandidate(distance);
            if (!measurements.TryGetValue(distance, out var measured))
            {
                measured = new CutOff(new Vector(sign * distance, 0), CutOffAxis.Vertical)
                    .MeasureSegments(plate, settings, cache);
                measurements.Add(distance, measured);
            }
            return measured;
        }

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

            // A full-width cut crosses every active front-edge clamp at its X. Test the
            // proposed X and every equivalent existing definition, because a tolerance-close
            // existing line is regenerated and certified at its own X. Keep the whole
            // footprint when it hangs off the sheet. A same-line manual limit conflict above
            // still blocks the whole plan, clamp or not.
            var blocked = clamps.FirstOrDefault(zone =>
                matches.Select(c => c.Position.X).Prepend(x).Any(position => CrossesClamp(zone, position)));
            if (blocked != null)
            {
                diagnostics.Add(new AutomaticCutOffDiagnostic(
                    AutomaticCutOffDiagnosticCode.ClampBlockedCut,
                    $"The proposed cut crosses clamp '{blocked.Id}' (including bottom-edge clearance); it was skipped.",
                    X: x));
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
                    "The line has no usable cut segments after part clearance and minimum-length filtering.",
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
                    "Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.",
                    X: x));

            if (usable && existing == null)
            {
                definitions.Add(candidate);
                if (!isSeparator)
                {
                    if (shifted)
                        stripCutLength += Measure(System.Math.Abs(x)).Length;
                    else
                        for (var index = 0; index < program.Codes.Count; index += 2)
                            stripCutLength += System.Math.Abs(
                                ((LinearMove)program.Codes[index + 1]).EndPoint.Y -
                                ((RapidMove)program.Codes[index]).EndPoint.Y);
                }
            }
        }

        // Share the exact placement-frame predicate with candidate exclusion and final
        // certification. Infeasible gaps still use nominal lines through AddCandidate.
        bool CrossesClamp(ClampZone zone, double x) =>
            x >= zone.Bounds.Left - plate.EdgeSpacing.Bottom && x <= zone.Bounds.Right + plate.EdgeSpacing.Bottom;

        void WarnNoSeparator(double x) => diagnostics.Add(new AutomaticCutOffDiagnostic(
            AutomaticCutOffDiagnosticCode.NoSafeTailSeparator,
            "No safe full-width tail separator survives the current settings. Review the tail manually.",
            X: x));
    }

    private static void ValidateInputs(Plate plate, AutomaticCutOffOptions options, CutOffSettings settings)
    {
        Require(double.IsFinite(options.Spacing) && options.Spacing > MinimumSpacing,
            $"Spacing must be finite and greater than {MinimumSpacing} model units.", nameof(options));
        Require(!options.MinimumStripLength.HasValue ||
            (double.IsFinite(options.MinimumStripLength.Value) && options.MinimumStripLength.Value > MinimumSpacing &&
                options.MinimumStripLength.Value <= options.Spacing),
            $"Minimum strip length must be finite, greater than {MinimumSpacing} and no greater than spacing.", nameof(options));
        Require(Nonnegative(options.MinimumTailLength),
            "Minimum tail length must be finite and nonnegative.", nameof(options));
        Require(double.IsFinite(plate.Size.Length) && plate.Size.Length > 0 &&
            double.IsFinite(plate.Size.Width) && plate.Size.Width > 0,
            "Sheet length and width must be positive and finite.", nameof(plate));
        Require(plate.Quadrant is >= 1 and <= 4, "Quadrant must be 1 through 4.", nameof(plate));
        Require(Nonnegative(plate.PartSpacing), "Part spacing must be finite and nonnegative.", nameof(plate));
        Require(plate.ClampZones != null && (plate.ClampZones.Count == 0 ||
            Nonnegative(plate.EdgeSpacing.Bottom)),
            "Clamp zones and bottom-edge clearance must be finite and valid.", nameof(plate));
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
