using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Reads and checks uploaded nest metadata. Text fields must be present and the status
/// must be a defined value, so invalid metadata is a 400 and never reaches storage.
/// </summary>
internal static class NestMetadata
{
    // Integer statuses stay accepted for existing callers; GetError refuses undefined ones.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static bool TryRead(string? json, out NestRecord record, out string error)
    {
        record = new NestRecord();
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Missing 'metadata' part.";
            return false;
        }

        try
        {
            if (JsonSerializer.Deserialize<NestRecord>(json, Options) is not { } parsed)
            {
                error = "Missing metadata.";
                return false;
            }

            record = parsed;
        }
        catch (JsonException)
        {
            error = "Invalid metadata JSON.";
            return false;
        }

        error = GetError(record) ?? "";
        return error.Length == 0;
    }

    public static string? GetError(NestRecord record)
    {
        if (record.Name is null) return "name must be a string.";
        if (record.Customer is null) return "customer must be a string.";
        if (record.Material is null) return "material must be a string.";
        if (record.Comments is null) return "comments must be a string.";
        if (record.MadeBy is null) return "madeBy must be a string.";
        if (!Enum.IsDefined(record.Status)) return "status must be quote, toBeCut or hasBeenCut.";
        if (!double.IsFinite(record.Thickness)) return "thickness must be a finite number.";
        return null;
    }
}
