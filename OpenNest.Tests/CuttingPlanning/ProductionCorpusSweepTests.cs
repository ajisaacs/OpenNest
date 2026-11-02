using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.IO;
using Xunit.Abstractions;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Plans every occupied plate of every <c>.nest</c> file in a local folder the way Plan Cutting
/// does after clearing its lead-ins and cut-off parts, and writes one JSON line per plate with
/// its outcome, warnings, expansions and time. A survey, not an assertion: it never fails on an
/// outcome. Opt-in and private: the folder holds production nests, so neither it nor the report
/// belongs in the repository. Set <see cref="CorpusVariable"/> to the folder; the report goes to
/// <see cref="ReportVariable"/>, or <c>sweep.jsonl</c> beside the nests.
///
/// A plate plans with its own saved cutting parameters when it has them. Otherwise it uses the
/// nest's machine defaults for its material and thickness, read from the machine configurations in
/// <see cref="MachinesVariable"/> (a folder of machine JSON files), applied over the cutting
/// parameters in <see cref="ParametersVariable"/> (a cutting-parameters JSON file) when that is
/// set, or over the built-in defaults.
/// </summary>
[Trait("Category", "ProductionCorpus")]
public class ProductionCorpusSweepTests(ITestOutputHelper output)
{
    internal const string CorpusVariable = "OPENNEST_PRODUCTION_CORPUS";
    internal const string ReportVariable = "OPENNEST_PRODUCTION_CORPUS_REPORT";
    internal const string MachinesVariable = "OPENNEST_PRODUCTION_CORPUS_MACHINES";
    internal const string ParametersVariable = "OPENNEST_PRODUCTION_CORPUS_PARAMETERS";
    internal const string ParallelismVariable = "OPENNEST_PRODUCTION_CORPUS_PARALLELISM";
    internal const string TimeoutVariable = "OPENNEST_PRODUCTION_CORPUS_TIMEOUT";
    internal const string FilterVariable = "OPENNEST_PRODUCTION_CORPUS_FILTER";
    internal const string BudgetVariable = "OPENNEST_PRODUCTION_CORPUS_BUDGET";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [SkippableFact]
    public void ProductionCorpus_PlansEveryPlateAndWritesAReport()
    {
        var folder = Environment.GetEnvironmentVariable(CorpusVariable);
        Skip.If(string.IsNullOrWhiteSpace(folder), $"Set {CorpusVariable} to a folder of .nest files to sweep them.");
        Assert.True(Directory.Exists(folder), $"{folder} does not exist.");
        var report = Environment.GetEnvironmentVariable(ReportVariable) is { Length: > 0 } path ? path
            : Path.Combine(folder!, "sweep.jsonl");
        var parallelism = int.TryParse(Environment.GetEnvironmentVariable(ParallelismVariable), out var p) && p > 0 ? p : 4;
        var limit = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable(TimeoutVariable), out var s) && s > 0 ? s : 180);
        var multiplier = double.TryParse(Environment.GetEnvironmentVariable(BudgetVariable), out var m) && m > 0 ? m : 1.0;
        var filters = (Environment.GetEnvironmentVariable(FilterVariable) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var machines = Environment.GetEnvironmentVariable(MachinesVariable) is { Length: > 0 } dir ? new LocalJsonProvider(dir) : null;
        var lastParameters = Environment.GetEnvironmentVariable(ParametersVariable) is { Length: > 0 } file
            ? File.ReadAllText(file) : null;

        var files = Directory.GetFiles(folder!, "*.nest").OrderBy(f => f, StringComparer.Ordinal)
            .Where(f => filters.Length == 0 || filters.Any(n => Path.GetFileName(f).Contains(n, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        output.WriteLine($"{files.Length} nests, parallelism {parallelism}, limit {limit.TotalSeconds:0} s, budget x{multiplier}, report {report}");
        var lines = new ConcurrentQueue<string>();
        using var writer = new StreamWriter(report, append: false);
        var written = 0;
        var done = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, file =>
        {
            foreach (var row in Sweep(file, machines, lastParameters, limit, multiplier))
            {
                var line = JsonSerializer.Serialize(row, JsonOptions);
                lock (writer)
                {
                    writer.WriteLine(line);
                    writer.Flush();
                    written++;
                }
            }
            var count = Interlocked.Increment(ref done);
            if (count % 25 == 0)
                output.WriteLine($"{count}/{files.Length} nests, {written} plates");
        });
        output.WriteLine($"Done: {written} plates from {files.Length} nests in {report}");
    }

    private static IEnumerable<PlateRow> Sweep(string file, LocalJsonProvider machines, string lastParameters,
        TimeSpan limit, double multiplier)
    {
        var id = Path.GetFileNameWithoutExtension(file);
        Nest nest;
        try
        {
            using var stream = File.OpenRead(file);
            nest = new NestReader(stream).Read();
        }
        catch (Exception e)
        {
            return [new PlateRow { Nest = id, Plate = 0, Outcome = "ReadError", Error = e.GetType().Name + ": " + e.Message }];
        }
        var machine = nest.Machine == null ? null : machines?.GetMachine(nest.Machine.Id);
        var rows = new List<PlateRow>();
        for (var index = 0; index < nest.Plates.Count; index++)
        {
            var plate = nest.Plates[index];
            if (plate.Parts.Count == 0 || plate.Parts.All(p => p.BaseDrawing.IsCutOff))
                continue;
            var number = index + 1;
            var row = new PlateRow
            {
                Nest = id,
                Name = nest.Name,
                Plate = number,
                Material = $"{nest.Material?.Name} {nest.Material?.Grade} {nest.Thickness}",
                Machine = nest.Machine?.Name,
                Units = nest.Units.ToString(),
            };
            try
            {
                // Clearing forgets the plate's recorded parameters: read them first.
                var (parameters, source) = ParametersFor(plate, nest, machine, lastParameters);
                row.ParametersSource = source;
                row.SupportTabs = parameters.SupportTabs;
                row.SupportBed = plate.SupportBed != null;
                // As Plan Cutting after "Clear Lead-ins & Cut-offs": the parts regenerate from clean geometry.
                var cleared = AppliedCuttingWork.Clear([plate], [number]);
                row.ClearedLeadIns = cleared.LeadInParts;
                row.ClearedLockedLeadIns = cleared.LockedLeadInParts;
                row.ClearedCutOffs = cleared.CutOffs;
                row.Parts = plate.Parts.Count;
                row.Pinned = plate.Parts.Count(p => p.HasActiveDrawingPins);
                row.Budget = CuttingPlanBatch.PlateBudget(plate.Parts.Count, multiplier);
                var watch = Stopwatch.StartNew();
                using var cancellation = new CancellationTokenSource(limit);
                try
                {
                    var planned = CuttingPlanBatch.Capture([plate], parameters, preservePartOrder: false, [number],
                        cancellation.Token, multiplier).Plan(token: cancellation.Token).Plates[0];
                    row.Seconds = watch.Elapsed.TotalSeconds;
                    Fill(row, planned);
                }
                catch (OperationCanceledException)
                {
                    row.Seconds = watch.Elapsed.TotalSeconds;
                    row.Outcome = "Timeout";
                }
            }
            catch (Exception e)
            {
                row.Outcome = "Error";
                row.Error = e.GetType().Name + ": " + e.Message;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static void Fill(PlateRow row, CuttingPlanPlateResult planned)
    {
        var result = planned.Result;
        row.Status = result.Status == CuttingPlanStatus.Canceled ? "Timeout" : result.Status.ToString();
        row.Outcome = result.Status == CuttingPlanStatus.Canceled ? "Timeout" : ProblemNestRegressionTests.Outcome(planned);
        row.Replayed = result.IndependentlyReplayed;
        row.SafestRoute = result.IsSafestRouteFallback;
        row.HeadRaises = result.HeadRaises;
        row.KeptOrder = planned.KeptCurrentOrder;
        row.Expansions = result.Expansions;
        row.ReorderExpansions = planned.ReorderAttempt?.Expansions;
        row.OverlapPairs = planned.Overlap?.Pairs.Count;
        row.OverlapComplete = planned.Overlap?.IsComplete;
        // Spacing never steers the plan; it is recorded so PEP-converted layouts can be told apart.
        row.PartSpacing = planned.Plate.PartSpacing;
        row.SpacingPairs = planned.Overlap?.SpacingPairs.Count;
        row.NarrowestGap = planned.Overlap?.SpacingPairs.Count > 0 ? planned.Overlap.SpacingPairs.Min(p => p.Gap) : null;
        row.Warnings = result.Findings.GroupBy(f => f.Kind?.ToString() ?? "None")
            .ToDictionary(g => g.Key, g => g.Count());
        row.Findings = result.Findings.Select(f => f.Message).Distinct().Take(12).ToArray();
        // Which drawings carry the geometry a material or replay check refused, to size such a class by drawing.
        row.GeometryParts = result.Findings
            .Where(f => f.Kind is null or PostVerificationKind.Incomplete && f.SourcePart?.BaseDrawing != null
                && !f.Message.StartsWith("Best-effort", StringComparison.Ordinal))
            .Select(f => $"{f.SourcePart.BaseDrawing.Name}: {f.Message}").Distinct().Take(8).ToArray();
    }

    // The plate's own parameters, else the machine's for the nest over the last-applied ones, else those alone.
    private static (CuttingParameters Parameters, string Source) ParametersFor(Plate plate, Nest nest,
        MachineConfig machine, string lastParameters)
    {
        if (plate.CuttingParameters != null)
            return (plate.CuttingParameters, "plate");
        var parameters = lastParameters == null ? new CuttingParameters() : CuttingParametersSerializer.Deserialize(lastParameters);
        var source = lastParameters == null ? "defaults" : "last";
        if (MachineCuttingDefaults.ApplyTo(parameters, machine, nest))
            source = "machine+" + source;
        return (parameters, source);
    }

    private sealed class PlateRow
    {
        public string Nest { get; set; }
        public string Name { get; set; }
        public int Plate { get; set; }
        public string Material { get; set; }
        public string Machine { get; set; }
        public string Units { get; set; }
        public string ParametersSource { get; set; }
        public bool SupportTabs { get; set; }
        public bool SupportBed { get; set; }
        public int Parts { get; set; }
        public int Pinned { get; set; }
        public int ClearedLeadIns { get; set; }
        public int ClearedLockedLeadIns { get; set; }
        public int ClearedCutOffs { get; set; }
        public int Budget { get; set; }
        public string Outcome { get; set; }
        public string Status { get; set; }
        public bool Replayed { get; set; }
        public bool SafestRoute { get; set; }
        public int HeadRaises { get; set; }
        public bool KeptOrder { get; set; }
        public int Expansions { get; set; }
        public int? ReorderExpansions { get; set; }
        public int? OverlapPairs { get; set; }
        public bool? OverlapComplete { get; set; }
        public double PartSpacing { get; set; }
        public int? SpacingPairs { get; set; }
        public double? NarrowestGap { get; set; }
        public double Seconds { get; set; }
        public Dictionary<string, int> Warnings { get; set; }
        public string[] Findings { get; set; }
        public string[] GeometryParts { get; set; }
        public string Error { get; set; }
    }
}
