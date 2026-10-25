using System.Text.Json;

namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// The MaterialLedger address and API key for this PC, stored at
/// %APPDATA%\OpenNest\material-ledger.json. The key is written only in the protected form the
/// caller's protector produces (DPAPI on the desktop), never in plain text. Loading never
/// throws; a missing, corrupt or unreadable file means not configured.
/// </summary>
public sealed class MaterialLedgerSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Base URL of MaterialLedger, e.g. http://ledger:5000. Empty means not configured.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The plain API key in memory (ML key name <c>Api:Keys:OpenNest</c>).</summary>
    public string ApiKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>%APPDATA%\OpenNest\material-ledger.json.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenNest", "material-ledger.json");

    /// <summary>
    /// Reads the settings, unprotecting the stored key with <paramref name="unprotect"/>. A key
    /// that cannot be unprotected (another user or PC) loads as empty so the ledger reports
    /// unauthorized instead of failing to start.
    /// </summary>
    public static MaterialLedgerSettings Load(string path, Func<string, string> unprotect)
    {
        ArgumentNullException.ThrowIfNull(unprotect);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new();

        try
        {
            var file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(path), JsonOptions);
            if (file == null)
                return new();
            var settings = new MaterialLedgerSettings { BaseUrl = NormalizeUrl(file.BaseUrl) };
            if (!string.IsNullOrEmpty(file.ProtectedApiKey))
            {
                try
                {
                    settings.ApiKey = unprotect(file.ProtectedApiKey);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    settings.ApiKey = "";
                }
            }
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>Writes camelCase JSON with the key protected by <paramref name="protect"/>.</summary>
    public void Save(string path, Func<string, string> protect)
    {
        ArgumentNullException.ThrowIfNull(protect);
        var json = JsonSerializer.Serialize(new SettingsFile
        {
            BaseUrl = NormalizeUrl(BaseUrl),
            ProtectedApiKey = string.IsNullOrEmpty(ApiKey) ? "" : protect(ApiKey),
        }, JsonOptions);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, json);
    }

    /// <summary>Validate and normalize a ledger base URL; blank is valid and means not configured.</summary>
    public static bool TryNormalizeUrl(string? url, out string normalized)
    {
        normalized = NormalizeUrl(url);
        if (normalized.Length == 0)
            return true;
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return true;

        normalized = "";
        return false;
    }

    private static string NormalizeUrl(string? url) => (url ?? "").Trim().TrimEnd('/');

    private sealed class SettingsFile
    {
        public string? BaseUrl { get; set; }
        public string? ProtectedApiKey { get; set; }
    }
}
