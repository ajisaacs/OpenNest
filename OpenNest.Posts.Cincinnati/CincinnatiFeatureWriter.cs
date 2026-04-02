using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Posts.Cincinnati;

/// <summary>
/// Data class carrying all context needed to emit one Cincinnati-format G-code feature block.
/// </summary>
public sealed class FeatureContext
{
    public List<ICode> Codes { get; set; } = new();
    public int FeatureNumber { get; set; }
    public string PartName { get; set; } = "";
    public bool IsFirstFeatureOfPart { get; set; }
    public bool IsLastFeatureOnSheet { get; set; }
    public bool IsSafetyHeadraise { get; set; }
    public bool IsExteriorFeature { get; set; }
    public bool IsEtch { get; set; }
    public string LibraryFile { get; set; } = "";
    public double CutDistance { get; set; }
    public double SheetDiagonal { get; set; }

    /// <summary>
    /// Part location on the plate. Added to all output X/Y coordinates
    /// so part-relative programs become plate-absolute under G90.
    /// </summary>
    public Vector PartLocation { get; set; } = Vector.Zero;

    /// <summary>
    /// Maps (drawingId, variableName) to assigned machine variable numbers.
    /// Used to emit #number references instead of literal values for user variables.
    /// </summary>
    public Dictionary<(int drawingId, string varName), int> UserVariableMapping { get; set; }

    /// <summary>
    /// The drawing ID for the current part, used to look up user variable mappings.
    /// </summary>
    public int DrawingId { get; set; }

    /// <summary>
    /// True if this feature is a cut-off line. Used to substitute plate-edge
    /// coordinates with sheet width/length variables.
    /// </summary>
    public bool IsCutOff { get; set; }

    /// <summary>Plate width (Y extent for vertical cutoffs).</summary>
    public double PlateWidth { get; set; }

    /// <summary>Plate length (X extent for horizontal cutoffs).</summary>
    public double PlateLength { get; set; }
}

/// <summary>
/// Emits one Cincinnati-format G-code feature block (one contour) to a TextWriter.
/// Handles rapid positioning, pierce, kerf compensation, anti-dive, feedrate modal
/// suppression, arc I/J conversion (absolute to incremental), and M47 head raise.
/// </summary>
public sealed class CincinnatiFeatureWriter
{
    private readonly CincinnatiPostConfig _config;
    private readonly CoordinateFormatter _fmt;
    private readonly SpeedClassifier _speedClassifier;

    public CincinnatiFeatureWriter(CincinnatiPostConfig config)
    {
        _config = config;
        _fmt = new CoordinateFormatter(config.PostedAccuracy);
        _speedClassifier = new SpeedClassifier();
    }

    /// <summary>
    /// Writes a complete feature block for the given context.
    /// </summary>
    public void Write(TextWriter writer, FeatureContext ctx)
    {
        var currentPos = Vector.Zero;
        var lastFeedVar = "";
        var kerfEmitted = false;
        var offset = ctx.PartLocation;

        // Find the pierce point from the first rapid move
        var piercePoint = FindPiercePoint(ctx.Codes);

        // 1. Rapid to pierce point (with line number if configured)
        WriteRapidToPierce(writer, ctx, piercePoint, offset);

        // 2. Part name comment on first feature of each part
        if (ctx.IsFirstFeatureOfPart && !string.IsNullOrEmpty(ctx.PartName))
            writer.WriteLine(CoordinateFormatter.Comment($"PART: {ctx.PartName}"));

        // 3. G89 process params
        if (_config.ProcessParameterMode == G89Mode.LibraryFile)
        {
            var lib = ctx.LibraryFile;
            if (!string.IsNullOrEmpty(lib))
            {
                var speedClass = _speedClassifier.Classify(ctx.CutDistance, ctx.SheetDiagonal);
                var cutDist = _speedClassifier.FormatCutDist(ctx.CutDistance, ctx.SheetDiagonal);
                writer.WriteLine($"G89 P{lib} ({speedClass} {cutDist})");
            }
            else
            {
                writer.WriteLine("(WARNING: No library found)");
            }
        }

        // 4. Pierce/beam on — G85 for etch (no pierce), G84 for cut
        writer.WriteLine(ctx.IsEtch ? "G85" : "G84");

        // 5. Anti-dive off
        if (_config.UseAntiDive)
            writer.WriteLine("M130 (ANTI DIVE OFF)");

        // Update current position to pierce point
        currentPos = piercePoint;

        // 6. Lead-in + contour moves with kerf comp and feedrate variables
        foreach (var code in ctx.Codes)
        {
            if (code is RapidMove)
                continue; // skip rapids in contour (already handled above)

            if (code is LinearMove linear)
            {
                var sb = new StringBuilder();

                // Kerf compensation on first cutting move (skip for etch)
                if (!ctx.IsEtch && !kerfEmitted && _config.KerfCompensation == KerfMode.ControllerSide)
                {
                    sb.Append(_config.DefaultKerfSide == KerfSide.Left ? "G41 " : "G42 ");
                    kerfEmitted = true;
                }

                var xCoord = FormatCoordWithVars(linear.EndPoint.X + offset.X, "X", linear.VariableRefs, ctx);
                var yCoord = FormatCoordWithVars(linear.EndPoint.Y + offset.Y, "Y", linear.VariableRefs, ctx);
                sb.Append($"G1 X{xCoord} Y{yCoord}");

                // Feedrate — etch always uses process feedrate
                var feedVar = ctx.IsEtch ? "#148" : GetLinearFeedVariable(linear.Layer);
                if (feedVar != lastFeedVar)
                {
                    sb.Append($" F{feedVar}");
                    lastFeedVar = feedVar;
                }

                writer.WriteLine(sb.ToString());
                currentPos = linear.EndPoint;
            }
            else if (code is ArcMove arc)
            {
                var sb = new StringBuilder();

                // Kerf compensation on first cutting move (skip for etch)
                if (!ctx.IsEtch && !kerfEmitted && _config.KerfCompensation == KerfMode.ControllerSide)
                {
                    sb.Append(_config.DefaultKerfSide == KerfSide.Left ? "G41 " : "G42 ");
                    kerfEmitted = true;
                }

                // G2 = CW, G3 = CCW
                var gCode = arc.Rotation == RotationType.CW ? "G2" : "G3";
                var xCoord = FormatCoordWithVars(arc.EndPoint.X + offset.X, "X", arc.VariableRefs, ctx);
                var yCoord = FormatCoordWithVars(arc.EndPoint.Y + offset.Y, "Y", arc.VariableRefs, ctx);
                sb.Append($"{gCode} X{xCoord} Y{yCoord}");

                // Convert absolute center to incremental I/J
                var i = arc.CenterPoint.X - currentPos.X;
                var j = arc.CenterPoint.Y - currentPos.Y;
                sb.Append($" I{_fmt.FormatCoord(i)} J{_fmt.FormatCoord(j)}");

                // Feedrate — etch always uses process feedrate, cut uses layer/radius-based
                var radius = currentPos.DistanceTo(arc.CenterPoint);
                var isFullCircle = IsFullCircle(currentPos, arc.EndPoint);
                var feedVar = ctx.IsEtch ? "#148"
                    : GetArcFeedrate(arc.Layer, radius, isFullCircle);
                if (feedVar != lastFeedVar)
                {
                    sb.Append($" F{feedVar}");
                    lastFeedVar = feedVar;
                }

                writer.WriteLine(sb.ToString());
                currentPos = arc.EndPoint;
            }
        }

        // 7. Cancel kerf compensation
        if (kerfEmitted)
            writer.WriteLine("G40");

        // 8. Beam off
        writer.WriteLine(_config.UseSpeedGas ? "M135" : "M35");

        // 9. Anti-dive on
        if (_config.UseAntiDive)
            writer.WriteLine("M131 (ANTI DIVE ON)");

        // 10. Head raise (unless last feature on sheet)
        if (!ctx.IsLastFeatureOnSheet)
            WriteM47(writer, ctx);
    }

    /// <summary>
    /// Formats a coordinate value, using a #number variable reference if the motion
    /// has a VariableRef for this axis and the variable is mapped (non-inline).
    /// For cut-off features, plate-edge coordinates are substituted with
    /// the sheet width/length variables.
    /// Inline variables fall through to literal formatting.
    /// </summary>
    private string FormatCoordWithVars(double value, string axis,
        Dictionary<string, string> variableRefs, FeatureContext ctx)
    {
        // User-defined variable references take priority
        if (variableRefs != null
            && variableRefs.TryGetValue(axis, out var varName)
            && ctx.UserVariableMapping != null
            && ctx.UserVariableMapping.TryGetValue((ctx.DrawingId, varName), out var varNum))
        {
            return $"#{varNum}";
        }

        // Cut-off plate-edge substitution
        if (ctx.IsCutOff)
        {
            var sheetVar = MatchCutOffSheetVariable(value, axis, ctx);
            if (sheetVar != null)
                return sheetVar;
        }

        return _fmt.FormatCoord(value);
    }

    /// <summary>
    /// For cut-off coordinates, checks if the value matches a plate edge dimension
    /// and returns the sheet variable reference (e.g., "#110") if so.
    /// </summary>
    private string MatchCutOffSheetVariable(double value, string axis, FeatureContext ctx)
    {
        // Vertical cutoffs travel along Y — the Y endpoint at the plate edge = sheet width
        // Horizontal cutoffs travel along X — the X endpoint at the plate edge = sheet length
        if (axis == "Y" && Tolerance.IsEqualTo(value, ctx.PlateWidth))
            return $"#{_config.SheetWidthVariable}";
        if (axis == "X" && Tolerance.IsEqualTo(value, ctx.PlateLength))
            return $"#{_config.SheetLengthVariable}";

        return null;
    }

    private Vector FindPiercePoint(List<ICode> codes)
    {
        foreach (var code in codes)
        {
            if (code is RapidMove rapid)
                return rapid.EndPoint;
        }

        // If no rapid move, use the endpoint of the first motion
        foreach (var code in codes)
        {
            if (code is Motion motion)
                return motion.EndPoint;
        }

        return Vector.Zero;
    }

    private void WriteRapidToPierce(TextWriter writer, FeatureContext ctx, Vector piercePoint, Vector offset)
    {
        var sb = new StringBuilder();

        if (_config.UseLineNumbers)
            sb.Append($"N{ctx.FeatureNumber} ");

        var xCoord = FormatCoordWithVars(piercePoint.X + offset.X, "X", null, ctx);
        var yCoord = FormatCoordWithVars(piercePoint.Y + offset.Y, "Y", null, ctx);
        sb.Append($"G0 X{xCoord} Y{yCoord}");

        writer.WriteLine(sb.ToString());
    }

    private void WriteM47(TextWriter writer, FeatureContext ctx)
    {
        if (ctx.IsSafetyHeadraise && _config.SafetyHeadraiseDistance.HasValue)
        {
            writer.WriteLine($"M47 P{_config.SafetyHeadraiseDistance.Value} (Safety Headraise)");
            return;
        }

        var mode = ctx.IsExteriorFeature ? _config.ExteriorM47 : _config.InteriorM47;

        switch (mode)
        {
            case M47Mode.Always:
                writer.WriteLine("M47");
                break;
            case M47Mode.BlockDelete:
                writer.WriteLine("/M47");
                break;
            case M47Mode.None:
                break;
        }
    }

    private static string GetLinearFeedVariable(LayerType layer)
    {
        return layer switch
        {
            LayerType.Leadin => "#126",
            LayerType.Leadout => "#129",
            _ => "#148"
        };
    }

    private string GetArcFeedrate(LayerType layer, double radius, bool isFullCircle)
    {
        if (layer == LayerType.Leadin) return "#127";
        if (layer == LayerType.Leadout) return "#129";
        if (isFullCircle) return "[#148*#128]";
        return GetArcCutFeedrate(radius);
    }

    private string GetArcCutFeedrate(double radius)
    {
        if (_config.ArcFeedrate == ArcFeedrateMode.None)
            return "#148";

        // Find the smallest range that contains this radius
        ArcFeedrateRange best = null;
        foreach (var range in _config.ArcFeedrateRanges)
        {
            if (radius <= range.MaxRadius && (best == null || range.MaxRadius < best.MaxRadius))
                best = range;
        }

        if (best == null)
            return "#148";

        return _config.ArcFeedrate == ArcFeedrateMode.Variables
            ? $"#{best.VariableNumber}"
            : $"[#148*{best.FeedratePercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}]";
    }

    private static bool IsFullCircle(Vector start, Vector end)
    {
        return Tolerance.IsEqualTo(start.X, end.X) && Tolerance.IsEqualTo(start.Y, end.Y);
    }
}
