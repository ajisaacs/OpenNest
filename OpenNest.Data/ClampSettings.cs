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
/// The machine's fixed edge-clamp rail, persisted to a single JSON file (by default
/// %APPDATA%\OpenNest\clamps.json). Off by default: until <see cref="Enabled"/> is set
/// with at least one center, new nests get no clamps and behave exactly as before.
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

    /// <summary>Clamp centers along X, measured from the plate origin.</summary>
    public IReadOnlyList<double> Centers { get; set; } = Array.Empty<double>();

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
    public static ClampSettings FromRail(ClampRail? rail, Units units, bool enabled) =>
        new()
        {
            Enabled = enabled,
            Units = units,
            Width = rail?.Width ?? ConvertDefault(DefaultWidth, units),
            Depth = rail?.Depth ?? ConvertDefault(DefaultDepth, units),
            RemovalMinutes = rail?.RemovalMinutes ?? ClampRail.DefaultRemovalMinutes,
            Centers = rail?.Centers.ToArray() ?? Array.Empty<double>(),
        };

    /// <summary>
    /// The rail a new nest in <paramref name="units"/> receives, or null when clamps are
    /// disabled or no center is configured. Values are converted between inches and
    /// millimeters as needed; <see cref="RemovalMinutes"/> is a time, so it is never scaled.
    /// </summary>
    public ClampRail? RailFor(Units units)
    {
        if (!Enabled || Centers.Count == 0)
            return null;

        var converted = ConvertedTo(units);
        return new ClampRail(converted.Width, converted.Depth, converted.Centers, converted.RemovalMinutes);
    }

    /// <summary>
    /// A copy with width, depth and centers expressed in <paramref name="units"/>
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
            Centers = Centers.Select(c => c * scale).ToArray(),
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
        var settings = Fallback;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            status = ClampSettingsStatus.Missing;
            return settings;
        }

        // Read as a raw document so a missing field (fallback, Ok) is distinguishable from a
        // present but unusable one, including an explicit null (fallback, Partial).
        Dictionary<string, JsonElement> values;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                status = ClampSettingsStatus.Invalid;
                return settings;
            }
            values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
                values[property.Name] = property.Value.Clone();
        }
        catch (Exception ex) when (IsUnreadableFile(ex))
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
            if (TryParseUnits(units, out var parsed))
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

        if (values.TryGetValue("centers", out var centers))
        {
            if (centers.ValueKind == JsonValueKind.Array)
            {
                var valid = new List<double>();
                foreach (var center in centers.EnumerateArray())
                {
                    if (TryLength(center, out var x) && x >= 0)
                        valid.Add(x);
                    else
                        ignored = true;
                }
                settings.Centers = valid.Distinct().OrderBy(x => x).ToArray();
            }
            else
            {
                ignored = true;
            }
        }

        status = ignored ? ClampSettingsStatus.Partial : ClampSettingsStatus.Ok;
        return settings;
    }

    /// <summary>
    /// Writes the file (creating the parent directory), retrying briefly on IO collisions
    /// the same way <see cref="LocalJsonProvider"/> does.
    /// </summary>
    public void Save(string path, int maxRetries = 3)
    {
        var dto = new
        {
            version = CurrentVersion,
            enabled = Enabled,
            units = Units.ToString().ToLowerInvariant(),
            width = Width,
            depth = Depth,
            removalMinutes = RemovalMinutes,
            centers = Centers,
        };
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.WriteAllText(path, json);
                return;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                Thread.Sleep(100);
            }
        }
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

    /// <summary>
    /// Accepts only a defined unit name (case-insensitive); Enum.TryParse would also accept
    /// numeric strings and comma-joined names.
    /// </summary>
    private static bool TryParseUnits(JsonElement element, out Units units)
    {
        units = default;
        if (element.ValueKind != JsonValueKind.String)
            return false;

        var text = element.GetString();
        foreach (var candidate in Enum.GetValues<Units>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                units = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsUnreadableFile(Exception ex) =>
        ex
            is JsonException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException;
}
