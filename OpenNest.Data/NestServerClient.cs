namespace OpenNest.Data;

/// <summary>The connection every nest server client holds: its HTTP client and the server's base address.</summary>
public abstract class NestServerClient : IDisposable
{
    private protected readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly Uri _baseUri;

    private protected NestServerClient(HttpClient httpClient, string baseUrl, bool ownsClient)
    {
        _httpClient = httpClient;
        _ownsClient = ownsClient;
        _baseUri = ServerUrl.ParseBase(baseUrl, nameof(baseUrl));
    }

    private protected Uri Url(string relative) => new(_baseUri, relative);

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
