namespace OpenNest.Data;

/// <summary>Base-address validation shared by the nest server's HTTP clients.</summary>
internal static class ServerUrl
{
    /// <summary>
    /// Returns the absolute http(s) base address with exactly one trailing slash, so
    /// relative combines replace no path segment. Throws for any other address.
    /// </summary>
    public static Uri ParseBase(string? baseUrl, string paramName)
    {
        var url = (baseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Invalid nest server URL: '{baseUrl}'", paramName);
        }

        return new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
