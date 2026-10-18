using System.Drawing;
using OpenNest;

namespace OpenNest.Data;

/// <summary>
/// Per-drawing nesting settings a library record owns. They are structured server fields,
/// not package content: canonical packages leave them at their defaults, and adding a library
/// drawing to a nest applies these values. Editing them adds a revision without new geometry.
/// A record, so two settings are equal when every value is.
/// </summary>
public sealed record DrawingNestingSettings
{
    /// <summary>Automatic sequencing cuts this drawing's parts first (for example test squares).</summary>
    public bool CutFirst { get; init; }

    public int Priority { get; init; }

    /// <summary>Rotation step in radians; zero means the engine's default.</summary>
    public double StepAngle { get; init; }

    /// <summary>Rotation start angle in radians.</summary>
    public double StartAngle { get; init; }

    /// <summary>Rotation end angle in radians.</summary>
    public double EndAngle { get; init; }

    public bool Allow180Equivalent { get; init; }

    /// <summary>
    /// Display color as a 32-bit ARGB value. A fully transparent value (alpha 0, the default)
    /// means no stored color: the drawing gets the next part color when it is added.
    /// </summary>
    public int ColorArgb { get; init; }

    /// <summary>The settings <paramref name="drawing"/> currently has.</summary>
    public static DrawingNestingSettings FromDrawing(Drawing drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        var constraints = drawing.Constraints ?? new NestConstraints();
        return new DrawingNestingSettings
        {
            CutFirst = drawing.CutFirst,
            Priority = drawing.Priority,
            StepAngle = constraints.StepAngle,
            StartAngle = constraints.StartAngle,
            EndAngle = constraints.EndAngle,
            Allow180Equivalent = constraints.Allow180Equivalent,
            ColorArgb = drawing.Color.ToArgb(),
        };
    }

    /// <summary>Writes these settings onto <paramref name="drawing"/>.</summary>
    public void ApplyTo(Drawing drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        drawing.CutFirst = CutFirst;
        drawing.Priority = Priority;
        drawing.Constraints = new NestConstraints
        {
            StepAngle = StepAngle,
            StartAngle = StartAngle,
            EndAngle = EndAngle,
            Allow180Equivalent = Allow180Equivalent,
        };
        var color = Color.FromArgb(ColorArgb);
        drawing.Color = color.A == 0 ? Drawing.GetNextColor() : color;
    }

    /// <summary>The first rule these settings break, or null when they may be saved.</summary>
    public string? GetValidationError()
    {
        if (!double.IsFinite(StepAngle) || !double.IsFinite(StartAngle) || !double.IsFinite(EndAngle))
            return "settings angles must be finite numbers.";
        // Ranges are not checked: nest drawings carry whatever their files hold, and
        // saving one to the library must not be refused for it.
        return StepAngle < 0 ? "settings.stepAngle must be zero or greater." : null;
    }
}
