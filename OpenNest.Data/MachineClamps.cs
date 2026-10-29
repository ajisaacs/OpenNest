namespace OpenNest.Data;

/// <summary>
/// A machine's fixed edge-clamp rail, stored on its <see cref="MachineConfig"/> in the machine's
/// units. New nests assigned the machine copy it as their own rail; editing it never changes an
/// existing nest. <see cref="ToSettings"/> validates the stored values field by field.
/// </summary>
public sealed class MachineClamps
{
    /// <summary>Whether new nests assigned this machine receive the rail. Default off.</summary>
    public bool Enabled { get; set; }

    /// <summary>Clamp extent along its edge.</summary>
    public double Width { get; set; } = ClampSettings.DefaultWidth;

    /// <summary>Clamp intrusion into the sheet from its edge.</summary>
    public double Depth { get; set; } = ClampSettings.DefaultDepth;

    /// <summary>Minutes to remove one clamp and put it back after cutting; never scaled.</summary>
    public double RemovalMinutes { get; set; } = ClampRail.DefaultRemovalMinutes;

    /// <summary>The gap parts keep from every clamp; null uses each sheet's bottom edge spacing.</summary>
    public double? Clearance { get; set; }

    /// <summary>Origin-side clamp centers along X, measured from the plate origin.</summary>
    public List<double> Centers { get; set; } = new();

    /// <summary>Far-side clamp centers along X, measured from the plate origin.</summary>
    public List<double> FarSideCenters { get; set; } = new();

    /// <summary>Origin-end clamp centers along Y, measured from the plate origin.</summary>
    public List<double> OriginEndCenters { get; set; } = new();

    /// <summary>Far-end clamp centers along Y, measured from the plate origin.</summary>
    public List<double> FarEndCenters { get; set; } = new();

    /// <summary>The <see cref="Units"/> matching a machine's <see cref="UnitSystem"/>.</summary>
    public static Units UnitsOf(UnitSystem units) =>
        units == UnitSystem.Millimeters ? Units.Millimeters : Units.Inches;

    /// <summary>
    /// <paramref name="settings"/> converted to a machine in <paramref name="units"/>, e.g. for
    /// "Save as Machine Defaults" from a nest in other units or the clamps.json migration.
    /// </summary>
    public static MachineClamps FromSettings(ClampSettings settings, UnitSystem units)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var converted = settings.ConvertedTo(UnitsOf(units));
        return new MachineClamps
        {
            Enabled = converted.Enabled,
            Width = converted.Width,
            Depth = converted.Depth,
            RemovalMinutes = converted.RemovalMinutes,
            Clearance = converted.Clearance,
            Centers = converted.Centers.ToList(),
            FarSideCenters = converted.FarSideCenters.ToList(),
            OriginEndCenters = converted.OriginEndCenters.ToList(),
            FarEndCenters = converted.FarEndCenters.ToList(),
        };
    }

    /// <summary>
    /// The rail as clamp settings in the machine's <paramref name="units"/>. An invalid size or
    /// removal time falls back to the default, an invalid clearance to the bottom edge spacing,
    /// and an invalid center is dropped, as when
    /// <see cref="ClampSettings.Parse"/> reads a file.
    /// </summary>
    public ClampSettings ToSettings(UnitSystem units)
    {
        var defaults = ClampSettings.FromRail(null, UnitsOf(units), Enabled);
        if (IsLength(Width) && Width > 0)
            defaults.Width = Width;
        if (IsLength(Depth) && Depth > 0)
            defaults.Depth = Depth;
        if (IsLength(RemovalMinutes) && RemovalMinutes >= 0)
            defaults.RemovalMinutes = RemovalMinutes;
        if (Clearance is double clearance && IsLength(clearance) && clearance >= 0)
            defaults.Clearance = clearance;
        defaults.Centers = Valid(Centers);
        defaults.FarSideCenters = Valid(FarSideCenters);
        defaults.OriginEndCenters = Valid(OriginEndCenters);
        defaults.FarEndCenters = Valid(FarEndCenters);
        return defaults;
    }

    private static double[] Valid(IEnumerable<double>? centers) =>
        (centers ?? Enumerable.Empty<double>()).Where(c => IsLength(c) && c >= 0).Distinct().Order().ToArray();

    // Finite in either unit, so converting to an inch or millimeter nest cannot overflow.
    private static bool IsLength(double value) => double.IsFinite(value) && double.IsFinite(value * 25.4);
}
