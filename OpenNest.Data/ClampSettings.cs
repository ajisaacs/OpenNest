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
            Centers = rail?.Centers.ToArray() ?? Array.Empty<double>(),
        };

    /// <summary>
    /// The rail a new nest in <paramref name="units"/> receives, or null when clamps are
    /// disabled or no center is configured. Values are converted between inches and
    /// millimeters as needed.
    /// </summary>
    public ClampRail? RailFor(Units units)
    {
        if (!Enabled || Centers.Count == 0)
            return null;

        var scale = Scale(Units, units);
        return new ClampRail(Width * scale, Depth * scale, Centers.Select(c => c * scale));
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

        ClampSettingsDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ClampSettingsDto>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (IsUnreadableFile(ex))
        {
            status = ClampSettingsStatus.Invalid;
            return settings;
        }

        if (dto is null)
        {
            status = ClampSettingsStatus.Invalid;
            return settings;
        }

        var ignored = false;

        if (dto.Enabled is { } enabled)
        {
            if (enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                settings.Enabled = enabled.GetBoolean();
            else
                ignored = true;
        }

        if (dto.Units is { } units)
        {
            if (TryParseUnits(units, out var parsed))
                settings.Units = parsed;
            else
                ignored = true;
        }

        if (dto.Width is { } width)
        {
            if (TryPositive(width, out var value))
                settings.Width = value;
            else
                ignored = true;
        }

        if (dto.Depth is { } depth)
        {
            if (TryPositive(depth, out var value))
                settings.Depth = value;
            else
                ignored = true;
        }

        if (dto.Centers is { } centers)
        {
            if (centers.ValueKind == JsonValueKind.Array)
            {
                var valid = new List<double>();
                foreach (var center in centers.EnumerateArray())
                {
                    if (
                        center.ValueKind == JsonValueKind.Number
                        && center.TryGetDouble(out var x)
                        && double.IsFinite(x)
                        && x >= 0
                    )
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

    private static bool TryPositive(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value)
            && value > 0;
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

    /// <summary>
    /// Wire format. Every value is read as raw JSON so a wrongly typed field falls back on
    /// its own instead of failing the whole file; unknown fields (including a future higher
    /// <c>version</c>) are ignored.
    /// </summary>
    private sealed record ClampSettingsDto
    {
        public JsonElement? Version { get; init; }
        public JsonElement? Enabled { get; init; }
        public JsonElement? Units { get; init; }
        public JsonElement? Width { get; init; }
        public JsonElement? Depth { get; init; }
        public JsonElement? Centers { get; init; }
    }
}
