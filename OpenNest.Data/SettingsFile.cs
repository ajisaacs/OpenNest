using System.Text.Json;

namespace OpenNest.Data;

/// <summary>File handling shared by the JSON settings files under %APPDATA%\OpenNest.</summary>
internal static class SettingsFile
{
    /// <summary>
    /// Writes the file, creating the parent directory, and retries briefly on IO collisions.
    /// The last failure propagates; the host handles a persistent write failure.
    /// </summary>
    public static void Write(string path, string json, int maxRetries = 3)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(path, json);
                return;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Deserializes the file, or returns null when it is missing, unreadable or corrupt.
    /// </summary>
    public static T? Read<T>(string path, JsonSerializerOptions options)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
        }
        catch (Exception ex) when (IsUnreadableFile(ex))
        {
            return null;
        }
    }

    public static bool IsUnreadableFile(Exception ex) =>
        ex
            is JsonException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException;

    /// <summary>
    /// Accepts only a defined unit name (case-insensitive). Enum.TryParse would also accept
    /// numeric strings ("7") and comma-joined names, producing undefined or unintended values.
    /// </summary>
    public static bool TryParseUnits(JsonElement? element, out Units units)
    {
        units = default;
        if (element is not { ValueKind: JsonValueKind.String } value)
            return false;

        var text = value.GetString();
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
}
