using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// Read-only client for MaterialLedger's stock API. It never writes to the ledger, and what it
/// reads is a snapshot, not a reservation. Expected failures come back as
/// <see cref="LedgerStockResult"/> cases; only the caller's own cancellation throws.
/// </summary>
public sealed class MaterialLedgerClient
{
    /// <summary>Stock lists are larger than the nest-name lookup, so this waits longer than its 3 s.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public const string ApiKeyHeader = "X-Api-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient http;
    private readonly MaterialLedgerSettings settings;
    private readonly TimeSpan timeout;

    /// <param name="timeout">Per-request limit; <see cref="RequestTimeout"/> when null.</param>
    public MaterialLedgerClient(HttpClient http, MaterialLedgerSettings settings, TimeSpan? timeout = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.timeout = timeout ?? RequestTimeout;
    }

    /// <summary>
    /// The free sheets and drops for an OpenNest material. ML resolves the
    /// material/grade/thickness mapping itself; <paramref name="thicknessIn"/> is inches.
    /// </summary>
    public async Task<LedgerStockResult> GetStockAsync(string material, string? grade, double thicknessIn,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
            return new LedgerStockResult.NotConfigured();
        if (!Uri.TryCreate(settings.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            return new LedgerStockResult.Unreachable($"'{settings.BaseUrl}' is not an http or https address.");

        var query = "api/stock/opennest"
            + "?material=" + Uri.EscapeDataString(material ?? "")
            + "&grade=" + Uri.EscapeDataString(grade ?? "")
            + "&thicknessIn=" + System.Math.Round(thicknessIn, 4).ToString("0.####", CultureInfo.InvariantCulture);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, query));
            if (!string.IsNullOrEmpty(settings.ApiKey))
                request.Headers.TryAddWithoutValidation(ApiKeyHeader, settings.ApiKey);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new LedgerStockResult.Unauthorized();
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new LedgerStockResult.Unreachable("This MaterialLedger has no OpenNest stock route; update MaterialLedger.");
            if (!response.IsSuccessStatusCode)
                return new LedgerStockResult.Unreachable($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());

            await using var body = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
            var stock = await JsonSerializer.DeserializeAsync<LedgerStockResponse>(body, JsonOptions, timeoutSource.Token);
            if (stock == null)
                return new LedgerStockResult.Unreachable("The ledger returned an empty response.");
            if (!stock.Mapped)
                return new LedgerStockResult.Unmapped();
            return new LedgerStockResult.Ok(stock.Specs ?? Array.Empty<LedgerStockSpec>(),
                stock.Items ?? Array.Empty<LedgerStockItem>());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LedgerStockResult.Unreachable(
                $"No answer within {timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s.");
        }
        catch (HttpRequestException ex)
        {
            return new LedgerStockResult.Unreachable(ex.Message);
        }
        catch (JsonException)
        {
            return new LedgerStockResult.Unreachable("The ledger's response could not be read.");
        }
        catch (IOException ex)
        {
            return new LedgerStockResult.Unreachable(ex.Message);
        }
    }
}
