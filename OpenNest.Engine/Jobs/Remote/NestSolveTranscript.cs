using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNest.Engine.Jobs.Remote;

/// <summary>
/// Every engine solve one <see cref="NestPipeline.Run(INestingEngine, string, NestPipelineRequest, IProgress{NestJobProgress}, System.Threading.CancellationToken)"/>
/// made, in order: what it asked (part and stock ids with their quantities) and the raw result
/// the engine returned. A remote solve sends this back instead of bound plates, so the client
/// replays the pipeline against its own drawings and validates and binds the result itself
/// (<see cref="RemoteNestSubmission"/>). Poses are kept bit for bit, nonfinite ones included,
/// so the client's validation sees exactly what the engine returned.
/// </summary>
public sealed class NestSolveTranscript
{
    public const int CurrentVersion = 1;

    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public int Version { get; set; } = CurrentVersion;
    public List<SolveJson> Solves { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a transcript; throws <see cref="FormatException"/> when it is not one this build understands.</summary>
    public static NestSolveTranscript FromJson(string json)
    {
        NestSolveTranscript transcript;
        try { transcript = JsonSerializer.Deserialize<NestSolveTranscript>(json, Options); }
        catch (JsonException ex) { throw new FormatException("The server's nest result is not valid JSON.", ex); }
        if (transcript?.Solves == null || transcript.Solves.Any(s => s?.Result == null))
            throw new FormatException("The server's nest result is incomplete.");
        if (transcript.Version != CurrentVersion)
            throw new FormatException($"The server's nest result is version {transcript.Version}; this build reads {CurrentVersion}.");
        return transcript;
    }

    internal void Add(NestJob job, NestJobResult result) =>
        Solves.Add(new SolveJson { Request = Fingerprint(job), Result = ResultJson.From(result) });

    /// <summary>What a solve was asked: the job's part and stock ids with their quantities, in order.</summary>
    internal static List<string> Fingerprint(NestJob job) =>
        job.Parts.Select(p => $"part:{p.Id}:{p.Quantity}")
            .Concat(job.Plates.Select(s => $"stock:{s.Id}:{s.Quantity?.ToString() ?? "*"}"))
            .ToList();

    public sealed class SolveJson
    {
        public List<string> Request { get; set; } = new();
        public ResultJson Result { get; set; }
    }

    public sealed class ResultJson
    {
        public NestJobStatus Status { get; set; }
        public NestJobStopReason StopReason { get; set; }
        public List<PlateJson> Plates { get; set; } = new();
        public List<PartFulfillment> Fulfillment { get; set; } = new();
        public List<StockUsage> StockUsage { get; set; } = new();

        internal static ResultJson From(NestJobResult result) => new()
        {
            Status = result.Status,
            StopReason = result.StopReason,
            Plates = result.Plates.Select(p => new PlateJson
            {
                PlateIndex = p.PlateIndex,
                Stock = NestStockJson.From(p.Stock),
                Placements = p.Placements.ToList(),
                CommonLines = p.CommonLines.Count == 0 ? null
                    : p.CommonLines.Select(g => new CommonLineJson { Kerf = g.Kerf, Placements = g.Placements.ToList() }).ToList(),
            }).ToList(),
            Fulfillment = result.Fulfillment.ToList(),
            StockUsage = result.StockUsage.ToList(),
        };

        /// <summary>The raw result, with each sheet that matches one of <paramref name="job"/>'s
        /// stock entries using that entry itself, as an engine solving the job would.</summary>
        internal NestJobResult ToResult(NestJob job)
        {
            var plates = new List<NestJobPlateResult>(Plates.Count);
            foreach (var plate in Plates)
            {
                var stock = plate.Stock?.ToStock() ?? throw new FormatException("A result sheet has no stock.");
                var own = job.Plates.FirstOrDefault(s => RepeatedPlateGrouping.SameStock(s, stock));
                plates.Add(new NestJobPlateResult(plate.PlateIndex, own ?? stock, plate.Placements ?? new(),
                    plate.CommonLines?.Select(g => g == null ? throw new FormatException("A result common-line group is empty.")
                        : new NestJobCommonLine(g.Kerf, g.Placements ?? new()))));
            }
            return new NestJobResult(Status, StopReason, plates, Fulfillment ?? new(), StockUsage ?? new());
        }
    }

    public sealed class PlateJson
    {
        public int PlateIndex { get; set; }
        public NestStockJson Stock { get; set; }
        public List<NestJobPlacement> Placements { get; set; } = new();

        /// <summary>Common-line groups an engine returned itself (none of the built-in engines
        /// do: the pipeline makes pairs around them). Omitted when empty.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<CommonLineJson> CommonLines { get; set; }
    }

    public sealed class CommonLineJson
    {
        public double Kerf { get; set; }
        public List<int> Placements { get; set; } = new();
    }
}
