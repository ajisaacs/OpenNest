using System.Text.Json;

namespace OpenNest.Data;

/// <summary>Outcome of <see cref="ClampSettings.Load(string, out ClampSettingsStatus)"/>.</summary>
public enum ClampSettingsStatus
{
    /// <summary>Every value present in the file was valid.</summary>
    Ok,

    /// <summary>No file exists at the path; clamps stay off.</summary>
    Missing,

    /// <summary>
    /// The file loaded, but some values were invalid and were ignored (an invalid clamp
    /// center is dropped, other fields fall back individually). Callers should warn: the
    /// resulting rail may lack a physical clamp.
    /// </summary>
    Partial,

    /// <summary>The file exists but could not be read or parsed; clamps stay off.</summary>
    Invalid,
}

/// <summary>
/// An edge-clamp rail in given units, as a machine gives new nests (see
/// <see cref="MachineConfig.GetClampSettings"/>) and as the legacy single
/// %APPDATA%\OpenNest\clamps.json held it before each machine kept its own; that file is now
/// read only to copy it onto a machine once (<see cref="LegacyClampMigration"/>). Off by default:
/// until <see cref="Enabled"/> is set with at least one center, new nests get no clamps.
/// Loading never throws; values are validated field by field.
/// </summary>
public sealed class ClampSettings
{
    public const int CurrentVersion = 1;

    /// <summary>Default clamp extent along the edge, in <see cref="Units"/> (inches).</summary>
    public const double DefaultWidth = 1.5;

    /// <summary>Default clamp intrusion into the sheet, in <see cref="Units"/> (inches).</summary>
    public const double DefaultDepth = 0.625;

    private const double MillimetersPerInch = 25.4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Whether new nests receive this rail. Default off.</summary>
    public bool Enabled { get; set; }

    /// <summary>Units of <see cref="Width"/>, <see cref="Depth"/> and <see cref="Centers"/>.</summary>
    public Units Units { get; set; } = Units.Inches;

    public double Width { get; set; } = DefaultWidth;

    public double Depth { get; set; } = DefaultDepth;

    /// <summary>
    /// Minutes to remove one clamp and put it back after cutting (D9), stored and edited in
    /// the machine clamp settings alongside the rail's physical size.
    /// </summary>
    public double RemovalMinutes { get; set; } = ClampRail.DefaultRemovalMinutes;

    /// <summary>
    /// The gap parts keep from every clamp, in <see cref="Units"/>; null uses each sheet's
    /// bottom edge spacing (<see cref="ClampRail.Clearance"/>).
    /// </summary>
    public double? Clearance { get; set; }

    /// <summary>Origin-side clamp centers along X, measured from the plate origin.</summary>
    public IReadOnlyList<double> Centers { get; set; } = Array.Empty<double>();

    /// <summary>Far-side clamp centers along X, measured from the plate origin.</summary>
    public IReadOnlyList<double> FarSideCenters { get; set; } = Array.Empty<double>();

    /// <summary>Origin-end clamp centers along Y, measured from the plate origin.</summary>
    public IReadOnlyList<double> OriginEndCenters { get; set; } = Array.Empty<double>();

    /// <summary>Far-end clamp centers along Y, measured from the plate origin.</summary>
    public IReadOnlyList<double> FarEndCenters { get; set; } = Array.Empty<double>();

    /// <summary>Every configured clamp, by edge then center as listed.</summary>
    public IEnumerable<RailClamp> Clamps =>
        Centers.Select(c => new RailClamp(ClampEdge.OriginSide, c))
            .Concat(FarSideCenters.Select(c => new RailClamp(ClampEdge.FarSide, c)))
            .Concat(OriginEndCenters.Select(c => new RailClamp(ClampEdge.OriginEnd, c)))
            .Concat(FarEndCenters.Select(c => new RailClamp(ClampEdge.FarEnd, c)));

    /// <summary>The number of configured clamps on every edge.</summary>
    public int ClampCount =>
        Centers.Count + FarSideCenters.Count + OriginEndCenters.Count + FarEndCenters.Count;

    /// <summary>The centers listed for <paramref name="edge"/>.</summary>
    public IReadOnlyList<double> CentersOn(ClampEdge edge) =>
        edge switch
        {
            ClampEdge.FarSide => FarSideCenters,
            ClampEdge.OriginEnd => OriginEndCenters,
            ClampEdge.FarEnd => FarEndCenters,
            _ => Centers,
        };

    /// <summary>Lists <paramref name="clamps"/> under their edges, replacing every edge's centers.</summary>
    public void SetClamps(IEnumerable<RailClamp> clamps)
    {
        var list = clamps.ToList();
        double[] On(ClampEdge edge) => list.Where(c => c.Edge == edge).Select(c => c.Center).ToArray();
        Centers = On(ClampEdge.OriginSide);
        FarSideCenters = On(ClampEdge.FarSide);
        OriginEndCenters = On(ClampEdge.OriginEnd);
        FarEndCenters = On(ClampEdge.FarEnd);
    }

    /// <summary>The built-in settings used for a missing or unreadable file: clamps off.</summary>
    public static ClampSettings Fallback => new();

    /// <summary>%APPDATA%\OpenNest\clamps.json.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenNest",
            "clamps.json"
        );

    /// <summary>
    /// Captures a nest's rail (or the default clamp size and no centers when it has none)
    /// in that nest's units, e.g. for "save as machine defaults".
    /// </summary>
    public static ClampSettings FromRail(ClampRail? rail, Units units, bool enabled)
    {
        var settings = new ClampSettings
        {
            Enabled = enabled,
            Units = units,
            Width = rail?.Width ?? ConvertDefault(DefaultWidth, units),
            Depth = rail?.Depth ?? ConvertDefault(DefaultDepth, units),
            RemovalMinutes = rail?.RemovalMinutes ?? ClampRail.DefaultRemovalMinutes,
            Clearance = rail?.Clearance,
        };
        if (rail != null)
            settings.SetClamps(rail.Clamps);
        return settings;
    }

    /// <summary>
    /// The rail a new nest in <paramref name="units"/> receives, or null when clamps are
    /// disabled or no center is configured. Values are converted between inches and
    /// millimeters as needed; <see cref="RemovalMinutes"/> is a time, so it is never scaled.
    /// </summary>
    public ClampRail? RailFor(Units units)
    {
        if (!Enabled || ClampCount == 0)
            return null;

        var converted = ConvertedTo(units);
        return new ClampRail(converted.Width, converted.Depth, converted.Clamps, converted.RemovalMinutes,
            converted.Clearance);
    }

    /// <summary>
    /// A copy with width, depth, clearance and centers expressed in <paramref name="units"/>
    /// (inches and millimeters convert at 25.4); <see cref="Enabled"/> and
    /// <see cref="RemovalMinutes"/> (a time, not a length) are kept unscaled.
    /// </summary>
    public ClampSettings ConvertedTo(Units units)
    {
        var scale = Scale(Units, units);
        return new ClampSettings
        {
            Enabled = Enabled,
            Units = units,
            Width = Width * scale,
            Depth = Depth * scale,
            RemovalMinutes = RemovalMinutes,
            Clearance = Clearance * scale,
            Centers = Centers.Select(c => c * scale).ToArray(),
            FarSideCenters = FarSideCenters.Select(c => c * scale).ToArray(),
            OriginEndCenters = OriginEndCenters.Select(c => c * scale).ToArray(),
            FarEndCenters = FarEndCenters.Select(c => c * scale).ToArray(),
        };
    }

    /// <summary>
    /// Gives a newly created nest this machine rail when enabled (after the nest's units are
    /// set); otherwise leaves it untouched.
    /// </summary>
    public void ApplyToNewNest(Nest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var rail = RailFor(nest.Units);
        if (rail != null)
            nest.SetClampRail(rail);
    }

    public static ClampSettings Load(string path) => Load(path, out _);

    /// <summary>
    /// Loads the settings, never throwing. A missing or unreadable file yields
    /// <see cref="Fallback"/> (clamps off); a readable file merges valid values over it and
    /// reports <see cref="ClampSettingsStatus.Partial"/> if anything was ignored.
    /// </summary>
    public static ClampSettings Load(string path, out ClampSettingsStatus status)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            status = ClampSettingsStatus.Missing;
            return Fallback;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (SettingsFile.IsUnreadableFile(ex))
        {
            status = ClampSettingsStatus.Invalid;
            return Fallback;
        }
        return Parse(json, out status);
    }

    /// <summary>
    /// Reads settings from <paramref name="json"/> (a file or the shared copy) the way
    /// <see cref="Load(string, out ClampSettingsStatus)"/> reads a file, never throwing.
    /// </summary>
    public static ClampSettings Parse(string json, out ClampSettingsStatus status)
    {
        var settings = Fallback;

        // Read as a raw document so a missing field (fallback, Ok) is distinguishable from a
        // present but unusable one, including an explicit null (fallback, Partial).
        Dictionary<string, JsonElement> values;
        try
        {
            using var document = JsonDocument.Parse(json ?? "");
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                status = ClampSettingsStatus.Invalid;
                return settings;
            }
            values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
                values[property.Name] = property.Value.Clone();
        }
        catch (Exception ex) when (SettingsFile.IsUnreadableFile(ex))
        {
            status = ClampSettingsStatus.Invalid;
            return settings;
        }

        var ignored = false;

        if (values.TryGetValue("enabled", out var enabled))
        {
            if (enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                settings.Enabled = enabled.GetBoolean();
            else
                ignored = true;
        }

        if (values.TryGetValue("units", out var units))
        {
            if (SettingsFile.TryParseUnits(units, out var parsed))
                settings.Units = parsed;
            else
                ignored = true;
        }

        // Fallback sizes are the physical default clamp, expressed in the file's units.
        settings.Width = ConvertDefault(DefaultWidth, settings.Units);
        settings.Depth = ConvertDefault(DefaultDepth, settings.Units);

        if (values.TryGetValue("width", out var width))
        {
            if (TryLength(width, out var value) && value > 0)
                settings.Width = value;
            else
                ignored = true;
        }

        if (values.TryGetValue("depth", out var depth))
        {
            if (TryLength(depth, out var value) && value > 0)
                settings.Depth = value;
            else
                ignored = true;
        }

        if (values.TryGetValue("removalMinutes", out var removalMinutes))
        {
            if (TryLength(removalMinutes, out var value) && value >= 0)
                settings.RemovalMinutes = value;
            else
                ignored = true;
        }

        // Missing or null keeps the bottom edge spacing; anything else must be a length.
        if (values.TryGetValue("clearance", out var clearance) && clearance.ValueKind != JsonValueKind.Null)
        {
            if (TryLength(clearance, out var value) && value >= 0)
                settings.Clearance = value;
            else
                ignored = true;
        }

        if (values.TryGetValue("centers", out var centers))
            settings.Centers = ReadCenters(centers, ref ignored);
        if (values.TryGetValue("farSideCenters", out var farSide))
            settings.FarSideCenters = ReadCenters(farSide, ref ignored);
        if (values.TryGetValue("originEndCenters", out var originEnd))
            settings.OriginEndCenters = ReadCenters(originEnd, ref ignored);
        if (values.TryGetValue("farEndCenters", out var farEnd))
            settings.FarEndCenters = ReadCenters(farEnd, ref ignored);

        status = ignored ? ClampSettingsStatus.Partial : ClampSettingsStatus.Ok;
        return settings;
    }

    /// <summary>Valid centers of a JSON array, distinct and ascending; flags anything dropped.</summary>
    private static double[] ReadCenters(JsonElement centers, ref bool ignored)
    {
        if (centers.ValueKind != JsonValueKind.Array)
        {
            ignored = true;
            return Array.Empty<double>();
        }

        var valid = new List<double>();
        foreach (var center in centers.EnumerateArray())
        {
            if (TryLength(center, out var x) && x >= 0)
                valid.Add(x);
            else
                ignored = true;
        }
        return valid.Distinct().OrderBy(x => x).ToArray();
    }

    /// <summary>
    /// Writes the file (creating the parent directory), retrying briefly on IO collisions
    /// the same way <see cref="LocalJsonProvider"/> does.
    /// </summary>
    public void Save(string path, int maxRetries = 3)
    {
        SettingsFile.Write(path, ToJson(), maxRetries);
    }

    /// <summary>The settings as <see cref="Save"/> writes them, for the shared copy.</summary>
    public string ToJson()
    {
        var dto = new Dictionary<string, object>
        {
            ["version"] = CurrentVersion,
            ["enabled"] = Enabled,
            ["units"] = Units.ToString().ToLowerInvariant(),
            ["width"] = Width,
            ["depth"] = Depth,
            ["removalMinutes"] = RemovalMinutes,
            ["centers"] = Centers,
        };
        if (Clearance is double clearance)
            dto["clearance"] = clearance;
        // Other edges are written only when used, so origin-side-only files stay unchanged.
        if (FarSideCenters.Count > 0)
            dto["farSideCenters"] = FarSideCenters;
        if (OriginEndCenters.Count > 0)
            dto["originEndCenters"] = OriginEndCenters;
        if (FarEndCenters.Count > 0)
            dto["farEndCenters"] = FarEndCenters;
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static double Scale(Units from, Units to) =>
        from == to ? 1
        : from == Units.Inches ? MillimetersPerInch
        : 1 / MillimetersPerInch;

    private static double ConvertDefault(double inches, Units units) =>
        inches * Scale(Units.Inches, units);

    /// <summary>
    /// A finite JSON number that stays finite in either unit, so applying it to an inch or a
    /// millimeter nest can never overflow.
    /// </summary>
    private static bool TryLength(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value)
            && double.IsFinite(value * MillimetersPerInch);
    }
}
