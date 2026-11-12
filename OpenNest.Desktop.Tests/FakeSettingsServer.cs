using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Data;

namespace OpenNest.Desktop.Tests;

/// <summary>
/// A nest server's /api/settings in memory: lists, creates and replaces documents, or refuses
/// every request as an unreachable server while <see cref="Online"/> is false.
/// </summary>
internal sealed class FakeSettingsServer : HttpMessageHandler
{
    public const string Url = "http://settings.test/";

    // The server's JSON shape: camel-case names and enum names.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public List<SharedSettingRecord> Records { get; } = [];

    public bool Online { get; set; } = true;

    public int Reads { get; private set; }

    public void Hold(string kind, string key, string json) =>
        Records.Add(new SharedSettingRecord { Kind = kind, Key = key, Json = json, Revision = 1, SavedAt = DateTime.Now });

    /// <summary>A source in Database mode on this server, keeping its last copy in <paramref name="cachePath"/>.</summary>
    public SharedSettingsSource Source(string cachePath) =>
        new(() => new NestStorageSettings { Mode = NestStorageMode.Database, ServerUrl = Url },
            url => new SharedSettingsStore(new RemoteSharedSettings(new HttpClient(this, disposeHandler: false), url), cachePath, "tester"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!Online)
            throw new HttpRequestException("No route to the nest server.");

        var path = request.RequestUri!.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (request.Method == HttpMethod.Get && path is ["api", "settings"])
        {
            Reads++;
            return JsonResponse(HttpStatusCode.OK, Records);
        }
        if (path is ["api", "settings", var kind, var key] && (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put))
        {
            var write = await request.Content!.ReadFromJsonAsync<SharedSettingWrite>(JsonOptions, cancellationToken);
            var current = Records.FirstOrDefault(r => r.Kind == kind && r.Key == key);
            var saved = new SharedSettingRecord
            {
                Kind = kind,
                Key = key,
                Json = write!.Json,
                Revision = (current?.Revision ?? 0) + 1,
                SavedAt = DateTime.Now,
                SavedBy = write.SavedBy,
            };
            if (current != null)
                Records.Remove(current);
            Records.Add(saved);
            return JsonResponse(current == null ? HttpStatusCode.Created : HttpStatusCode.OK, saved);
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body) =>
        new(status) { Content = JsonContent.Create(body, options: JsonOptions) };
}

/// <summary>The two machine user settings in memory.</summary>
internal sealed class MemoryUserSettings : IMachineUserSettings
{
    public Units DefaultUnit { get; set; } = Units.Inches;

    public string CuttingParametersJson { get; set; } = "";

    public int Saves { get; private set; }

    string IMachineUserSettings.CuttingParametersJson
    {
        get => CuttingParametersJson;
        set
        {
            CuttingParametersJson = value;
            Saves++;
        }
    }
}
