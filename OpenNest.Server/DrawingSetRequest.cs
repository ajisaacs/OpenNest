using System.Text.Json;
using System.Text.Json.Nodes;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Parses drawing set requests with the drawing library's strictness: unknown, repeated
/// or out-of-range values are rejected rather than ignored or clamped, and sort accepts
/// names only.
/// </summary>
internal static class DrawingSetRequest
{
    private static readonly string[] QueryKeys =
    {
        "search", "customer", "includeArchived", "sort", "order", "offset", "limit",
    };

    private static readonly JsonSerializerOptions SetOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryParseQuery(IQueryCollection values, out DrawingSetQuery query, out string error)
    {
        query = new DrawingSetQuery();
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

        var sort = DrawingSetSortField.SavedAt;
        if (values.TryGetValue("sort", out var sortText) && !DrawingLibraryRequest.TryEnumName(sortText.ToString(), out sort))
        {
            error = $"sort must be one of: {string.Join(", ", Enum.GetNames<DrawingSetSortField>().Select(DrawingLibraryRequest.CamelCase))}.";
            return false;
        }

        var descending = true;
        if (values.TryGetValue("order", out var orderText))
        {
            if (string.Equals(orderText, "asc", StringComparison.OrdinalIgnoreCase))
            {
                descending = false;
            }
            else if (!string.Equals(orderText, "desc", StringComparison.OrdinalIgnoreCase))
            {
                error = "order must be asc or desc.";
                return false;
            }
        }

        var includeArchived = false;
        if (values.TryGetValue("includeArchived", out var archivedText) && !bool.TryParse(archivedText.ToString(), out includeArchived))
        {
            error = "includeArchived must be true or false.";
            return false;
        }

        var offset = 0;
        var limit = DrawingSetQuery.DefaultLimit;
        if (!DrawingLibraryRequest.TryInteger(values, "offset", ref offset, out error)
            || !DrawingLibraryRequest.TryInteger(values, "limit", ref limit, out error))
            return false;

        query = new DrawingSetQuery
        {
            Search = values["search"].ToString(),
            Customer = values["customer"].ToString(),
            IncludeArchived = includeArchived,
            Sort = sort,
            Descending = descending,
            Offset = offset,
            Limit = limit,
        };
        error = query.GetValidationError() ?? "";
        return error.Length == 0;
    }

    /// <summary>
    /// Reads and validates one set JSON object. <c>members</c> must be present; each
    /// member's joined <c>drawing</c> is ignored, so a record read from the server may be
    /// sent back as it is.
    /// </summary>
    public static bool TryReadSet(string? json, out DrawingSetRecord record, out string error)
    {
        record = new DrawingSetRecord();
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Missing set.";
            return false;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                error = "The set must be a JSON object.";
                return false;
            }

            if (!RemoveJoined(root, "members", "drawing"))
            {
                error = "members is required.";
                return false;
            }

            record = root.Deserialize<DrawingSetRecord>(SetOptions) ?? record;
        }
        catch (JsonException)
        {
            error = "Invalid set JSON.";
            return false;
        }

        error = DrawingSetRules.GetError(record) ?? "";
        return error.Length == 0;
    }

    internal static JsonNode? Property(JsonObject node, string name) =>
        node.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    internal static bool IsName<TEnum>(JsonNode value)
        where TEnum : struct, Enum =>
        value is JsonValue text && text.TryGetValue<string>(out var name) && DrawingLibraryRequest.TryEnumName<TEnum>(name, out _);

    // Drops the server-joined property from each object in the list, so a record read from
    // the server may be sent back; false when the list property is missing altogether.
    internal static bool RemoveJoined(JsonObject root, string list, string joined)
    {
        if (!root.Any(p => string.Equals(p.Key, list, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (Property(root, list) is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                foreach (var key in item.Select(p => p.Key)
                             .Where(k => string.Equals(k, joined, StringComparison.OrdinalIgnoreCase)).ToList())
                    item.Remove(key);
            }
        }

        return true;
    }
}
