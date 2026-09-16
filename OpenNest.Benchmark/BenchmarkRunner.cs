using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Runs every candidate engine against every job. A job may need several
    /// plates to place everything it asks for; this drives that loop itself,
    /// since NestEngineBase.Nest() fills exactly one already-sized plate and
    /// has no say in picking its own size. For each plate the loop needs, the
    /// smallest candidate size that fits the largest still-unplaced drawing is
    /// chosen via the codebase's own MultiPlateNester.CreatePlate, then the
    /// engine's Nest() fills that plate with whatever of the remaining items
    /// fit. This is applied identically to every engine, so no engine gets to
    /// (or has to) implement sheet-size selection itself.
    /// </summary>
    public static class BenchmarkRunner
    {
        /// <summary>Safety cap so a degenerate engine (placing almost nothing
        /// per plate) can't loop indefinitely.</summary>
        private const int MaxPlates = 40;

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
            var template = job.CreateTemplatePlate();
            var options = job.BuildPlateOptions();
            var remaining = job.CreateItems();
            var requested = job.TotalRequestedQuantity;

            var plateRuns = new List<(Plate Plate, List<Part> Parts)>();
            string engineError = null;
            var sw = Stopwatch.StartNew();

            try
            {
                while (remaining.Any(i => i.Quantity > 0) && plateRuns.Count < MaxPlates)
                {
                    var largest = remaining
                        .Where(i => i.Quantity > 0)
                        .OrderByDescending(i => BoundsArea(i))
                        .First();

                    var plate = MultiPlateNester.CreatePlate(template, options, largest.Drawing.Program.BoundingBox());
                    var engine = engineInfo.Factory(plate);
                    var itemsClone = CloneItems(remaining);

                    var parts = engine.Nest(itemsClone, null, CancellationToken.None) ?? new List<Part>();

                    if (parts.Count == 0)
                    {
                        // Not even the largest available candidate size could fit
                        // the current largest remaining part - stop here rather
                        // than loop forever; whatever's left is reported unplaced.
                        break;
                    }

                    plateRuns.Add((plate, parts));

                    foreach (var item in remaining)
                    {
                        var placed = parts.Count(p => p.BaseDrawing.Id == item.Drawing.Id);

                        if (placed > 0)
                            item.Quantity = System.Math.Max(0, item.Quantity - placed);
                    }
                }
            }
            catch (Exception ex)
            {
                engineError = $"{ex.GetType().Name}: {ex.Message}";
            }

            sw.Stop();

            if (engineError != null)
            {
                return new JobResult
                {
                    EngineName = engineInfo.Name,
                    JobName = job.Name,
                    Valid = false,
                    PartsRequested = requested,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Error = engineError,
                };
            }

            var validation = NestValidator.Validate(plateRuns, job);
            var totalPlaced = plateRuns.Sum(pr => pr.Parts.Count);
            var placedArea = validation.Valid ? plateRuns.Sum(pr => pr.Parts.Sum(p => p.BaseDrawing.Area)) : 0;
            var plateArea = plateRuns.Sum(pr => pr.Plate.Area());

            var sizeBreakdown = plateRuns
                .GroupBy(pr => pr.Plate.Size.ToString(1))
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());

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

        private static double BoundsArea(NestItem item)
        {
            var bb = item.Drawing.Program.BoundingBox();
            return bb.Width * bb.Length;
        }

        private static List<NestItem> CloneItems(List<NestItem> items)
        {
            return items.Select(i => new NestItem
            {
                Drawing = i.Drawing,
                Quantity = i.Quantity,
                Priority = i.Priority,
                StepAngle = i.StepAngle,
                RotationStart = i.RotationStart,
                RotationEnd = i.RotationEnd,
            }).ToList();
        }
    }
}
