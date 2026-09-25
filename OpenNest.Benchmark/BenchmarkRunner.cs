using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Runs every candidate engine against every job. Each engine is a full
    /// INestingEngine: it owns its own plate/size selection and multi-plate
    /// strategy for the whole job, rather than being handed one already-sized
    /// plate at a time by this harness. A per-run timeout guards against a
    /// runaway or hanging engine — cooperative cancellation, so it reliably
    /// stops engines built on NestJobRunner (all four built-ins) but can't
    /// forcibly interrupt an engine that never checks its token.
    /// </summary>
    public static class BenchmarkRunner
    {
        /// <summary>Physical-sheet cap passed to every job's NestJobOptions.MaxPlates.</summary>
        private const int MaxPlates = 40;

        /// <summary>Wall-clock budget for one engine solving one job.</summary>
        private static readonly TimeSpan SolveTimeout = TimeSpan.FromMinutes(5);

        public static List<JobResult> Run(
            List<BenchmarkJob> jobs,
            IReadOnlyList<NestingEngineInfo> engines,
            double? salvageRate = null,
            double? minimumSalvageDimension = null,
            string outputDirectory = null,
            int maxParallelism = 1,
            System.IO.TextWriter progressLog = null
        )
        {
            var pairs = jobs.SelectMany(job => engines.Select(engine => (Job: job, Engine: engine)))
                .ToList();
            var results = new JobResult[pairs.Count];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = System.Math.Max(1, maxParallelism),
            };

            var baselineResults = new JobResult[jobs.Count];
            Parallel.ForEach(
                Partitioner.Create(
                    Enumerable.Range(0, jobs.Count),
                    EnumerablePartitionerOptions.NoBuffering
                ),
                options,
                i => baselineResults[i] = RunBaseline(jobs[i], salvageRate, minimumSalvageDimension)
            );

            // NoBuffering hands out one pair at a time: solves run for seconds to minutes,
            // so chunked partitioning would leave workers idle behind a slow engine.
            Parallel.ForEach(
                Partitioner.Create(
                    Enumerable.Range(0, pairs.Count),
                    EnumerablePartitionerOptions.NoBuffering
                ),
                options,
                i =>
                    results[i] = RunOne(
                        pairs[i].Job,
                        pairs[i].Engine,
                        salvageRate,
                        minimumSalvageDimension,
                        outputDirectory,
                        progressLog
                    )
            );

            // Indexed writes keep the report in job-then-engine order whatever finishes first.
            var ordered = new List<JobResult>(
                results.Length + baselineResults.Count(result => result != null)
            );
            for (var jobIndex = 0; jobIndex < jobs.Count; jobIndex++)
            {
                if (baselineResults[jobIndex] != null)
                    ordered.Add(baselineResults[jobIndex]);
                var firstResult = jobIndex * engines.Count;
                for (var engineIndex = 0; engineIndex < engines.Count; engineIndex++)
                    ordered.Add(results[firstResult + engineIndex]);
            }
            return ordered;
        }

        private static JobResult RunBaseline(
            BenchmarkJob job,
            double? salvageRate,
            double? minimumSalvageDimension
        )
        {
            if (job.BaselinePlateRuns == null)
                return null;
            var requested = job.TotalRequestedQuantity;
            try
            {
                var requirements = job.Requests.ToDictionary<
                    DrawingRequest,
                    Drawing,
                    (string Name, int Quantity)
                >(
                    request => request.Drawing,
                    request => (request.Drawing.Name, request.Quantity),
                    ReferenceEqualityComparer.Instance
                );
                var partIds = job.Requests.ToDictionary<DrawingRequest, Drawing, string>(
                    request => request.Drawing,
                    request => request.Drawing.Id.ToString(),
                    ReferenceEqualityComparer.Instance
                );
                var validation = NestValidator.Validate(job.BaselinePlateRuns, requirements);
                var benchmarkJob = job.BuildNestJob(
                    MaxPlates,
                    salvageRate,
                    minimumSalvageDimension
                );
                var instanceIndices = new Dictionary<string, int>(StringComparer.Ordinal);
                var plateResults = job
                    .BaselinePlateRuns.Select(
                        (run, index) =>
                        {
                            var stock = new NestPlateStock(
                                $"baseline-{index}",
                                run.Plate.Size,
                                1,
                                run.Plate.PartSpacing,
                                run.Plate.EdgeSpacing,
                                run.Plate.Quadrant
                            );
                            var placements = run
                                .Parts.Select(part =>
                                {
                                    var partId = partIds[part.BaseDrawing];
                                    instanceIndices.TryGetValue(partId, out var instanceIndex);
                                    instanceIndices[partId] = instanceIndex + 1;
                                    return new NestJobPlacement(
                                        partId,
                                        instanceIndex,
                                        part.Location.X,
                                        part.Location.Y,
                                        part.Rotation
                                    );
                                })
                                .ToList();
                            return new NestJobPlateResult(index, stock, placements);
                        }
                    )
                    .ToList();
                var baselineJob = new NestJob(
                    benchmarkJob.Parts,
                    plateResults.Select(result => result.Stock),
                    benchmarkJob.Options
                );
                var baselineJobResult = new NestJobResult(
                    NestJobStatus.Complete,
                    NestJobStopReason.Completed,
                    plateResults,
                    Array.Empty<PartFulfillment>(),
                    Array.Empty<StockUsage>()
                );
                NestValidator.ValidateAgainstJob(
                    baselineJob,
                    baselineJobResult,
                    job.Requests.ToDictionary(
                        request => request.Drawing.Id.ToString(),
                        request => request.Drawing.Name
                    ),
                    validation
                );

                var plateRuns = job.BaselinePlateRuns;
                var placedArea = validation.Valid
                    ? plateRuns.Sum(run => run.Parts.Sum(part => part.BaseDrawing.Area))
                    : 0;
                var plateArea = plateRuns.Sum(run => run.Plate.Area());
                var netSheetArea = validation.Valid
                    ? plateResults.Sum(result =>
                        NestJobCost.NetSheetArea(baselineJob, result)
                    )
                    : 0;
                var sizeBreakdown = plateRuns
                    .GroupBy(run => run.Plate.Size.ToString(1))
                    .OrderByDescending(group => group.Count())
                    .ToDictionary(group => group.Key, group => group.Count());

                return new JobResult
                {
                    EngineName = "Baseline",
                    JobName = job.Name,
                    Valid = validation.Valid,
                    Violations = validation.Violations,
                    PartsPlaced = plateRuns.Sum(run => run.Parts.Count),
                    PartsRequested = requested,
                    PlacedArea = placedArea,
                    PlateArea = plateArea,
                    NetSheetArea = netSheetArea,
                    UnplacedPartPenalty = job.UnplacedPartPenalty,
                    PlatesUsed = plateRuns.Count,
                    SizeBreakdown = sizeBreakdown,
                    ElapsedMs = 0,
                };
            }
            catch (Exception ex)
            {
                return new JobResult
                {
                    EngineName = "Baseline",
                    JobName = job.Name,
                    Valid = false,
                    PartsRequested = requested,
                    UnplacedPartPenalty = job.UnplacedPartPenalty,
                    Error = $"{ex.GetType().Name}: {ex.Message}",
                };
            }
        }

        private static JobResult RunOne(
            BenchmarkJob job,
            NestingEngineInfo engineInfo,
            double? salvageRate,
            double? minimumSalvageDimension,
            string outputDirectory,
            System.IO.TextWriter progressLog
        )
        {
            var requested = job.TotalRequestedQuantity;
            var log = progressLog == null
                ? null
                : new JobProgressLog(progressLog, $"{job.Name}/{engineInfo.Name}");
            log?.Started();
            var sw = Stopwatch.StartNew();

            try
            {
                var nestJob = job.BuildNestJob(MaxPlates, salvageRate, minimumSalvageDimension);
                var engine = engineInfo.Factory();
                using var cts = new CancellationTokenSource(SolveTimeout);
                var jobResult = engine.Solve(nestJob, log, cts.Token);
                log?.Finished(jobResult, sw.ElapsedMilliseconds);

                var materialized = NestResultMaterializer.Materialize(nestJob, jobResult);
                var plateRuns = materialized
                    .Nest.Plates.Select(plate => (Plate: plate, Parts: plate.Parts.ToList()))
                    .ToList();

                var requirements = job.Requests.ToDictionary<
                    DrawingRequest,
                    Drawing,
                    (string Name, int Quantity)
                >(
                    r => materialized.DrawingsByPartId[r.Drawing.Id.ToString()],
                    r => (r.Drawing.Name, r.Quantity),
                    ReferenceEqualityComparer.Instance
                );

                var validation = NestValidator.Validate(plateRuns, requirements);
                NestValidator.ValidateAgainstJob(
                    nestJob,
                    jobResult,
                    job.Requests.ToDictionary(r => r.Drawing.Id.ToString(), r => r.Drawing.Name),
                    validation
                );
                var totalPlaced = plateRuns.Sum(pr => pr.Parts.Count);
                var placedArea = validation.Valid
                    ? plateRuns.Sum(pr => pr.Parts.Sum(p => p.BaseDrawing.Area))
                    : 0;
                var plateArea = plateRuns.Sum(pr => pr.Plate.Area());
                // Salvage credit is recomputed from the job's own geometry, never taken from the engine.
                var netSheetArea = validation.Valid
                    ? jobResult.Plates.Sum(p =>
                        NestJobCost.NetSheetArea(nestJob, p)
                    )
                    : 0;

                var sizeBreakdown = plateRuns
                    .GroupBy(pr => pr.Plate.Size.ToString(1))
                    .OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count());

                if (validation.Valid && outputDirectory != null)
                {
                    System.IO.Directory.CreateDirectory(outputDirectory);
                    // Keep names and job metadata for a useful inspectable output; never modify source.
                    // Manifest jobs have no source nest to copy from, so they keep the job's name.
                    if (job.SourceFile.EndsWith(".nest", StringComparison.OrdinalIgnoreCase))
                    {
                        var source = new OpenNest.IO.NestReader(job.SourceFile).Read();
                        materialized.Nest.Name = source.Name;
                        materialized.Nest.Units = source.Units;
                        materialized.Nest.Material = source.Material;
                        materialized.Nest.Thickness = source.Thickness;
                    }
                    else
                    {
                        materialized.Nest.Name = job.Name;
                    }
                    materialized.Nest.SalvageRate = nestJob.Options.SalvageRate;
                    foreach (var request in job.Requests)
                        materialized.DrawingsByPartId[request.Drawing.Id.ToString()].Name = request
                            .Drawing
                            .Name;
                    var path = System.IO.Path.Combine(
                        outputDirectory,
                        $"{job.Name}-{engineInfo.Name}.nest"
                    );
                    if (
                        System.IO.Path.GetFullPath(path)
                        == System.IO.Path.GetFullPath(job.SourceFile)
                    )
                        throw new InvalidOperationException(
                            "Output must not overwrite the source nest."
                        );
                    new OpenNest.IO.NestWriter(materialized.Nest).Write(path);
                    var report = new
                    {
                        Source = job.SourceFile,
                        Engine = engineInfo.Name,
                        jobResult.Status,
                        jobResult.StopReason,
                        Requested = requested,
                        Placed = totalPlaced,
                        SheetArea = plateArea,
                        PlacedArea = placedArea,
                        SalvageRate = nestJob.Options.SalvageRate,
                        MinimumSalvageDimension = nestJob.Options.MinimumSalvageDimension,
                        EstimatedNetArea = netSheetArea,
                        Fulfillment = jobResult.Fulfillment,
                        StockUsage = jobResult.StockUsage,
                        Plates = jobResult.Plates,
                        validation.Violations,
                    };
                    System.IO.File.WriteAllText(
                        System.IO.Path.ChangeExtension(path, ".json"),
                        System.Text.Json.JsonSerializer.Serialize(
                            report,
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                        )
                    );
                }
                sw.Stop();

                return new JobResult
                {
                    EngineName = engineInfo.Name,
                    JobName = job.Name,
                    Valid = validation.Valid,
                    Violations = validation.Violations,
                    PartsPlaced = totalPlaced,
                    PartsRequested = requested,
                    PlacedArea = placedArea,
                    PlateArea = plateArea,
                    NetSheetArea = netSheetArea,
                    UnplacedPartPenalty = job.UnplacedPartPenalty,
                    PlatesUsed = plateRuns.Count,
                    SizeBreakdown = sizeBreakdown,
                    ElapsedMs = sw.ElapsedMilliseconds,
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                log?.Failed("timed out", sw.ElapsedMilliseconds);
                return new JobResult
                {
                    EngineName = engineInfo.Name,
                    JobName = job.Name,
                    Valid = false,
                    PartsRequested = requested,
                    UnplacedPartPenalty = job.UnplacedPartPenalty,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Error = $"Timed out after {SolveTimeout.TotalMinutes:F0} minute(s)",
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                log?.Failed($"{ex.GetType().Name}: {ex.Message}", sw.ElapsedMilliseconds);
                return new JobResult
                {
                    EngineName = engineInfo.Name,
                    JobName = job.Name,
                    Valid = false,
                    PartsRequested = requested,
                    UnplacedPartPenalty = job.UnplacedPartPenalty,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Error = $"{ex.GetType().Name}: {ex.Message}",
                };
            }
        }
    }
}
