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

    /// <summary>
    /// Servers this PC has used, each with the operator's note on what it is for, so the
    /// server pickers can offer them by name. Always holds <see cref="ServerUrl"/> after a load.
    /// </summary>
    public List<SavedNestServer> Servers { get; set; } = new();

    /// <summary>True when Database mode is active and a server URL is configured.</summary>
    public bool IsDatabaseMode =>
        Mode == NestStorageMode.Database && !string.IsNullOrWhiteSpace(ServerUrl);

    /// <summary>The saved server at this address, or null. Addresses differing only by case or a trailing slash match.</summary>
    public SavedNestServer? FindServer(string? url)
    {
        var key = NormalizeUrl(url);
        return Servers.Find(s => string.Equals(s.Url, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds the server to the saved list, or renames it when already there. A null name keeps
    /// the saved one; an invalid address is ignored.
    /// </summary>
    public void RememberServer(string? url, string? name)
    {
        if (!TryNormalizeServerUrl(url, out var normalized))
            return;

        var index = Servers.FindIndex(s => string.Equals(s.Url, normalized, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            Servers.Add(new SavedNestServer((name ?? "").Trim(), normalized));
        else if (name != null)
            Servers[index] = Servers[index] with { Name = name.Trim() };
    }

    /// <summary>Removes the server from the saved list; true when it was there.</summary>
    public bool ForgetServer(string? url)
    {
        var saved = FindServer(url);
        return saved != null && Servers.Remove(saved);
    }

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
        var settings = SettingsFile.Read<NestStorageSettings>(path, JsonOptions) ?? new();
        settings.ServerUrl = NormalizeUrl(settings.ServerUrl);
        settings.Servers = CleanServers(settings.Servers);
        settings.RememberServer(settings.ServerUrl, null);
        return settings;
    }

    /// <summary>Writes camelCase JSON, creating the parent directory and retrying IO collisions.</summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(
            new NestStorageSettings
            {
                Mode = Mode,
                ServerUrl = NormalizeUrl(ServerUrl),
                Servers = CleanServers(Servers),
            },
            JsonOptions);
        SettingsFile.Write(path, json);
    }

    /// <summary>Normalized entries in their saved order, without invalid addresses or repeats.</summary>
    private static List<SavedNestServer> CleanServers(List<SavedNestServer>? servers)
    {
        var clean = new NestStorageSettings();
        foreach (var server in servers ?? new())
        {
            if (server != null && clean.FindServer(server.Url) == null)
                clean.RememberServer(server.Url, server.Name ?? "");
        }

        return clean.Servers;
    }

    private static string NormalizeUrl(string? url) => (url ?? "").Trim().TrimEnd('/');
}

/// <summary>A nest server the operator has used, with their note on what it is for.</summary>
public sealed record SavedNestServer(string Name, string Url)
{
    /// <summary>"Name — address", or the address alone when the server has no name.</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Url : $"{Name} — {Url}";
}
