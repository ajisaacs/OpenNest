using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenNest;
using OpenNest.Benchmark;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

return BenchmarkConsole.Run(args);

static class BenchmarkConsole
{
    public static int Run(string[] args)
    {
        var options = ParseArgs(args);

        if (options == null)
            return 0; // --help was requested

        if (options.InputPath == null)
        {
            PrintUsage();
            return 1;
        }

        List<BenchmarkJob> jobs;

        try
        {
            jobs = JobLoader.Load(options.InputPath, options.SheetSizes, options.PartSpacing);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        if (jobs.Count == 0)
        {
            Console.Error.WriteLine(
                "No benchmark jobs found (no .nest files with any drawing quantity > 0, or *.manifest.json files)."
            );
            return 1;
        }

        var enginesDir = Path.Combine(AppContext.BaseDirectory, "Engines");
        NestingEngineRegistry.LoadPlugins(enginesDir);

        var engines = NestingEngineRegistry.AvailableEngines;

        if (options.EngineNames.Count > 0)
        {
            engines = engines
                .Where(e =>
                    options.EngineNames.Any(n =>
                        n.Equals(e.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToList();

            if (engines.Count == 0)
            {
                Console.Error.WriteLine(
                    "None of the requested engines are registered. Available: "
                        + string.Join(
                            ", ",
                            NestingEngineRegistry.AvailableEngines.Select(e => e.Name)
                        )
                );
                return 1;
            }
        }

        Console.WriteLine($"Loaded {jobs.Count} job(s) from '{options.InputPath}'");

        foreach (var job in jobs)
        {
            var sizes = string.Join(", ", job.CandidateSizes.Select(s => s.ToString(1)));
            Console.WriteLine(
                $"  {job.Name}: {job.Requests.Count} drawing(s), {job.TotalRequestedQuantity} part(s) requested, candidate sizes: {sizes}"
            );
        }

        if (
            options.SheetSizes.Count == 0
            && jobs.Any(j => j.SourceFile.EndsWith(".nest", StringComparison.OrdinalIgnoreCase))
        )
        {
            Console.Error.WriteLine(
                "Warning: no --sheet-sizes given, so each .nest job only offers the sheet sizes its "
                    + "original layout used - a hint toward that answer. Pass --sheet-sizes with the "
                    + "sizes you actually stock for an unbiased comparison."
            );
        }

        Console.WriteLine($"Engines: {string.Join(", ", engines.Select(e => e.Name))}");

        var effectiveSalvageRates = jobs.Select(job => options.SalvageRate ?? job.SalvageRate);
        if (
            effectiveSalvageRates.Any(rate => rate > 0)
            && (options.MinimumSalvageDimension ?? 0) <= 0
        )
        {
            Console.Error.WriteLine(
                "Warning: salvage credit is disabled because --min-salvage-dimension was not set to a positive value."
            );
        }

        var solves = jobs.Count * engines.Count;

        if (options.Parallel > 1 && solves > 1)
        {
            Console.WriteLine(
                $"Running up to {options.Parallel} solves at a time; Time(ms) is measured under that "
                    + "concurrent load. Use --parallel 1 for strictly isolated timings."
            );
        }

        var results = BenchmarkRunner.Run(
            jobs,
            engines,
            options.SalvageRate,
            options.MinimumSalvageDimension,
            options.OutputDirectory,
            options.Parallel,
            options.Progress ? Console.Out : null
        );

        Report.PrintDetailed(results);
        Report.PrintSummary(results);

        if (options.CsvPath != null)
        {
            Report.WriteCsv(options.CsvPath, results);
            Console.WriteLine();
            Console.WriteLine($"Wrote CSV report to {options.CsvPath}");
        }

        return 0;
    }

    private static Options ParseArgs(string[] args)
    {
        var o = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sheet-sizes" when i + 1 < args.Length:
                    o.SheetSizes = ParseSheetSizes(args[++i]);
                    break;

                case "--spacing" when i + 1 < args.Length:
                    o.PartSpacing = double.Parse(
                        args[++i],
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                    break;

                case "--engines" when i + 1 < args.Length:
                    o.EngineNames = args[++i]
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                        )
                        .ToList();
                    break;

                case "--csv" when i + 1 < args.Length:
                    o.CsvPath = args[++i];
                    break;

                case "--salvage-rate" when i + 1 < args.Length:
                    o.SalvageRate = double.Parse(
                        args[++i],
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                    break;
                case "--min-salvage-dimension" when i + 1 < args.Length:
                    o.MinimumSalvageDimension = double.Parse(
                        args[++i],
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                    break;
                case "--output" when i + 1 < args.Length:
                    o.OutputDirectory = args[++i];
                    break;

                case "--parallel" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var parallel) && parallel >= 1)
                        o.Parallel = parallel;
                    else
                        Console.Error.WriteLine(
                            $"Warning: --parallel needs a whole number >= 1, using {o.Parallel}"
                        );
                    break;

                case "--progress":
                    o.Progress = true;
                    break;

                case "--help":
                    PrintUsage();
                    return null;

                default:
                    if (!args[i].StartsWith("--"))
                        o.InputPath = args[i];
                    break;
            }
        }

        return o;
    }

    private static List<Size> ParseSheetSizes(string arg)
    {
        var sizes = new List<Size>();

        foreach (
            var token in arg.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (JobLoader.TryParseSheetSize(token, out var size))
                sizes.Add(size);
            else
                Console.Error.WriteLine($"Warning: could not parse sheet size '{token}', skipping");
        }

        return sizes.Distinct().ToList();
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "OpenNest.Benchmark - compare registered whole-job nesting engines on a set of .nest files"
        );
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "For each .nest file, every drawing with quantity > 0 is nested (mixed together),"
        );
        Console.Error.WriteLine(
            "once per registered INestingEngine. Each engine is handed the full job - every"
        );
        Console.Error.WriteLine(
            "requested part and the whole pool of candidate sheet sizes - and owns its own"
        );
        Console.Error.WriteLine(
            "multi-plate/size strategy: how many plates it uses, of which sizes, and how"
        );
        Console.Error.WriteLine(
            "demand splits across them. Ranking: a run that places every requested part beats"
        );
        Console.Error.WriteLine(
            "one that does not; then lower cost = sheet area consumed (minus salvage credit for a"
        );
        Console.Error.WriteLine(
            "usable offcut) + the largest candidate sheet's area per unplaced part; then fewer"
        );
        Console.Error.WriteLine(
            "plates. An invalid layout (out of bounds, overlapping, over-quantity, off-stock, or"
        );
        Console.Error.WriteLine(
            "breaking a rotation constraint), a thrown exception, or a timeout places nothing."
        );
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine(
            "  OpenNest.Benchmark <file.nest | manifest.json | folder> [options]"
        );
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "A manifest.json builds a job straight from DXF files (paths relative to the manifest):"
        );
        Console.Error.WriteLine(
            "  { \"sheetSizes\": [\"48x96\"], \"spacing\": 0.25, \"edgeSpacing\": 0.25, \"quadrant\": 1,"
        );
        Console.Error.WriteLine(
            "    \"parts\": [ { \"dxf\": \"a.dxf\", \"quantity\": 12 }, { \"dxf\": \"b.dxf\", \"quantity\": 4, \"allowRotation\": false } ] }"
        );
        Console.Error.WriteLine(
            "Sheet sizes must use the same units as the DXFs. A folder is scanned for *.nest and"
        );
        Console.Error.WriteLine(
            "*.manifest.json files. --sheet-sizes and --spacing override the manifest."
        );
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine(
            "  --sheet-sizes W1xL1,W2xL2,...  Candidate sheet-size pool for the whole nest"
        );
        Console.Error.WriteLine(
            "                                 (default: the distinct sizes already in each file,"
        );
        Console.Error.WriteLine(
            "                                 which hints engines toward the original layout)"
        );
        Console.Error.WriteLine(
            "  --spacing <value>               Override part spacing for every job"
        );
        Console.Error.WriteLine(
            "  --engines Name1,Name2,...       Only benchmark these registered engines (default: all)"
        );
        Console.Error.WriteLine(
            "  --csv <path>                    Write a flat CSV of all results"
        );
        Console.Error.WriteLine(
            "  --salvage-rate <0..1>           Fraction of eligible offcut area credited (default: saved .nest rate;"
        );
        Console.Error.WriteLine(
            "                                 manifests 0; needs positive --min-salvage-dimension)"
        );
        Console.Error.WriteLine(
            "  --min-salvage-dimension <value> Both offcut dimensions must qualify; positive value enables credit (default 0)"
        );
        Console.Error.WriteLine(
            "  --output <directory>           Save valid layouts as .nest plus detailed JSON reports"
        );
        Console.Error.WriteLine(
            "  --parallel <n>                  Solves to run at once (default 3; 1 = strictly sequential,"
        );
        Console.Error.WriteLine(
            "                                 which gives the cleanest per-engine timings)"
        );
        Console.Error.WriteLine(
            "  --progress                      Log each solve's start, engine progress and finish"
        );
        Console.Error.WriteLine("  --help                          Show this message");
    }

    private class Options
    {
        public string InputPath;
        public List<Size> SheetSizes = new();
        public double? PartSpacing;
        public List<string> EngineNames = new();
        public string CsvPath;
        public string OutputDirectory;
        public double? SalvageRate;
        public double? MinimumSalvageDimension;
        public int Parallel = 3;
        public bool Progress;
    }
}
