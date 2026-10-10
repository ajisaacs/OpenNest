using System.Text.Json;

namespace OpenNest.Data;

/// <summary>Where the desktop app persists nests: local .nest files or the shared nest server.</summary>
public enum NestStorageMode
{
    File,
    Database,
}

/// <summary>
/// Storage-mode toggle and nest-server address, stored at %APPDATA%\OpenNest\storage.json.
/// Loading never throws; a missing or corrupt file means File mode so existing installs
/// keep the original behavior until the operator opts in.
/// </summary>
public sealed class NestStorageSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public NestStorageMode Mode { get; set; } = NestStorageMode.File;

    /// <summary>Base URL of the nest server, e.g. http://barge.lan:8090. No trailing slash required.</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>True when Database mode is active and a server URL is configured.</summary>
    public bool IsDatabaseMode =>
        Mode == NestStorageMode.Database && !string.IsNullOrWhiteSpace(ServerUrl);

    /// <summary>Validate and normalize a nest server base URL without changing storage mode.</summary>
    public static bool TryNormalizeServerUrl(string? url, out string normalized)
    {
        normalized = NormalizeUrl(url);
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return true;

        normalized = "";
        return false;
    }

    /// <summary>%APPDATA%\OpenNest\storage.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenNest", "storage.json");

    public static NestStorageSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new();

        try
        {
            var settings = JsonSerializer.Deserialize<NestStorageSettings>(
                File.ReadAllText(path), JsonOptions) ?? new();
            settings.ServerUrl = NormalizeUrl(settings.ServerUrl);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>Writes camelCase JSON, creating the parent directory and retrying IO collisions.</summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(
            new NestStorageSettings { Mode = Mode, ServerUrl = NormalizeUrl(ServerUrl) },
            JsonOptions);
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

    private static string NormalizeUrl(string? url) => (url ?? "").Trim().TrimEnd('/');
}
