using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using OpenNest.IO;

namespace OpenNest.Api;

public class NestResponse
{
    public int SheetCount { get; init; }
    public double Utilization { get; init; }
    public TimeSpan CutTime { get; init; }
    public TimeSpan Elapsed { get; init; }
    public Nest Nest { get; init; }
    public NestRequest Request { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IncludeFields = true  // Required for OpenNest.Geometry.Size (public fields)
    };

    public async Task SaveAsync(string path)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        // Write request.json
        var requestEntry = zip.CreateEntry("request.json");
        await using (var stream = requestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(stream, Request, JsonOptions);
        }

        // Write response.json (metrics only)
        var metrics = new
        {
            SheetCount,
            Utilization,
            CutTimeTicks = CutTime.Ticks,
            ElapsedTicks = Elapsed.Ticks
        };
        var responseEntry = zip.CreateEntry("response.json");
        await using (var stream = responseEntry.Open())
        {
            await JsonSerializer.SerializeAsync(stream, metrics, JsonOptions);
        }

        // Write embedded nest.nest via NestWriter → MemoryStream → ZIP entry
        var nestEntry = zip.CreateEntry("nest.nest");
        using var nestMs = new MemoryStream();
        var writer = new NestWriter(Nest);
        writer.Write(nestMs);
        nestMs.Position = 0;
        await using (var stream = nestEntry.Open())
        {
            await nestMs.CopyToAsync(stream);
        }
    }

    public static async Task<NestResponse> LoadAsync(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

        // Read request.json
        var requestEntry = zip.GetEntry("request.json");
        NestRequest request;
        await using (var stream = requestEntry!.Open())
        {
            request = await JsonSerializer.DeserializeAsync<NestRequest>(stream, JsonOptions);
        }

        // Read response.json
        var responseEntry = zip.GetEntry("response.json");
        JsonElement metricsJson;
        await using (var stream = responseEntry!.Open())
        {
            metricsJson = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOptions);
        }

        // Read embedded nest.nest via NestReader(Stream)
        var nestEntry = zip.GetEntry("nest.nest");
        Nest nest;
        using (var nestMs = new MemoryStream())
        {
            await using (var stream = nestEntry!.Open())
            {
                await stream.CopyToAsync(nestMs);
            }
            nestMs.Position = 0;
            var reader = new NestReader(nestMs);
            nest = reader.Read();
        }

        return new NestResponse
        {
            SheetCount = metricsJson.GetProperty("sheetCount").GetInt32(),
            Utilization = metricsJson.GetProperty("utilization").GetDouble(),
            CutTime = TimeSpan.FromTicks(metricsJson.GetProperty("cutTimeTicks").GetInt64()),
            Elapsed = TimeSpan.FromTicks(metricsJson.GetProperty("elapsedTicks").GetInt64()),
            Nest = nest,
            Request = request
        };
    }
}
