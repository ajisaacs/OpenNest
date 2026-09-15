using OpenNest.Geometry;
using OpenNest.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Builds BenchmarkJobs from .nest files on disk. Fully generic: works on any
    /// valid .nest file, using whatever drawings/quantities/plate settings it contains.
    /// Optionally sweeps a fixed list of sheet sizes instead of the sizes embedded
    /// in the file, so the same drawing set can be benchmarked across a standard
    /// sheet-size lineup.
    /// </summary>
    public static class JobLoader
    {
        public static List<BenchmarkJob> Load(string inputPath, IReadOnlyList<Size> sheetSizeOverrides = null,
            double? partSpacingOverride = null)
        {
            var files = ResolveFiles(inputPath);
            var jobs = new List<BenchmarkJob>();

            foreach (var file in files)
            {
                Nest nest;

                try
                {
                    nest = new NestReader(file).Read();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[JobLoader] Skipping '{file}': failed to read ({ex.Message})");
                    continue;
                }

                var requests = BuildRequests(nest);

                if (requests.Count == 0)
                {
                    Console.Error.WriteLine($"[JobLoader] Skipping '{file}': no drawings with quantity > 0");
                    continue;
                }

                var template = ResolvePlateTemplate(nest);
                var sizes = sheetSizeOverrides != null && sheetSizeOverrides.Count > 0
                    ? sheetSizeOverrides
                    : ResolveSheetSizes(nest);

                foreach (var size in sizes)
                {
                    jobs.Add(new BenchmarkJob
                    {
                        SourceFile = file,
                        SheetSizeLabel = size.ToString(1),
                        PlateSize = size,
                        EdgeSpacing = template.EdgeSpacing,
                        PartSpacing = partSpacingOverride ?? template.PartSpacing,
                        Quadrant = template.Quadrant,
                        Requests = requests,
                    });
                }
            }

            return jobs;
        }

        private static List<string> ResolveFiles(string inputPath)
        {
            if (Directory.Exists(inputPath))
            {
                return Directory.GetFiles(inputPath, "*.nest", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (File.Exists(inputPath))
                return new List<string> { inputPath };

            throw new FileNotFoundException($"Benchmark input not found: {inputPath}");
        }

        private static List<DrawingRequest> BuildRequests(Nest nest)
        {
            var requests = new List<DrawingRequest>();

            foreach (var drawing in nest.Drawings)
            {
                var qty = drawing.Quantity.Required;

                if (qty <= 0)
                    continue;

                var constraints = drawing.Constraints;

                requests.Add(new DrawingRequest
                {
                    Drawing = drawing,
                    Quantity = qty,
                    Priority = drawing.Priority,
                    StepAngle = constraints?.StepAngle ?? 0,
                    RotationStart = constraints?.StartAngle ?? 0,
                    RotationEnd = constraints?.EndAngle ?? 0,
                });
            }

            return requests;
        }

        private static (Spacing EdgeSpacing, double PartSpacing, int Quadrant) ResolvePlateTemplate(Nest nest)
        {
            var source = nest.Plates?.FirstOrDefault();

            if (source != null)
                return (source.EdgeSpacing, source.PartSpacing, source.Quadrant);

            var defaults = nest.PlateDefaults;
            return (defaults.EdgeSpacing, defaults.PartSpacing, defaults.Quadrant);
        }

        private static List<Size> ResolveSheetSizes(Nest nest)
        {
            var sizes = (nest.Plates ?? Enumerable.Empty<Plate>())
                .Select(p => p.Size)
                .Distinct()
                .ToList();

            if (sizes.Count == 0)
                sizes.Add(nest.PlateDefaults.Size);

            return sizes;
        }
    }
}
