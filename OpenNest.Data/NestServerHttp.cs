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
