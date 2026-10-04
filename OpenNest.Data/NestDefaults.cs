using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Geometry;

namespace OpenNest.Data;

/// <summary>Outcome of <see cref="NestDefaults.Load(string, out NestDefaultsStatus)"/>.</summary>
public enum NestDefaultsStatus
{
    /// <summary>Defaults were read from the file (invalid fields still fall back individually).</summary>
    Ok,

    /// <summary>No file exists at the path; built-in fallback values were used.</summary>
    Missing,

    /// <summary>The file exists but could not be read or parsed; fallback values were used.</summary>
    Invalid,
}

/// <summary>
/// Plate/nest defaults persisted to a single JSON file
/// (by default %APPDATA%\OpenNest\defaults.json), replacing the
/// .nstdot nest-template mechanism. Loading never throws: a missing,
/// corrupt, or partially valid file degrades field-by-field to
/// <see cref="Fallback"/> values so creating a new nest is never blocked.
/// </summary>
public sealed class NestDefaults
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public Units Units { get; set; } = Units.Inches;

    public Size Size { get; set; } = new(100, 100);

    public int Quadrant { get; set; } = 1;

    public double PartSpacing { get; set; } = 1;

    public Spacing EdgeSpacing { get; set; } = new(1, 1, 1, 1);

    /// <summary>
    /// The built-in defaults used when no file exists and for every field
    /// that is missing or invalid. Matches the historical
    /// MainForm.CreateDefaultNest values (units default to Inches; callers
    /// may override from their own settings).
    /// </summary>
    public static NestDefaults Fallback => new();

    /// <summary>%APPDATA%\OpenNest\defaults.json.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenNest",
            "defaults.json"
        );

    /// <summary>
    /// Loads defaults from <paramref name="path"/>, falling back field by
    /// field for a missing file, invalid JSON, or invalid values.
    /// </summary>
    public static NestDefaults Load(string path) => Load(path, out _);

    /// <summary>
    /// Loads defaults and reports whether the file was missing, loaded, or
    /// present but unreadable/invalid, so callers can warn about a corrupt
    /// file while still returning usable values.
    /// </summary>
    public static NestDefaults Load(string path, out NestDefaultsStatus status)
    {
        var defaults = Fallback;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            status = NestDefaultsStatus.Missing;
            return defaults;
        }

        NestDefaultsDto? dto;
        try
        {
            var json = File.ReadAllText(path);
            dto = JsonSerializer.Deserialize<NestDefaultsDto>(json, JsonOptions);
        }
        catch (Exception ex) when (IsUnreadableFile(ex))
        {
            // A file that exists but cannot be read (locked, access-denied)
            // or parsed must never block creating a nest.
            status = NestDefaultsStatus.Invalid;
            return defaults;
        }

        if (dto is null)
        {
            status = NestDefaultsStatus.Invalid;
            return defaults;
        }

        status = NestDefaultsStatus.Ok;

        if (
            dto.Units is not null
            && Enum.TryParse<Units>(dto.Units, ignoreCase: true, out var units)
        )
            defaults.Units = units;

        if (
            dto.Size?.Width is { } width
            && dto.Size.Length is { } length
            && IsValidSize(width, length)
        )
            defaults.Size = new Size(width, length);

        if (dto.Quadrant is { } quadrant && quadrant is >= 1 and <= 4)
            defaults.Quadrant = quadrant;

        if (dto.PartSpacing is { } partSpacing && IsValidSpacing(partSpacing))
            defaults.PartSpacing = partSpacing;

        if (
            dto.EdgeSpacing?.Left is { } left
            && dto.EdgeSpacing.Bottom is { } bottom
            && dto.EdgeSpacing.Right is { } right
            && dto.EdgeSpacing.Top is { } top
            && IsValidSpacing(left)
            && IsValidSpacing(bottom)
            && IsValidSpacing(right)
            && IsValidSpacing(top)
        )
            defaults.EdgeSpacing = new Spacing(left, bottom, right, top);

        return defaults;
    }

    /// <summary>
    /// Captures the current units and plate defaults from a nest.
    /// </summary>
    public static NestDefaults FromNest(Nest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var plate = nest.PlateDefaults;
        return new NestDefaults
        {
            Units = nest.Units,
            Size = plate.Size,
            Quadrant = plate.Quadrant,
            PartSpacing = plate.PartSpacing,
            EdgeSpacing = plate.EdgeSpacing,
        };
    }

    /// <summary>
    /// Captures defaults from an existing plate (a copy of its size,
    /// quadrant, and spacing), e.g. the active plate in the desktop app.
    /// </summary>
    public static NestDefaults FromPlate(Units units, Plate plate)
    {
        ArgumentNullException.ThrowIfNull(plate);
        return new NestDefaults
        {
            Units = units,
            Size = plate.Size,
            Quadrant = plate.Quadrant,
            PartSpacing = plate.PartSpacing,
            EdgeSpacing = plate.EdgeSpacing,
        };
    }

    public void ApplyTo(Nest nest)
    {
        ArgumentNullException.ThrowIfNull(nest);
        nest.Units = Units;
        var plate = nest.PlateDefaults;
        plate.Size = Size;
        plate.Quadrant = Quadrant;
        plate.PartSpacing = PartSpacing;
        plate.EdgeSpacing = EdgeSpacing;
    }

    /// <summary>
    /// Writes the file (creating the parent directory), retrying briefly on
    /// IO collisions the same way <see cref="LocalJsonProvider"/> does.
    /// </summary>
    public void Save(string path, int maxRetries = 3)
    {
        var dto = new NestDefaultsDto
        {
            Version = CurrentVersion,
            Units = Units.ToString().ToLowerInvariant(),
            Size = new SizeDto { Width = Size.Width, Length = Size.Length },
            Quadrant = Quadrant,
            PartSpacing = PartSpacing,
            EdgeSpacing = new SpacingDto
            {
                Left = EdgeSpacing.Left,
                Bottom = EdgeSpacing.Bottom,
                Right = EdgeSpacing.Right,
                Top = EdgeSpacing.Top,
            },
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

    private static bool IsUnreadableFile(Exception ex) =>
        ex
            is JsonException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException;

    private static bool IsValidSize(double width, double length) =>
        !double.IsNaN(width)
            && !double.IsNaN(length)
            && !double.IsInfinity(width)
            && !double.IsInfinity(length)
            && width > 0
            && length > 0;

    private static bool IsValidSpacing(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;

    /// <summary>
    /// Wire format. Every field is nullable so a partial file merges over
    /// the fallback field by field; unknown fields (including a future
    /// higher <c>version</c>) are ignored rather than rejected.
    /// </summary>
    private sealed record NestDefaultsDto
    {
        public int? Version { get; init; } = CurrentVersion;
        public string? Units { get; init; }
        public SizeDto? Size { get; init; }
        public int? Quadrant { get; init; }
        public double? PartSpacing { get; init; }
        public SpacingDto? EdgeSpacing { get; init; }
    }

    private sealed record SizeDto
    {
        public double? Width { get; init; }
        public double? Length { get; init; }
    }

    private sealed record SpacingDto
    {
        public double? Left { get; init; }
        public double? Bottom { get; init; }
        public double? Right { get; init; }
        public double? Top { get; init; }
    }
}
