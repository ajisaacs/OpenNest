using System.Text.Json;

namespace OpenNest.Data;

/// <summary>
/// One settings document every workstation shares through the nest server
/// (<c>/api/settings</c>): a machine configuration, a post's settings, the clamp rail or the
/// nest defaults. <see cref="Json"/> is the document exactly as its owner serializes it;
/// <see cref="Revision"/> guards every change.
/// </summary>
public sealed class SharedSettingRecord
{
    public string Kind { get; set; } = "";

    public string Key { get; set; } = "";

    public int Revision { get; set; }

    public string Json { get; set; } = "";

    public DateTime SavedAt { get; set; }

    public string SavedBy { get; set; } = "";
}

/// <summary>The body of a shared settings create or update.</summary>
public sealed class SharedSettingWrite
{
    public string Json { get; set; } = "";

    public string SavedBy { get; set; } = "";
}

/// <summary>The kinds of shared settings and the keys of the single-document kinds.</summary>
public static class SharedSettingKinds
{
    /// <summary>A <see cref="MachineConfig"/>, keyed by its id.</summary>
    public const string Machine = "machine";

    /// <summary>A post processor's settings, keyed by the post's type name.</summary>
    public const string Post = "post";

    /// <summary>
    /// The legacy <see cref="ClampSettings"/> new nests started from before each machine held its
    /// clamps, under <see cref="DefaultKey"/>; read only to copy them onto a machine once.
    /// </summary>
    public const string Clamps = "clamps";

    /// <summary>The <see cref="NestDefaults"/> new nests start from, under <see cref="DefaultKey"/>.</summary>
    public const string NestDefaults = "nest-defaults";

    /// <summary>The key of the clamp settings and nest defaults documents.</summary>
    public const string DefaultKey = "default";

    public static IReadOnlyList<string> All { get; } = [Machine, Post, Clamps, NestDefaults];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}

/// <summary>Validation shared by the nest server and its clients.</summary>
public static class SharedSettingRules
{
    public const int MaxKeyLength = 200;

    public const int MaxSavedByLength = 200;

    /// <summary>
    /// A settings document's limit in characters: far above the largest post configuration, and
    /// small enough that the escaped document fits the server's request limit.
    /// </summary>
    public const int MaxJsonLength = 512 * 1024;

    /// <summary>
    /// The key as stored: a machine id in lower-case "D" form, any other key as given. Null when
    /// the key is not valid for the kind.
    /// </summary>
    public static string? NormalizeKey(string? kind, string? key)
    {
        if (!SharedSettingKinds.IsKnown(kind) || string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
            return null;
        if (kind == SharedSettingKinds.Machine)
            return Guid.TryParse(key, out var id) && id != Guid.Empty ? id.ToString("D") : null;
        if (kind is SharedSettingKinds.Clamps or SharedSettingKinds.NestDefaults)
            return key == SharedSettingKinds.DefaultKey ? key : null;
        return key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+') ? key : null;
    }

    /// <summary>Why the document cannot be stored under the kind and key, or null when it can.</summary>
    public static string? GetError(string? kind, string? key, SharedSettingWrite? write)
    {
        if (!SharedSettingKinds.IsKnown(kind))
            return $"Unknown settings kind '{kind}'.";
        var normalized = NormalizeKey(kind, key);
        if (normalized is null)
            return $"Invalid {kind} settings key.";
        if (write is null || string.IsNullOrEmpty(write.Json))
            return "The settings document is required.";
        if (write.Json.Length > MaxJsonLength)
            return "The settings document is too large.";
        if ((write.SavedBy ?? "").Length > MaxSavedByLength)
            return "savedBy is too long.";

        try
        {
            using var document = JsonDocument.Parse(write.Json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return "The settings document must be a JSON object.";
        }
        catch (JsonException)
        {
            return "The settings document is not valid JSON.";
        }

        if (kind == SharedSettingKinds.Machine)
        {
            MachineConfig? machine;
            try { machine = JsonSerializer.Deserialize<MachineConfig>(write.Json, LocalJsonProvider.JsonOptions); }
            catch (JsonException) { return "The machine configuration is not valid."; }
            if (machine is null || machine.Id.ToString("D") != normalized)
                return "The machine configuration's id does not match its key.";
        }
        return null;
    }

    /// <summary>
    /// Why <paramref name="json"/> may not replace the stored <paramref name="storedJson"/>, or
    /// null when it may. A machine saved by an OpenNest too old to know a setting would drop it:
    /// a field the stored machine, one of its materials (matched by name and grade) or one of
    /// their thicknesses (matched by value) holds a value for, and that the new document leaves
    /// out entirely, refuses the save. Current builds keep fields they do not know, and write
    /// cleared settings as null, so they never trip this.
    /// </summary>
    public static string? GetReplaceError(string? kind, string storedJson, string json)
    {
        if (kind != SharedSettingKinds.Machine)
            return null;
        try
        {
            using var stored = JsonDocument.Parse(storedJson);
            using var incoming = JsonDocument.Parse(json);
            var dropped = DroppedField(stored.RootElement, incoming.RootElement, "");
            return dropped is null
                ? null
                : $"This machine has settings ({dropped}) that this version of OpenNest does not know, "
                    + "so saving here would erase them. Update OpenNest to change this machine.";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The first field with a value in stored that incoming leaves out, as a path; materials and
    // thicknesses are matched by identity, so a reorder or a removed entry is not a drop.
    private static string? DroppedField(JsonElement stored, JsonElement incoming, string path)
    {
        if (stored.ValueKind != JsonValueKind.Object || incoming.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var field in stored.EnumerateObject())
        {
            if (field.Value.ValueKind == JsonValueKind.Null)
                continue;
            var name = path.Length == 0 ? field.Name : $"{path}.{field.Name}";
            if (!TryGetProperty(incoming, field.Name, out var match))
                return name;
            var nested = field.Name.ToLowerInvariant() switch
            {
                "materials" => DroppedInList(field.Value, match, name, Material),
                "thicknesses" => DroppedInList(field.Value, match, name, Thickness),
                _ => path.Length > 0 && path.EndsWith("]", StringComparison.Ordinal)
                    ? DroppedField(field.Value, match, name)
                    : null,
            };
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static string? DroppedInList(JsonElement stored, JsonElement incoming, string path, Func<JsonElement, string?> identity)
    {
        if (stored.ValueKind != JsonValueKind.Array || incoming.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in stored.EnumerateArray())
        {
            var id = identity(item);
            if (id == null)
                continue;
            foreach (var candidate in incoming.EnumerateArray())
            {
                if (identity(candidate) != id)
                    continue;
                if (DroppedField(item, candidate, $"{path}[{id}]") is { } dropped)
                    return dropped;
                break;
            }
        }
        return null;
    }

    private static string? Material(JsonElement material) =>
        material.ValueKind == JsonValueKind.Object
            ? $"{Text(material, "name")}/{Text(material, "grade")}"
            : null;

    private static string? Thickness(JsonElement thickness) =>
        thickness.ValueKind == JsonValueKind.Object && TryGetProperty(thickness, "value", out var value)
            && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static string Text(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? SharedListNames.Key(value.GetString())
            : "";

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
