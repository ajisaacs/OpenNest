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
    private static readonly string[] Keys = { "search", "offset", "limit" };

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

        var offset = 0;
        var limit = NestQuery.DefaultLimit;
        if (!TryInteger(values, "offset", ref offset, out error) || !TryInteger(values, "limit", ref limit, out error))
            return false;

        query = new NestQuery { Search = values["search"].ToString(), Offset = offset, Limit = limit };
        error = query.GetValidationError() ?? "";
        return error.Length == 0;
    }

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
