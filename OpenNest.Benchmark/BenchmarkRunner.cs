using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Runs every candidate engine against every job. Each (job, engine) pair gets
    /// its own freshly-built Plate and NestItem list (via BenchmarkJob.CreatePlate/
    /// CreateItems), so no engine can see another's mutated state and no job can
    /// leak partial state into the next run of the same engine.
    /// </summary>
    public static class BenchmarkRunner
    {
        public static List<JobResult> Run(List<BenchmarkJob> jobs, IReadOnlyList<NestEngineInfo> engines)
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

        private static JobResult RunOne(BenchmarkJob job, NestEngineInfo engineInfo)
        {
            var plate = job.CreatePlate();
            var items = job.CreateItems();
            var requested = job.TotalRequestedQuantity;

            var sw = Stopwatch.StartNew();
            List<Part> parts;

            try
            {
                var engine = engineInfo.Factory(plate);
                parts = engine.Nest(items, null, CancellationToken.None) ?? new List<Part>();
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

            sw.Stop();

            var validation = NestValidator.Validate(parts, plate, job);

            // Matches Plate.Utilization(): full sheet area, not just the cuttable
            // work area, since that's what the material actually costs.
            var plateArea = plate.Area();
            var placedArea = validation.Valid ? parts.Sum(p => p.BaseDrawing.Area) : 0;
            var usedBox = parts.Count > 0 ? parts.GetBoundingBox() : Box.Empty;

            return new JobResult
            {
                EngineName = engineInfo.Name,
                JobName = job.Name,
                Valid = validation.Valid,
                Violations = validation.Violations,
                PartsPlaced = parts.Count,
                PartsRequested = requested,
                PlacedArea = placedArea,
                PlateArea = plateArea,
                UsedBoundingBoxArea = usedBox.Width * usedBox.Length,
                ElapsedMs = sw.ElapsedMilliseconds,
            };
        }
    }
}
