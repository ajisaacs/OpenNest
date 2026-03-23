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
    public string LibraryFile { get; set; } = "";
    public double CutDistance { get; set; }
    public double SheetDiagonal { get; set; }
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

        // Find the pierce point from the first rapid move
        var piercePoint = FindPiercePoint(ctx.Codes);

        // 1. Rapid to pierce point (with line number if configured)
        WriteRapidToPierce(writer, ctx.FeatureNumber, piercePoint);

        // 2. Part name comment on first feature of each part
        if (ctx.IsFirstFeatureOfPart && !string.IsNullOrEmpty(ctx.PartName))
            writer.WriteLine(CoordinateFormatter.Comment($"PART: {ctx.PartName}"));

        // 3. G89 process params (if RepeatG89BeforeEachFeature)
        if (_config.RepeatG89BeforeEachFeature && _config.ProcessParameterMode == G89Mode.LibraryFile)
        {
            var lib = !string.IsNullOrEmpty(ctx.LibraryFile) ? ctx.LibraryFile : _config.DefaultLibraryFile;
            var speedClass = _speedClassifier.Classify(ctx.CutDistance, ctx.SheetDiagonal);
            var cutDist = _speedClassifier.FormatCutDist(ctx.CutDistance, ctx.SheetDiagonal);
            writer.WriteLine($"G89 P {lib} ({speedClass} {cutDist})");
        }

        // 4. Pierce and start cut
        writer.WriteLine("G84");

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

                // Kerf compensation on first cutting move
                if (!kerfEmitted && _config.KerfCompensation == KerfMode.ControllerSide)
                {
                    sb.Append(_config.DefaultKerfSide == KerfSide.Left ? "G41" : "G42");
                    kerfEmitted = true;
                }

                sb.Append($"G1X{_fmt.FormatCoord(linear.EndPoint.X)}Y{_fmt.FormatCoord(linear.EndPoint.Y)}");

                // Feedrate
                var feedVar = GetFeedVariable(linear.Layer);
                if (feedVar != lastFeedVar)
                {
                    sb.Append($"F{feedVar}");
                    lastFeedVar = feedVar;
                }

                writer.WriteLine(sb.ToString());
                currentPos = linear.EndPoint;
            }
            else if (code is ArcMove arc)
            {
                var sb = new StringBuilder();

                // Kerf compensation on first cutting move
                if (!kerfEmitted && _config.KerfCompensation == KerfMode.ControllerSide)
                {
                    sb.Append(_config.DefaultKerfSide == KerfSide.Left ? "G41" : "G42");
                    kerfEmitted = true;
                }

                // G2 = CW, G3 = CCW
                var gCode = arc.Rotation == RotationType.CW ? "G2" : "G3";
                sb.Append($"{gCode}X{_fmt.FormatCoord(arc.EndPoint.X)}Y{_fmt.FormatCoord(arc.EndPoint.Y)}");

                // Convert absolute center to incremental I/J
                var i = arc.CenterPoint.X - currentPos.X;
                var j = arc.CenterPoint.Y - currentPos.Y;
                sb.Append($"I{_fmt.FormatCoord(i)}J{_fmt.FormatCoord(j)}");

                // Feedrate — full circles use multiplied feedrate
                var isFullCircle = IsFullCircle(currentPos, arc.EndPoint);
                var feedVar = isFullCircle ? "[#148*#128]" : GetFeedVariable(arc.Layer);
                if (feedVar != lastFeedVar)
                {
                    sb.Append($"F{feedVar}");
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

    private void WriteRapidToPierce(TextWriter writer, int featureNumber, Vector piercePoint)
    {
        var sb = new StringBuilder();

        if (_config.UseLineNumbers)
            sb.Append($"N{featureNumber}");

        sb.Append($"G0X{_fmt.FormatCoord(piercePoint.X)}Y{_fmt.FormatCoord(piercePoint.Y)}");

        writer.WriteLine(sb.ToString());
    }

    private void WriteM47(TextWriter writer, FeatureContext ctx)
    {
        if (ctx.IsSafetyHeadraise && _config.SafetyHeadraiseDistance.HasValue)
        {
            writer.WriteLine($"M47 P{_config.SafetyHeadraiseDistance.Value}(Safety Headraise)");
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

    private static string GetFeedVariable(LayerType layer)
    {
        return layer switch
        {
            LayerType.Leadin => "#126",
            LayerType.Cut => "#148",
            _ => "#148"
        };
    }

    private static bool IsFullCircle(Vector start, Vector end)
    {
        return Tolerance.IsEqualTo(start.X, end.X) && Tolerance.IsEqualTo(start.Y, end.Y);
    }
}
