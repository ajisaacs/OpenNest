using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenNest;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Parses release requests with the drawing library's strictness: unknown or repeated query
/// parameters and enum numbers or combinations are rejected, and server-computed or joined
/// properties in a posted release are ignored, so a record read from the server may be sent back.
/// </summary>
internal static class ReleaseRequest
{
    private static readonly string[] QueryKeys = { "setId", "nestId", "drawingId", "offset", "limit" };

    // Integer enum values are refused; names are additionally checked before deserializing.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static bool TryParseQuery(IQueryCollection values, out Guid? setId, out Guid? nestId,
        out Guid? drawingId, out int offset, out int limit, out string error)
    {
        setId = null;
        nestId = null;
        drawingId = null;
        offset = 0;
        limit = DrawingLibraryRules.DefaultRevisionLimit;
        foreach (var key in values.Keys)
        {
            if (!QueryKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                error = $"Unknown query parameter '{key}'.";
                return false;
            }

            if (values[key].Count > 1)
            {
                error = $"Query parameter '{key}' was given more than once.";
                return false;
            }
        }

        if (!TryId(values, "setId", out setId, out error) || !TryId(values, "nestId", out nestId, out error)
            || !TryId(values, "drawingId", out drawingId, out error)
            || !DrawingLibraryRequest.TryInteger(values, "offset", ref offset, out error)
            || !DrawingLibraryRequest.TryInteger(values, "limit", ref limit, out error))
            return false;

        if (offset < 0 || limit < 1 || limit > DrawingLibraryRules.MaxRevisionLimit)
        {
            error = $"offset must be zero or more and limit between 1 and {DrawingLibraryRules.MaxRevisionLimit}.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads and validates one release JSON object. <c>sets</c> and <c>nests</c> must be
    /// present, <c>drawings</c> may be left out (clients from before drawings on their own),
    /// enums must be single names, and joined or server-computed values are dropped.
    /// </summary>
    public static bool TryReadRelease(string? json, out SetReleaseRecord release, out string error)
    {
        release = new SetReleaseRecord();
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Missing release.";
            return false;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                error = "The release must be a JSON object.";
                return false;
            }

            if (!DrawingSetRequest.RemoveJoined(root, "sets", "setName"))
            {
                error = "sets is required.";
                return false;
            }

            DrawingSetRequest.RemoveJoined(root, "drawings", "drawingName");
            if (!DrawingSetRequest.RemoveJoined(root, "nests", "nest"))
            {
                error = "nests is required.";
                return false;
            }

            Remove(root, "releasedAt", "undoneAt", "undoneBy");
            if (DrawingSetRequest.Property(root, "nests") is JsonArray nests)
            {
                for (var i = 0; i < nests.Count; i++)
                {
                    if (nests[i] is not JsonObject nest)
                        continue;
                    Remove(nest, "undo", "undoRevision");
                    error = NameError<Units>(nest, "units", $"nests[{i}].units must be a supported unit name.")
                        ?? NameError<CutType>(nest, "cutType", $"nests[{i}].cutType must be Laser, Waterjet or Plasma.")
                        ?? NameError<SetReleaseNestKind>(nest, "kind", $"nests[{i}].kind must be created or merged.")
                        ?? "";
                    if (error.Length > 0)
                        return false;
                }
            }

            release = root.Deserialize<SetReleaseRecord>(Options) ?? release;
        }
        catch (JsonException)
        {
            error = "Invalid release JSON.";
            return false;
        }

        error = SetReleaseRules.GetError(release) ?? "";
        return error.Length == 0;
    }

    public static bool TryReadUndo(string? json, out SetReleaseUndoRequest request, out string error)
    {
        request = new SetReleaseUndoRequest();
        try
        {
            if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject root)
            {
                error = "The undo request must be a JSON object.";
                return false;
            }

            request = root.Deserialize<SetReleaseUndoRequest>(Options) ?? request;
        }
        catch (JsonException)
        {
            error = "Invalid undo JSON.";
            return false;
        }

        error = SetReleaseRules.GetUndoError(request) ?? "";
        return error.Length == 0;
    }

    private static bool TryId(IQueryCollection values, string key, out Guid? id, out string error)
    {
        id = null;
        error = "";
        if (!values.TryGetValue(key, out var text))
            return true;
        if (!Guid.TryParse(text.ToString(), out var parsed) || parsed == Guid.Empty)
        {
            error = $"{key} must be an id.";
            return false;
        }

        id = parsed;
        return true;
    }

    private static string? NameError<TEnum>(JsonObject node, string property, string error)
        where TEnum : struct, Enum =>
        DrawingSetRequest.Property(node, property) is { } value && !DrawingSetRequest.IsName<TEnum>(value) ? error : null;

    private static void Remove(JsonObject node, params string[] names)
    {
        foreach (var key in node.Select(p => p.Key)
                     .Where(k => names.Any(n => string.Equals(k, n, StringComparison.OrdinalIgnoreCase))).ToList())
            node.Remove(key);
    }
}
