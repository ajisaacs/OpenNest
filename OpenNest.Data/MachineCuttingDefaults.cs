using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Data;

/// <summary>
/// The lead-in, lead-out, kerf and cut-off a machine configuration has on file for a nest's
/// material and thickness, converted to the nest's units. A machine has one lead-in and one
/// lead-out per thickness, so they apply to external, internal and arc/circle contours alike;
/// a Clean Hole lead-in is for holes only and leaves the external lead-in as it was, and a
/// Line + Line lead-in, whose second segment the configuration does not hold, leaves every
/// lead-in as it was.
/// </summary>
public static class MachineCuttingDefaults
{
    private const double MillimetersPerInch = 25.4;

    // Thicknesses are typed or imported to three decimals (PEP stores 3/16 as 0.188), so this
    // matches those and ones converted between units; gauges are far further apart.
    private const double ThicknessToleranceInches = 0.001;

    /// <summary>
    /// The thickness settings of <paramref name="machine"/> for the nest's material (its grade
    /// when one is set, otherwise the first grade on file) and thickness, or null when the
    /// machine has none.
    /// </summary>
    public static ThicknessConfig? Find(MachineConfig? machine, Nest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var name = nest.Material?.Name;
        if (machine == null || string.IsNullOrWhiteSpace(name) || !double.IsFinite(nest.Thickness) || nest.Thickness <= 0)
            return null;

        var grade = nest.Material.Grade;
        var material = string.IsNullOrWhiteSpace(grade) ? machine.GetMaterial(name) : machine.GetMaterial(name, grade);
        if (material == null)
            return null;

        var thickness = nest.Thickness * Scale(nest.Units, machine.Units);
        var tolerance = ThicknessToleranceInches * Scale(Units.Inches, machine.Units);
        return material.Thicknesses
            .Where(t => System.Math.Abs(t.Value - thickness) <= tolerance)
            .OrderBy(t => System.Math.Abs(t.Value - thickness))
            .FirstOrDefault();
    }

    /// <summary>
    /// Replaces the lead-ins, lead-outs and kerf of <paramref name="parameters"/> with the
    /// machine's for the nest, and records the machine, material and thickness they came from.
    /// Returns false, changing nothing, when the machine has no settings for the nest.
    /// </summary>
    public static bool ApplyTo(CuttingParameters parameters, MachineConfig? machine, Nest nest)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (Find(machine, nest) is not { } settings)
            return false;

        var scale = Scale(machine!.Units, nest.Units);
        var kerf = settings.Kerf * scale;
        // Each lead set gets its own instance, as the cutting panel builds them.
        if (ToLeadIn(settings.LeadIn, scale, kerf) is { } leadIn)
        {
            if (leadIn is not CleanHoleLeadIn)
                parameters.ExternalLeadIn = leadIn;
            parameters.InternalLeadIn = ToLeadIn(settings.LeadIn, scale, kerf);
        }
        parameters.ExternalLeadOut = ToLeadOut(settings.LeadOut, scale);
        parameters.InternalLeadOut = ToLeadOut(settings.LeadOut, scale);
        parameters.Kerf = kerf;
        parameters.MachineName = machine.Name;
        parameters.MaterialName = nest.Material.Name;
        parameters.Grade = nest.Material.Grade;
        parameters.Thickness = nest.Thickness;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="parameters"/> are stamped with the machine, material, grade and
    /// thickness <see cref="ApplyTo(CuttingParameters, MachineConfig?, Nest)"/> would record for
    /// the nest: they started from the machine's settings for it, and any lead edits since are
    /// the user's choice for that same job, which the machine's settings must not replace.
    /// </summary>
    public static bool IsRecordedFor(CuttingParameters parameters, MachineConfig? machine, Nest nest)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(nest);
        if (Find(machine, nest) == null)
            return false;
        var tolerance = ThicknessToleranceInches * Scale(Units.Inches, nest.Units);
        return string.Equals(parameters.MachineName, machine!.Name, StringComparison.Ordinal)
            && string.Equals(parameters.MaterialName, nest.Material.Name, StringComparison.Ordinal)
            && string.Equals(parameters.Grade ?? "", nest.Material.Grade ?? "", StringComparison.Ordinal)
            && System.Math.Abs(parameters.Thickness - nest.Thickness) <= tolerance;
    }

    /// <summary>
    /// Replaces the part clearance, overtravel, minimum segment and direction of
    /// <paramref name="settings"/> with the machine's for the nest; the head-lock choice is kept.
    /// Returns false, changing nothing, when the machine has no settings for the nest.
    /// </summary>
    public static bool ApplyTo(CutOffSettings settings, MachineConfig? machine, Nest nest)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (Find(machine, nest)?.CutOff is not { } cutOff)
            return false;

        var scale = Scale(machine!.Units, nest.Units);
        settings.PartClearance = cutOff.PartClearance * scale;
        settings.Overtravel = cutOff.Overtravel * scale;
        settings.MinSegmentLength = cutOff.MinSegmentLength * scale;
        if (Enum.TryParse<CutDirection>(cutOff.Direction, true, out var direction) && Enum.IsDefined(direction))
            settings.CutDirection = direction;
        return true;
    }

    // Lead types are the names Tools > Machine Configuration offers; null when one cannot be built.
    private static LeadIn? ToLeadIn(LeadConfig? lead, double scale, double kerf) => lead?.Type switch
    {
        null or "None" => new NoLeadIn(),
        "Line" => new LineLeadIn { Length = lead.Length * scale, ApproachAngle = lead.Angle },
        "Arc" => new ArcLeadIn { Radius = lead.Radius * scale },
        "Line + Arc" => new LineArcLeadIn
        {
            LineLength = lead.Length * scale,
            ArcRadius = lead.Radius * scale,
            ApproachAngle = lead.Angle,
        },
        "Clean Hole" => new CleanHoleLeadIn { LineLength = lead.Length * scale, ArcRadius = lead.Radius * scale, Kerf = kerf },
        _ => null,
    };

    private static LeadOut ToLeadOut(LeadConfig? lead, double scale) => lead?.Type switch
    {
        "Line" => new LineLeadOut { Length = lead.Length * scale, ApproachAngle = lead.Angle },
        "Arc" => new ArcLeadOut { Radius = lead.Radius * scale },
        _ => new NoLeadOut(),
    };

    private static double Scale(UnitSystem from, Units to) =>
        Scale(from == UnitSystem.Millimeters ? Units.Millimeters : Units.Inches, to);

    private static double Scale(Units from, UnitSystem to) =>
        Scale(from, to == UnitSystem.Millimeters ? Units.Millimeters : Units.Inches);

    private static double Scale(Units from, Units to) => (from, to) switch
    {
        (Units.Inches, Units.Millimeters) => MillimetersPerInch,
        (Units.Millimeters, Units.Inches) => 1 / MillimetersPerInch,
        _ => 1,
    };
}
