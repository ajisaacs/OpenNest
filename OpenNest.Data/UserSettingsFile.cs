using System.Text.Json;

namespace OpenNest.Data;

/// <summary>
/// The desktop apps' per-user settings file, %APPDATA%\OpenNest\user-settings.json: one JSON
/// object of setting names to their text values. The WinForms app writes it through
/// <c>AppDataSettingsProvider</c> (values in System.Configuration's invariant text form, e.g.
/// "1936, 1048" for a size); the Avalonia app reads and writes the same names, so both apps
/// share one file on Windows.
/// </summary>
public static class UserSettingsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>%APPDATA%\OpenNest\user-settings.json (~/.config/OpenNest on Linux).</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenNest", "user-settings.json");

    /// <summary>
    /// The stored values. A missing, unreadable or corrupt file reads as empty, and an entry
    /// whose value is not text is skipped; reading never changes the file.
    /// </summary>
    public static Dictionary<string, string> Read(string path)
    {
        var values = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return values;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return values;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    values[property.Name] = property.Value.GetString()!;
            }
        }
        catch (Exception ex) when (SettingsFile.IsUnreadableFile(ex))
        {
            values.Clear();
        }

        return values;
    }

    /// <summary>
    /// Replaces the file with <paramref name="values"/> through a temporary file. Settings are a
    /// convenience: a failed write returns false and leaves the previous file in place.
    /// </summary>
    public static bool Write(string path, IReadOnlyDictionary<string, string> values)
    {
        try
        {
            SettingsFile.Write(path, JsonSerializer.Serialize(values, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Re-reads the file, applies <paramref name="change"/> and writes it back, so entries this
    /// build does not know (from a newer copy, or the other desktop app) are kept.
    /// </summary>
    public static bool Update(string path, Action<Dictionary<string, string>> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var values = Read(path);
        change(values);
        return Write(path, values);
    }
}
