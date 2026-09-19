using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

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
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

        public static List<JobResult> Run(List<BenchmarkJob> jobs, IReadOnlyList<NestingEngineInfo> engines)
        {
            var results = new List<JobResult>(jobs.Count * engines.Count);

            foreach (var job in jobs)
            {
                foreach (var engineInfo in engines)
                {
                    results.Add(RunOne(job, engineInfo));
                }
            }

            return results;
        }

        private static JobResult RunOne(BenchmarkJob job, NestingEngineInfo engineInfo)
        {
            var nestJob = job.BuildNestJob(MaxPlates);
            var requested = job.TotalRequestedQuantity;
            var sw = Stopwatch.StartNew();

            try
            {
                var engine = engineInfo.Factory();
                using var cts = new CancellationTokenSource(Timeout);
                var jobResult = engine.Solve(nestJob, null, cts.Token);

                var materialized = NestResultMaterializer.Materialize(nestJob, jobResult);
                var plateRuns = materialized.Nest.Plates
                    .Select(plate => (Plate: plate, Parts: plate.Parts.ToList()))
                    .ToList();

                var validation = NestValidator.Validate(plateRuns, job);
                var totalPlaced = plateRuns.Sum(pr => pr.Parts.Count);
                var placedArea = validation.Valid ? plateRuns.Sum(pr => pr.Parts.Sum(p => p.BaseDrawing.Area)) : 0;
                var plateArea = plateRuns.Sum(pr => pr.Plate.Area());

                var sizeBreakdown = plateRuns
                    .GroupBy(pr => pr.Plate.Size.ToString(1))
                    .OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count());

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
                    Error = $"Timed out after {Timeout.TotalMinutes:F0} minute(s)",
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
