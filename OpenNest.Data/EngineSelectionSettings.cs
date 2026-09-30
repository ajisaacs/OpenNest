using System.Text.Json;

namespace OpenNest.Data;

/// <summary>
/// Last-used Auto Nest engine, stored separately from nest/plate defaults.
/// Loading does not resolve the name: the host must finish plug-in discovery first.
/// </summary>
public sealed class EngineSelectionSettings
{
    public const string DefaultEngineName = "Default";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string EngineName { get; set; } = DefaultEngineName;

    /// <summary>%APPDATA%\OpenNest\engine-selection.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenNest", "engine-selection.json");

    /// <summary>Missing, unreadable or corrupt settings safely use Default without writing.</summary>
    public static EngineSelectionSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new();

        try
        {
            var settings = JsonSerializer.Deserialize<EngineSelectionSettings>(
                File.ReadAllText(path), JsonOptions) ?? new();
            settings.EngineName = NormalizeName(settings.EngineName);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>
    /// Resolves against the host's selectable engines AFTER plug-in loading. A missing engine
    /// returns Default plus a status-bar warning, without replacing the saved preference.
    /// Names use registry casing so desktop combo-box selection remains exact. When the saved
    /// name is not selectable, <paramref name="renamed"/> (the registry's legacy-name lookup)
    /// may map it to the engine that replaced it; the result must itself be selectable.
    /// </summary>
    public string Resolve(
        IEnumerable<string> availableEngineNames,
        out string? statusMessage,
        Func<string, string?>? renamed = null)
    {
        ArgumentNullException.ThrowIfNull(availableEngineNames);
        var available = availableEngineNames.ToList();
        var requestedName = NormalizeName(EngineName);
        var registeredName = Find(available, requestedName)
            ?? (renamed?.Invoke(requestedName) is { } replacement ? Find(available, replacement) : null);
        if (registeredName is not null)
        {
            statusMessage = null;
            return registeredName;
        }

        statusMessage = $"Saved Auto Nest engine '{requestedName}' is unavailable. Using Default.";
        return DefaultEngineName;
    }

    private static string? Find(IEnumerable<string> names, string name) =>
        names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Writes camelCase JSON, creating the parent directory and retrying IO collisions as
    /// LocalJsonProvider does. The host handles a persistent write failure.
    /// </summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(
            new EngineSelectionSettings { EngineName = NormalizeName(EngineName) }, JsonOptions);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(path, json);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static string NormalizeName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? DefaultEngineName : name.Trim();
}
