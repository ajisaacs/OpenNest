using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Parses drawing library requests. Unknown, repeated or out-of-range values are
/// rejected rather than ignored or clamped, and enum fields accept names only.
/// </summary>
internal static class DrawingLibraryRequest
{
    private static readonly string[] QueryKeys =
    {
        "search", "customer", "materialName", "materialGrade", "thickness", "cutType",
        "includeArchived", "sort", "order", "offset", "limit",
    };

    // Integer enum values are refused; names are additionally checked before deserializing.
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static bool TryParseQuery(IQueryCollection values, out DrawingLibraryQuery query, out string error)
    {
        query = new DrawingLibraryQuery();
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

        var sort = DrawingSortField.SavedAt;
        if (values.TryGetValue("sort", out var sortText) && !TryEnumName(sortText.ToString(), out sort))
        {
            error = $"sort must be one of: {string.Join(", ", Enum.GetNames<DrawingSortField>().Select(CamelCase))}.";
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

        CutType? cutType = null;
        if (values.TryGetValue("cutType", out var cutTypeText))
        {
            if (!TryEnumName<CutType>(cutTypeText.ToString(), out var parsed))
            {
                error = "cutType must be Laser, Waterjet or Plasma.";
                return false;
            }

            cutType = parsed;
        }

        double? thickness = null;
        if (values.TryGetValue("thickness", out var thicknessText))
        {
            if (!double.TryParse(thicknessText.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                || !double.IsFinite(parsed))
            {
                error = "thickness must be a finite number, zero or greater.";
                return false;
            }

            thickness = parsed;
        }

        var includeArchived = false;
        if (values.TryGetValue("includeArchived", out var archivedText) && !bool.TryParse(archivedText.ToString(), out includeArchived))
        {
            error = "includeArchived must be true or false.";
            return false;
        }

        var offset = 0;
        var limit = DrawingLibraryQuery.DefaultLimit;
        if (!TryInteger(values, "offset", ref offset, out error) || !TryInteger(values, "limit", ref limit, out error))
            return false;

        query = new DrawingLibraryQuery
        {
            Search = values["search"].ToString(),
            Customer = values["customer"].ToString(),
            MaterialName = values["materialName"].ToString(),
            MaterialGrade = values["materialGrade"].ToString(),
            Thickness = thickness,
            CutType = cutType,
            IncludeArchived = includeArchived,
            Sort = sort,
            Descending = descending,
            Offset = offset,
            Limit = limit,
        };
        error = query.GetValidationError() ?? "";
        return error.Length == 0;
    }

    /// <summary>Reads and validates one metadata JSON object.</summary>
    public static bool TryReadMetadata(string? json, out DrawingLibraryRecord record, out string error)
    {
        record = new DrawingLibraryRecord();
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Missing metadata.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Metadata must be a JSON object.";
                return false;
            }

            // Enum converters also accept comma-separated combinations; allow single names only.
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "cutType", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind != JsonValueKind.String || !TryEnumName<CutType>(property.Value.GetString()!, out _))
                    {
                        error = "cutType must be Laser, Waterjet or Plasma.";
                        return false;
                    }
                }
                else if (string.Equals(property.Name, "units", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind != JsonValueKind.String || !TryEnumName<Units>(property.Value.GetString()!, out _))
                    {
                        error = "units must be a supported unit name.";
                        return false;
                    }
                }
            }

            record = document.RootElement.Deserialize<DrawingLibraryRecord>(MetadataOptions) ?? record;
        }
        catch (JsonException)
        {
            error = "Invalid metadata JSON.";
            return false;
        }

        error = DrawingLibraryRules.GetMetadataError(record) ?? "";
        return error.Length == 0;
    }

    // Names only: Enum.TryParse would also accept numbers, combinations and undefined values.
    private static bool TryEnumName<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        return text.Length > 0
            && text.All(char.IsAsciiLetter)
            && Enum.TryParse(text, ignoreCase: true, out value)
            && Enum.IsDefined(value);
    }

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static bool TryInteger(IQueryCollection values, string key, ref int value, out string error)
    {
        error = "";
        if (!values.TryGetValue(key, out var text))
            return true;

        if (int.TryParse(text.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value))
            return true;

        error = $"{key} must be a non-negative integer.";
        return false;
    }
}
