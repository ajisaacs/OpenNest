using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Data;

namespace OpenNest.Server.Tests;

/// <summary>Neutral, code-built drawing library fixtures for the server tests.</summary>
internal static class DrawingLibraryFixtures
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public static DrawingLibraryRecord Record(string name = "Synthetic bracket", Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Customer = "Synthetic customer",
        MaterialName = "Synthetic Steel",
        MaterialGrade = "Grade A",
        Thickness = 0.25,
        Units = Units.Inches,
        CutType = CutType.Laser,
        Notes = "Synthetic notes",
        SavedBy = "Synthetic operator",
    };

    /// <summary>A real ZIP archive whose single entry holds <paramref name="content"/>.</summary>
    public static byte[] Package(string content = "synthetic")
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("nest.json");
            entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
        return buffer.ToArray();
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static MultipartFormDataContent Body(string metadataJson, byte[]? package)
    {
        var body = new MultipartFormDataContent();
        body.Add(new StringContent(metadataJson), "metadata");
        if (package is not null)
            body.Add(new ByteArrayContent(package), "package", "drawing.nest");
        return body;
    }

    public static MultipartFormDataContent Body(DrawingLibraryRecord record, byte[]? package) =>
        Body(JsonSerializer.Serialize(record, Options), package);

    public static async Task<DrawingLibraryRecord> Upload(HttpClient client, DrawingLibraryRecord record, byte[] package)
    {
        using var response = await client.PostAsync("/api/drawings", Body(record, package));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DrawingLibraryRecord>(Options))!;
    }

    public static HttpRequestMessage Guarded(HttpMethod method, string url, string? ifMatch, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }
}
