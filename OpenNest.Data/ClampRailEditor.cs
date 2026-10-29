namespace OpenNest.Data;

/// <summary>
/// Decision logic behind the desktop Clamps dialog: edits one nest's clamp rail in that
/// nest's units. Three explicit boundaries: <see cref="LoadMachineDefaults"/> only fills
/// the editor, <see cref="ApplyTo"/> changes the nest (the dialog's OK), and
/// <see cref="ToMachineDefaults"/> is what "Save as Machine Defaults" writes immediately.
/// </summary>
public sealed class ClampRailEditor
{
    private readonly List<RailClamp> _clamps = new();

    /// <summary>Starts from the nest's rail, or the default clamp size with no clamps.</summary>
    public ClampRailEditor(ClampRail? rail, Units units, bool applyToNewNests)
    {
        Units = units;
        ApplyToNewNests = applyToNewNests;
        var initial = ClampSettings.FromRail(rail, units, applyToNewNests);
        Width = initial.Width;
        Depth = initial.Depth;
        RemovalMinutes = initial.RemovalMinutes;
        Clearance = initial.Clearance;
        _clamps.AddRange(initial.Clamps.Order());
    }

    /// <summary>The nest's units; every value is in these units.</summary>
    public Units Units { get; }

    /// <summary>Clamp extent along its edge.</summary>
    public double Width { get; set; }

    /// <summary>Clamp intrusion into the sheet from its edge.</summary>
    public double Depth { get; set; }

    /// <summary>Minutes to remove one clamp and put it back after cutting (D9); not a
    /// length, so it is never converted between units.</summary>
    public double RemovalMinutes { get; set; }

    /// <summary>The gap parts keep from every clamp; null uses each sheet's bottom edge spacing.</summary>
    public double? Clearance { get; set; }

    /// <summary>Whether saved machine defaults give new nests these clamps.</summary>
    public bool ApplyToNewNests { get; set; }

    /// <summary>Clamps ordered by edge, then center from the plate origin; distinct.</summary>
    public IReadOnlyList<RailClamp> Clamps => _clamps;

    /// <summary>Origin-side clamp centers along X from the plate origin, ascending.</summary>
    public IReadOnlyList<double> Centers =>
        _clamps.Where(c => c.Edge == ClampEdge.OriginSide).Select(c => c.Center).ToList();

    /// <summary>Adds an origin-side clamp; see <see cref="TryAddClamp"/>.</summary>
    public bool TryAddCenter(double center, out string? error) =>
        TryAddClamp(new RailClamp(ClampEdge.OriginSide, center), out error);

    /// <summary>
    /// Adds a clamp, keeping the list sorted. Refuses (with an operator-facing reason)
    /// centers that are not finite, negative, or already listed on that edge.
    /// </summary>
    public bool TryAddClamp(RailClamp clamp, out string? error)
    {
        if (!double.IsFinite(clamp.Center) || clamp.Center < 0)
        {
            error = "Enter a clamp center measured from the plate origin (zero or more).";
            return false;
        }

        var index = _clamps.BinarySearch(clamp);
        if (index >= 0)
        {
            error = $"A clamp is already centered at {clamp.Center:0.####} on that edge.";
            return false;
        }

        _clamps.Insert(~index, clamp);
        error = null;
        return true;
    }

    /// <summary>Removes the clamp at <paramref name="index"/> in <see cref="Clamps"/>.</summary>
    public void RemoveCenterAt(int index)
    {
        if (index >= 0 && index < _clamps.Count)
            _clamps.RemoveAt(index);
    }

    /// <summary>
    /// Replaces the editor's values with the machine defaults converted to this nest's
    /// units. Nothing is applied or saved.
    /// </summary>
    public void LoadMachineDefaults(ClampSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var converted = settings.ConvertedTo(Units);
        Width = converted.Width;
        Depth = converted.Depth;
        RemovalMinutes = converted.RemovalMinutes;
        Clearance = converted.Clearance;
        ApplyToNewNests = settings.Enabled;
        _clamps.Clear();
        _clamps.AddRange(converted.Clamps.Distinct().Order());
    }

    /// <summary>
    /// The edited rail, or null when no clamp is listed. Throws
    /// <see cref="ArgumentOutOfRangeException"/> for a non-positive width or depth, or a
    /// negative/non-finite removal time or clearance.
    /// </summary>
    public ClampRail? BuildRail() =>
        _clamps.Count == 0 ? null : new ClampRail(Width, Depth, _clamps, RemovalMinutes, Clearance);

    /// <summary>The dialog's OK: makes the edited rail the nest's, re-resolving every plate.</summary>
    public void ApplyTo(Nest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);
        nest.SetClampRail(BuildRail());
    }

    /// <summary>
    /// The machine settings "Save as Machine Defaults" writes: these values in this nest's
    /// units, enabled for new nests only when requested and at least one clamp is listed.
    /// </summary>
    public ClampSettings ToMachineDefaults()
    {
        // Validate before anything is persisted.
        if (!double.IsFinite(Width) || Width <= 0 || !double.IsFinite(Depth) || Depth <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(Width),
                "Clamp width and depth must be positive."
            );
        if (!double.IsFinite(RemovalMinutes) || RemovalMinutes < 0)
            throw new ArgumentOutOfRangeException(
                nameof(RemovalMinutes),
                "Clamp removal time must be finite and zero or more."
            );
        if (Clearance is double clearance && (!double.IsFinite(clearance) || clearance < 0))
            throw new ArgumentOutOfRangeException(
                nameof(Clearance),
                "Clamp clearance must be finite and zero or more."
            );
        var settings = new ClampSettings
        {
            Enabled = ApplyToNewNests && _clamps.Count > 0,
            Units = Units,
            Width = Width,
            Depth = Depth,
            RemovalMinutes = RemovalMinutes,
            Clearance = Clearance,
        };
        settings.SetClamps(_clamps);
        return settings;
    }
}
