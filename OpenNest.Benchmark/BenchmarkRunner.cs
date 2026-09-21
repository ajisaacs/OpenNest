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
            double salvageRate = 0,
            double minimumSalvageDimension = 0,
            string outputDirectory = null,
            int maxParallelism = 1
        )
        {
            var pairs = jobs.SelectMany(job => engines.Select(engine => (Job: job, Engine: engine)))
                .ToList();
            var results = new JobResult[pairs.Count];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = System.Math.Max(1, maxParallelism),
            };

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
                        outputDirectory
                    )
            );

            // Indexed writes keep the report in job-then-engine order whatever finishes first.
            return results.ToList();
        }

        private static JobResult RunOne(
            BenchmarkJob job,
            NestingEngineInfo engineInfo,
            double salvageRate,
            double minimumSalvageDimension,
            string outputDirectory
        )
        {
            var requested = job.TotalRequestedQuantity;
            var sw = Stopwatch.StartNew();

            try
            {
                var nestJob = job.BuildNestJob(MaxPlates, salvageRate, minimumSalvageDimension);
                var engine = engineInfo.Factory();
                using var cts = new CancellationTokenSource(SolveTimeout);
                var jobResult = engine.Solve(nestJob, null, cts.Token);

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
                var totalPlaced = plateRuns.Sum(pr => pr.Parts.Count);
                var placedArea = validation.Valid
                    ? plateRuns.Sum(pr => pr.Parts.Sum(p => p.BaseDrawing.Area))
                    : 0;
                var plateArea = plateRuns.Sum(pr => pr.Plate.Area());

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
                    materialized.Nest.SalvageRate = salvageRate;
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
                        SalvageRate = salvageRate,
                        MinimumSalvageDimension = minimumSalvageDimension,
                        EstimatedNetArea = jobResult.Plates.Sum(p =>
                            StockLadderNestingEngine.EstimateNetArea(nestJob, p)
                        ),
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
                    PlatesUsed = plateRuns.Count,
                    SizeBreakdown = sizeBreakdown,
                    ElapsedMs = sw.ElapsedMilliseconds,
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new JobResult
                {
                    EngineName = engineInfo.Name,
                    JobName = job.Name,
                    Valid = false,
                    PartsRequested = requested,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Error = $"Timed out after {SolveTimeout.TotalMinutes:F0} minute(s)",
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new JobResult
                {
                    EngineName = engineInfo.Name,
                    JobName = job.Name,
                    Valid = false,
                    PartsRequested = requested,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Error = $"{ex.GetType().Name}: {ex.Message}",
                };
            }
        }
    }
}
