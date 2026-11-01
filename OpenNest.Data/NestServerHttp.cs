using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNest.Data;

/// <summary>Wire conventions shared by the nest server's HTTP clients.</summary>
internal static class NestServerHttp
{
    /// <summary>The server's JSON shape: camel-case names and enum names.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Guards a write on the revision the caller last read.</summary>
    public static void AddIfMatch(HttpRequestMessage request, int expectedRevision) =>
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expectedRevision.ToString(CultureInfo.InvariantCulture)}\""));

    public static void ValidatePaging(int offset, int limit, int maxLimit)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 1 || limit > maxLimit) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    public static string Paging(int offset, int limit) =>
        $"offset={offset.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Whether a page is the one asked for and holds no more than its window and total allow.</summary>
    public static bool IsBoundedPage(int count, int total, int pageOffset, int pageLimit, int offset, int limit) =>
        count <= limit && pageOffset == offset && pageLimit == limit && total >= 0
        && count <= System.Math.Max(0L, (long)total - offset);

    /// <summary>Whether a history page holds only revisions of <paramref name="id"/>, newest first.</summary>
    public static bool IsRevisionHistory<T>(IReadOnlyList<T> items, Guid id, Func<T, Guid> idOf, Func<T, int> revisionOf)
        where T : class =>
        !items.Any(item => item is null || revisionOf(item) < 1 || idOf(item) != id)
        && !items.Zip(items.Skip(1)).Any(pair => revisionOf(pair.First) <= revisionOf(pair.Second));

    /// <summary>Throws an <see cref="IOException"/> naming the action, status and the start of the body.</summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = "";
        try
        {
            detail = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The status code is the useful part; the body is best-effort.
        }

        if (detail.Length > 200)
            detail = detail[..200] + "…";
        throw new IOException(
            $"Could not {action} on the nest server: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {detail}".TrimEnd());
    }
}
