namespace OpenNest.Data;

/// <summary>
/// Decision logic behind the desktop Clamps dialog: edits one nest's clamp rail in that
/// nest's units. Three explicit boundaries: <see cref="LoadMachineDefaults"/> only fills
/// the editor, <see cref="ApplyTo"/> changes the nest (the dialog's OK), and
/// <see cref="ToMachineDefaults"/> is what "Save as Machine Defaults" writes immediately.
/// </summary>
public sealed class ClampRailEditor
{
    private readonly List<double> _centers = new();

    /// <summary>Starts from the nest's rail, or the default clamp size with no clamps.</summary>
    public ClampRailEditor(ClampRail? rail, Units units, bool applyToNewNests)
    {
        Units = units;
        ApplyToNewNests = applyToNewNests;
        var initial = ClampSettings.FromRail(rail, units, applyToNewNests);
        Width = initial.Width;
        Depth = initial.Depth;
        RemovalMinutes = initial.RemovalMinutes;
        _centers.AddRange(initial.Centers);
    }

    /// <summary>The nest's units; every value is in these units.</summary>
    public Units Units { get; }

    /// <summary>Clamp extent along the edge (X).</summary>
    public double Width { get; set; }

    /// <summary>Clamp intrusion into the sheet (Y).</summary>
    public double Depth { get; set; }

    /// <summary>Minutes to remove one clamp and put it back after cutting (D9); not a
    /// length, so it is never converted between units.</summary>
    public double RemovalMinutes { get; set; }

    /// <summary>Whether saved machine defaults give new nests these clamps.</summary>
    public bool ApplyToNewNests { get; set; }

    /// <summary>Clamp centers along X from the plate origin, ascending and distinct.</summary>
    public IReadOnlyList<double> Centers => _centers;

    /// <summary>
    /// Adds a clamp center, keeping the list sorted. Refuses (with an operator-facing
    /// reason) values that are not finite, negative, or already listed.
    /// </summary>
    public bool TryAddCenter(double center, out string? error)
    {
        if (!double.IsFinite(center) || center < 0)
        {
            error = "Enter a clamp center measured from the plate origin (zero or more).";
            return false;
        }

        var index = _centers.BinarySearch(center);
        if (index >= 0)
        {
            error = $"A clamp is already centered at {center:0.####}.";
            return false;
        }

        _centers.Insert(~index, center);
        error = null;
        return true;
    }

    public void RemoveCenterAt(int index)
    {
        if (index >= 0 && index < _centers.Count)
            _centers.RemoveAt(index);
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
        ApplyToNewNests = settings.Enabled;
        _centers.Clear();
        _centers.AddRange(converted.Centers.Distinct().OrderBy(c => c));
    }

    /// <summary>
    /// The edited rail, or null when no clamp is listed. Throws
    /// <see cref="ArgumentOutOfRangeException"/> for a non-positive width or depth, or a
    /// negative/non-finite removal time.
    /// </summary>
    public ClampRail? BuildRail() =>
        _centers.Count == 0 ? null : new ClampRail(Width, Depth, _centers, RemovalMinutes);

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
        return new ClampSettings
        {
            Enabled = ApplyToNewNests && _centers.Count > 0,
            Units = Units,
            Width = Width,
            Depth = Depth,
            RemovalMinutes = RemovalMinutes,
            Centers = _centers.ToArray(),
        };
    }
}
