using System.Text.Json;
using OpenNest.Support;

namespace OpenNest.Data;

/// <summary>
/// The machine's support bed, a laser grid of points or a plasma table's slats, persisted to a
/// single JSON file (by default %APPDATA%\OpenNest\support-grid.json). Both layouts' values are
/// kept; <see cref="UseSlats"/> picks one. Off by default: until <see cref="Enabled"/> is set,
/// new nests get no support bed. Loading never throws; an invalid value falls back to its
/// default and a file that cannot be read loads as off.
/// </summary>
public sealed class SupportGridSettings
{
    public const int CurrentVersion = 1;

    private const double MillimetersPerInch = 25.4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Whether new nests receive this grid. Default off.</summary>
    public bool Enabled { get; set; }

    /// <summary>Units of every length below.</summary>
    public Units Units { get; set; } = Units.Inches;

    public double PitchX { get; set; } = 2;
    public double PitchY { get; set; } = 2;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    /// <summary>Whether the bed is slats rather than the grid of points.</summary>
    public bool UseSlats { get; set; }

    public SlatAxis SlatAxis { get; set; } = SlatAxis.Y;
    public double SlatPitch { get; set; } = 4;
    public double SlatThickness { get; set; } = 0.125;
    public double SlatOffset { get; set; }

    public double RegistrationTolerance { get; set; } = 0.125;
    public double StabilityMargin { get; set; } = 0.25;

    /// <summary>%APPDATA%\OpenNest\support-grid.json.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenNest",
            "support-grid.json"
        );

    /// <summary>
    /// Settings holding <paramref name="bed"/> in <paramref name="units"/>; the other layout's
    /// values come from <paramref name="others"/> (or the defaults when null).
    /// </summary>
    public static SupportGridSettings FromBed(SupportBed bed, Units units, bool enabled, SupportGridSettings others = null)
    {
        var settings = (others ?? new SupportGridSettings()).ConvertedTo(units);
        settings.Enabled = enabled;
        if (bed != null)
        {
            settings.RegistrationTolerance = bed.RegistrationTolerance;
            settings.StabilityMargin = bed.StabilityMargin;
        }
        switch (bed)
        {
            case SupportGrid grid:
                settings.UseSlats = false;
                settings.PitchX = grid.PitchX;
                settings.PitchY = grid.PitchY;
                settings.OffsetX = grid.OffsetX;
                settings.OffsetY = grid.OffsetY;
                break;
            case SupportSlats slats:
                settings.UseSlats = true;
                settings.SlatAxis = slats.Axis;
                settings.SlatPitch = slats.Pitch;
                settings.SlatThickness = slats.Thickness;
                settings.SlatOffset = slats.Offset;
                break;
        }
        return settings;
    }

    /// <summary>The bed these settings describe in <paramref name="units"/>, or null when it is invalid.</summary>
    public SupportBed? BedFor(Units units)
    {
        var converted = ConvertedTo(units);
        try
        {
            return UseSlats
                ? new SupportSlats(converted.SlatAxis, converted.SlatPitch, converted.SlatThickness,
                    converted.SlatOffset, converted.RegistrationTolerance, converted.StabilityMargin)
                : new SupportGrid(converted.PitchX, converted.PitchY, converted.OffsetX, converted.OffsetY,
                    converted.RegistrationTolerance, converted.StabilityMargin);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>A copy with every length expressed in <paramref name="units"/> (inches and millimeters convert at 25.4).</summary>
    public SupportGridSettings ConvertedTo(Units units)
    {
        var scale = Units == units ? 1 : Units == Units.Inches ? MillimetersPerInch : 1 / MillimetersPerInch;
        return new SupportGridSettings
        {
            Enabled = Enabled,
            Units = units,
            PitchX = PitchX * scale,
            PitchY = PitchY * scale,
            OffsetX = OffsetX * scale,
            OffsetY = OffsetY * scale,
            UseSlats = UseSlats,
            SlatAxis = SlatAxis,
            SlatPitch = SlatPitch * scale,
            SlatThickness = SlatThickness * scale,
            SlatOffset = SlatOffset * scale,
            RegistrationTolerance = RegistrationTolerance * scale,
            StabilityMargin = StabilityMargin * scale,
        };
    }

    /// <summary>Gives a newly created nest this bed when enabled (after its units are set).</summary>
    public void ApplyToNewNest(Nest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);
        if (Enabled && BedFor(nest.Units) is { } bed)
            nest.SetSupportBed(bed);
    }

    /// <summary>
    /// Loads the settings, never throwing. A missing or unreadable file yields the defaults
    /// (off); each invalid value in a readable file keeps its default.
    /// </summary>
    public static SupportGridSettings Load(string path)
    {
        var settings = new SupportGridSettings();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return settings;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return settings;

            if (root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                settings.Enabled = enabled.GetBoolean();
            // Lengths read below are in the file's units; the defaults are converted to them first.
            if (root.TryGetProperty("units", out var units) && units.ValueKind == JsonValueKind.String)
                foreach (var unit in Enum.GetValues<Units>())
                    if (string.Equals(unit.ToString(), units.GetString(), StringComparison.OrdinalIgnoreCase))
                        settings = settings.ConvertedTo(unit);

            settings.PitchX = Length(root, "pitchX", settings.PitchX, positive: true);
            settings.PitchY = Length(root, "pitchY", settings.PitchY, positive: true);
            settings.OffsetX = Length(root, "offsetX", settings.OffsetX, positive: false, signed: true);
            settings.OffsetY = Length(root, "offsetY", settings.OffsetY, positive: false, signed: true);
            if (root.TryGetProperty("layout", out var layout) && layout.ValueKind == JsonValueKind.String)
                settings.UseSlats = string.Equals(layout.GetString(), "slats", StringComparison.OrdinalIgnoreCase);
            if (root.TryGetProperty("slatAxis", out var axis) && axis.ValueKind == JsonValueKind.String
                && Enum.TryParse<SlatAxis>(axis.GetString(), ignoreCase: true, out var slatAxis)
                && Enum.IsDefined(slatAxis))
                settings.SlatAxis = slatAxis;
            settings.SlatPitch = Length(root, "slatPitch", settings.SlatPitch, positive: true);
            settings.SlatThickness = Length(root, "slatThickness", settings.SlatThickness, positive: false);
            settings.SlatOffset = Length(root, "slatOffset", settings.SlatOffset, positive: false, signed: true);
            settings.RegistrationTolerance = Length(root, "registrationTolerance", settings.RegistrationTolerance, positive: false);
            settings.StabilityMargin = Length(root, "stabilityMargin", settings.StabilityMargin, positive: false);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
            or NotSupportedException or System.Security.SecurityException)
        {
            return new SupportGridSettings();
        }
    }

    /// <summary>Writes the file, creating its directory.</summary>
    public void Save(string path)
    {
        var dto = new Dictionary<string, object>
        {
            ["version"] = CurrentVersion,
            ["enabled"] = Enabled,
            ["units"] = Units.ToString().ToLowerInvariant(),
            ["pitchX"] = PitchX,
            ["pitchY"] = PitchY,
            ["offsetX"] = OffsetX,
            ["offsetY"] = OffsetY,
            ["layout"] = UseSlats ? "slats" : "grid",
            ["slatAxis"] = SlatAxis.ToString().ToLowerInvariant(),
            ["slatPitch"] = SlatPitch,
            ["slatThickness"] = SlatThickness,
            ["slatOffset"] = SlatOffset,
            ["registrationTolerance"] = RegistrationTolerance,
            ["stabilityMargin"] = StabilityMargin,
        };
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    // A finite number that stays finite in either unit; otherwise the fallback.
    private static double Length(JsonElement root, string name, double fallback, bool positive, bool signed = false)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out var value) || !double.IsFinite(value) || !double.IsFinite(value * MillimetersPerInch))
            return fallback;
        if (positive ? value <= 0 : !signed && value < 0)
            return fallback;
        return value;
    }
}
