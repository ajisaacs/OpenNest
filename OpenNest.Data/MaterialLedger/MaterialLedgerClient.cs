using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// Read-only client for MaterialLedger's API. It never writes to the ledger, and what it
/// reads is a snapshot, not a reservation. Expected failures come back as result cases
/// (<see cref="LedgerStockResult"/>, <see cref="LedgerHoldsResult"/>); only the caller's own
/// cancellation throws.
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

        var query = "api/stock/opennest"
            + "?material=" + Uri.EscapeDataString(material ?? "")
            + "&grade=" + Uri.EscapeDataString(grade ?? "")
            + "&thicknessIn=" + System.Math.Round(thicknessIn, 4).ToString("0.####", CultureInfo.InvariantCulture);

        var reply = await GetJsonAsync<LedgerStockResponse>(query, "stock", cancellationToken);
        if (reply.Unauthorized)
            return new LedgerStockResult.Unauthorized();
        if (reply.Body is not { } stock)
            return new LedgerStockResult.Unreachable(reply.Failure!);
        if (!stock.Mapped)
            return new LedgerStockResult.Unmapped();
        return new LedgerStockResult.Ok(stock.Specs ?? Array.Empty<LedgerStockSpec>(),
            stock.Items ?? Array.Empty<LedgerStockItem>());
    }

    /// <summary>
    /// Every hold ML has for the sheets of server nest <paramref name="openNestNestId"/>, in
    /// whatever status; the caller decides which are open.
    /// </summary>
    public async Task<LedgerHoldsResult> GetHoldsAsync(Guid openNestNestId, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
            return new LedgerHoldsResult.NotConfigured();

        var query = "api/stock/holds?openNestNestId=" + openNestNestId.ToString("D");
        var reply = await GetJsonAsync<LedgerHoldsResponse>(query, "holds", cancellationToken);
        if (reply.Unauthorized)
            return new LedgerHoldsResult.Unauthorized();
        if (reply.Body is not { } holds)
            return new LedgerHoldsResult.Unreachable(reply.Failure!);
        return new LedgerHoldsResult.Ok((holds.Holds ?? Array.Empty<LedgerHold>())
            .Where(hold => hold != null)
            .Select(hold => hold with { ItemCode = hold.ItemCode ?? "", Status = hold.Status ?? "" })
            .ToList());
    }

    /// <summary>
    /// GETs <paramref name="pathAndQuery"/> below the ledger's base URL with the API key and the
    /// per-request timeout. <paramref name="route"/> names the route in the 404 message.
    /// </summary>
    private async Task<Reply<T>> GetJsonAsync<T>(string pathAndQuery, string route,
        CancellationToken cancellationToken) where T : class
    {
        if (!Uri.TryCreate(settings.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            return Reply<T>.Failed($"'{settings.BaseUrl}' is not an http or https address.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, pathAndQuery));
            if (!string.IsNullOrEmpty(settings.ApiKey))
                request.Headers.TryAddWithoutValidation(ApiKeyHeader, settings.ApiKey);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new Reply<T>(null, true, null);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Reply<T>.Failed($"This MaterialLedger has no OpenNest {route} route; update MaterialLedger.");
            if (!response.IsSuccessStatusCode)
                return Reply<T>.Failed($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());

            await using var body = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
            var value = await JsonSerializer.DeserializeAsync<T>(body, JsonOptions, timeoutSource.Token);
            return value == null
                ? Reply<T>.Failed("The ledger returned an empty response.")
                : new Reply<T>(value, false, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Reply<T>.Failed(
                $"No answer within {timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s.");
        }
        catch (HttpRequestException ex)
        {
            return Reply<T>.Failed(ex.Message);
        }
        catch (JsonException)
        {
            return Reply<T>.Failed("The ledger's response could not be read.");
        }
        catch (IOException ex)
        {
            return Reply<T>.Failed(ex.Message);
        }
    }

    /// <summary>A parsed body, a rejected key, or why the ledger was unreachable.</summary>
    private readonly record struct Reply<T>(T? Body, bool Unauthorized, string? Failure) where T : class
    {
        public static Reply<T> Failed(string reason) => new(null, false, reason);
    }
}
