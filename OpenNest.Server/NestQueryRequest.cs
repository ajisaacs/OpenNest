using System.Globalization;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Parses <c>GET /api/nests/query</c> parameters. Unknown, repeated, non-integer or
/// out-of-range values are rejected rather than ignored or clamped, so a request
/// can never silently receive a broader result than it asked for.
/// </summary>
internal static class NestQueryRequest
{
    private static readonly string[] Keys =
    {
        "search", "sort", "order", "offset", "limit", "status", "material", "materialGrade", "thickness",
    };

    public static bool TryParse(IQueryCollection values, out NestQuery query, out string error)
    {
        query = new NestQuery();
        foreach (var key in values.Keys)
        {
            if (!Keys.Contains(key, StringComparer.OrdinalIgnoreCase))
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

        var sort = NestSortField.SavedAt;
        if (values.TryGetValue("sort", out var sortText)
            && !DrawingLibraryRequest.TryEnumName(sortText.ToString(), out sort))
        {
            error = $"sort must be one of: {string.Join(", ", Enum.GetNames<NestSortField>().Select(DrawingLibraryRequest.CamelCase))}.";
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

        NestStatus? status = null;
        if (values.TryGetValue("status", out var statusText))
        {
            if (!DrawingLibraryRequest.TryEnumName<NestStatus>(statusText.ToString(), out var parsed))
            {
                error = "status must be draft, quote, toBeCut or hasBeenCut.";
                return false;
            }
            status = parsed;
        }

        double? thickness = null;
        if (values.TryGetValue("thickness", out var thicknessText))
        {
            if (!double.TryParse(thicknessText.ToString(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out var parsed))
            {
                error = "thickness must be a number, zero or greater.";
                return false;
            }
            thickness = parsed;
        }

        var offset = 0;
        var limit = NestQuery.DefaultLimit;
        if (!DrawingLibraryRequest.TryInteger(values, "offset", ref offset, out error)
            || !DrawingLibraryRequest.TryInteger(values, "limit", ref limit, out error))
            return false;

        query = new NestQuery
        {
            Search = values["search"].ToString(),
            Status = status,
            // Present but blank is a filter too: a blank grade asks for nests without one.
            Material = values.TryGetValue("material", out var material) ? material.ToString() : null,
            MaterialGrade = values.TryGetValue("materialGrade", out var grade) ? grade.ToString() : null,
            Thickness = thickness,
            Sort = sort,
            Descending = descending,
            Offset = offset,
            Limit = limit,
        };
        error = query.GetValidationError() ?? "";
        return error.Length == 0;
    }
}
