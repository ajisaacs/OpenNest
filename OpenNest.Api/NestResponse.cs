using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OpenNest.IO;
using OpenNest.Engine.Jobs;

namespace OpenNest.Api;

/// <summary>Stable fulfillment metadata for one requested part identity.</summary>
public sealed record NestPartFulfillment(string PartId, int Requested, int Placed, int Unplaced);

/// <summary>Physical-sheet usage for one stock identity.</summary>
public sealed record NestStockUsage(string StockId, int Used, int? Remaining);

/// <summary>Maps each materialized physical sheet to its source stock identity.</summary>
public sealed record NestPlateStockMapping(int PlateIndex, string StockId);

public class NestResponse
{
    public const int CurrentSchemaVersion = 2;

    /// <summary>Zero identifies an archive written before response metadata was versioned.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public int SheetCount { get; init; }

    /// <summary>Placed-part area divided by total materialized physical-sheet area, as a 0.0–1.0 ratio.</summary>
    public double Utilization { get; init; }
    public TimeSpan CutTime { get; init; }
    public TimeSpan Elapsed { get; init; }

    /// <summary>Null means an older archive did not record whole-job fulfillment status.</summary>
    public NestJobStatus? Status { get; init; }
    public NestJobStopReason? StopReason { get; init; }
    public IReadOnlyList<NestPartFulfillment> Fulfillment { get; init; } = [];
    public IReadOnlyList<NestStockUsage> StockUsage { get; init; } = [];
    public IReadOnlyList<NestPlateStockMapping> PlateStockMappings { get; init; } = [];
    public Nest Nest { get; init; }
    public NestRequest Request { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IncludeFields = true, // Required for OpenNest.Geometry.Size and Spacing public fields.
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task SaveAsync(string path)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        var requestEntry = zip.CreateEntry("request.json");
        await using (var stream = requestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(stream, Request, JsonOptions);
        }

        // Keep persisted data versioned and detached from the live mutable Nest graph.
        var responseEntry = zip.CreateEntry("response.json");
        await using (var stream = responseEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                stream,
                new NestResponseArchiveDto
                {
                    SchemaVersion = CurrentSchemaVersion,
                    SheetCount = SheetCount,
                    Utilization = Utilization,
                    CutTimeTicks = CutTime.Ticks,
                    ElapsedTicks = Elapsed.Ticks,
                    Status = Status,
                    StopReason = StopReason,
                    Fulfillment = Fulfillment is null
                        ? []
                        : new List<NestPartFulfillment>(Fulfillment),
                    StockUsage = StockUsage is null ? [] : new List<NestStockUsage>(StockUsage),
                    PlateStockMappings = PlateStockMappings is null
                        ? []
                        : new List<NestPlateStockMapping>(PlateStockMappings),
                },
                JsonOptions
            );
        }

        var nestEntry = zip.CreateEntry("nest.nest");
        using var nestMs = new MemoryStream();
        new NestWriter(Nest).Write(nestMs);
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

        var requestEntry =
            zip.GetEntry("request.json")
            ?? throw new InvalidOperationException("Missing request.json in .nestquote file");
        NestRequest request;
        await using (var stream = requestEntry.Open())
        {
            request =
                await JsonSerializer.DeserializeAsync<NestRequest>(stream, JsonOptions)
                ?? throw new InvalidOperationException("Invalid request.json in .nestquote file");
        }

        var responseEntry =
            zip.GetEntry("response.json")
            ?? throw new InvalidOperationException("Missing response.json in .nestquote file");
        NestResponseArchiveDto archive;
        var hasSchemaVersion = false;
        var hasStatusMetadata = false;
        await using (var stream = responseEntry.Open())
        using (var document = await JsonDocument.ParseAsync(stream))
        {
            var root = document.RootElement;
            hasSchemaVersion = root.TryGetProperty("schemaVersion", out _);
            hasStatusMetadata =
                root.TryGetProperty("status", out _)
                || root.TryGetProperty("stopReason", out _)
                || root.TryGetProperty("fulfillment", out _)
                || root.TryGetProperty("stockUsage", out _)
                || root.TryGetProperty("plateStockMappings", out _);
            archive =
                root.Deserialize<NestResponseArchiveDto>(JsonOptions)
                ?? throw new InvalidOperationException("Invalid response.json in .nestquote file");
        }

        var nestEntry =
            zip.GetEntry("nest.nest")
            ?? throw new InvalidOperationException("Missing nest.nest in .nestquote file");
        Nest nest;
        using (var nestMs = new MemoryStream())
        {
            await using (var stream = nestEntry.Open())
            {
                await stream.CopyToAsync(nestMs);
            }
            nestMs.Position = 0;
            nest = new NestReader(nestMs).Read();
        }

        return new NestResponse
        {
            SchemaVersion = hasSchemaVersion ? archive.SchemaVersion : 0,
            SheetCount = archive.SheetCount,
            Utilization = archive.Utilization,
            CutTime = TimeSpan.FromTicks(archive.CutTimeTicks),
            Elapsed = TimeSpan.FromTicks(archive.ElapsedTicks),
            Status = hasStatusMetadata ? archive.Status : null,
            StopReason = hasStatusMetadata ? archive.StopReason : null,
            Fulfillment = hasStatusMetadata ? archive.Fulfillment ?? [] : [],
            StockUsage = hasStatusMetadata ? archive.StockUsage ?? [] : [],
            PlateStockMappings = hasStatusMetadata ? archive.PlateStockMappings ?? [] : [],
            Nest = nest,
            Request = request,
        };
    }

    private sealed class NestResponseArchiveDto
    {
        public int SchemaVersion { get; init; }
        public int SheetCount { get; init; }
        public double Utilization { get; init; }
        public long CutTimeTicks { get; init; }
        public long ElapsedTicks { get; init; }
        public NestJobStatus? Status { get; init; }
        public NestJobStopReason? StopReason { get; init; }
        public List<NestPartFulfillment> Fulfillment { get; init; } = [];
        public List<NestStockUsage> StockUsage { get; init; } = [];
        public List<NestPlateStockMapping> PlateStockMappings { get; init; } = [];
    }
}
