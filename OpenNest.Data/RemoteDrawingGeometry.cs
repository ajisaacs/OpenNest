using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace OpenNest.Data;

/// <summary>
/// The server worker's side of library measurement (<c>/api/drawing-packages</c>): lists the
/// packages of current drawings not yet measured at a version, downloads one by its hash and
/// stores its <see cref="DrawingGeometrySummary"/>. A server without these routes raises
/// <see cref="ServerTooOldException"/>; other failures surface as <see cref="IOException"/>.
/// </summary>
public sealed partial class RemoteDrawingGeometry : NestServerClient
{
    public const int MaxBatch = 500;

    public RemoteDrawingGeometry(string baseUrl)
        : base(new HttpClient(), baseUrl, ownsClient: true)
    {
    }

    public RemoteDrawingGeometry(HttpClient httpClient, string baseUrl)
        : base(httpClient, baseUrl, ownsClient: false)
    {
    }

    /// <summary>Up to <paramref name="limit"/> package hashes with no summary at <paramref name="version"/> or later.</summary>
    public async Task<IReadOnlyList<string>> GetUnmeasuredAsync(int version, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxBatch);
        using var response = await _httpClient.GetAsync(Url(
            $"api/drawing-packages/unmeasured?version={version.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}"),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ServerTooOldException();
        await NestServerHttp.EnsureSuccessAsync(response, "list unmeasured library drawings", cancellationToken);
        var hashes = await response.Content.ReadFromJsonAsync<List<string>>(NestServerHttp.JsonOptions, cancellationToken)
            ?? throw new IOException("Nest server returned an empty list of unmeasured drawings.");
        if (hashes.Count > limit || hashes.Any(hash => hash is null || !IsSha256(hash)))
            throw new IOException("Nest server returned an invalid list of unmeasured drawings.");
        return hashes;
    }

    /// <summary>The package stored under <paramref name="sha256"/>, or null when there is none.</summary>
    public async Task<byte[]?> GetPackageAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ValidateSha(sha256);
        using var response = await _httpClient.GetAsync(Url($"api/drawing-packages/{sha256}"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await NestServerHttp.EnsureSuccessAsync(response, $"download library package {sha256}", cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>Stores a package's summary; false when the server no longer has the package.</summary>
    public async Task<bool> PutAsync(string sha256, DrawingGeometrySummary summary, CancellationToken cancellationToken = default)
    {
        ValidateSha(sha256);
        ArgumentNullException.ThrowIfNull(summary);
        var error = summary.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(summary));
        using var response = await _httpClient.PutAsJsonAsync(
            Url($"api/drawing-packages/{sha256}/geometry"), summary, NestServerHttp.JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        await NestServerHttp.EnsureSuccessAsync(response, $"store the size of library package {sha256}", cancellationToken);
        return true;
    }

    /// <summary>True for a lower-case hex SHA-256, the form the server stores.</summary>
    public static bool IsSha256(string text) => Sha256Pattern().IsMatch(text);

    private static void ValidateSha(string sha256)
    {
        if (sha256 is null || !IsSha256(sha256))
            throw new ArgumentException("A package hash is 64 lower-case hex characters.", nameof(sha256));
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();
}
