using OpenNest;
using OpenNest.Benchmark;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

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
            Console.Error.WriteLine("No benchmark jobs found (no .nest files with any drawing quantity > 0).");
            return 1;
        }

        var engines = NestEngineRegistry.AvailableEngines;

        if (options.EngineNames.Count > 0)
        {
            engines = engines
                .Where(e => options.EngineNames.Any(n => n.Equals(e.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (engines.Count == 0)
            {
                Console.Error.WriteLine("None of the requested engines are registered. Available: " +
                    string.Join(", ", NestEngineRegistry.AvailableEngines.Select(e => e.Name)));
                return 1;
            }
        }

        Console.WriteLine($"Loaded {jobs.Count} job(s) from '{options.InputPath}'");

        foreach (var job in jobs)
        {
            var sizes = string.Join(", ", job.CandidateSizes.Select(s => s.ToString(1)));
            Console.WriteLine($"  {job.Name}: {job.Requests.Count} drawing(s), {job.TotalRequestedQuantity} part(s) requested, candidate sizes: {sizes}");
        }

        Console.WriteLine($"Engines: {string.Join(", ", engines.Select(e => e.Name))}");

        var results = BenchmarkRunner.Run(jobs, engines);

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
                    o.PartSpacing = double.Parse(args[++i]);
                    break;

                case "--engines" when i + 1 < args.Length:
                    o.EngineNames = args[++i]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    break;

                case "--csv" when i + 1 < args.Length:
                    o.CsvPath = args[++i];
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

        foreach (var token in arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Size.TryParse(token, out var size))
                sizes.Add(size);
            else
                Console.Error.WriteLine($"Warning: could not parse sheet size '{token}', skipping");
        }

        return sizes;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("OpenNest.Benchmark - compare registered nesting engines on a set of .nest files");
        Console.Error.WriteLine();
        Console.Error.WriteLine("For each .nest file, every drawing with quantity > 0 is nested (mixed together),");
        Console.Error.WriteLine("once per registered engine. This is a full nest, not a single fixed-size plate:");
        Console.Error.WriteLine("as many plates as needed are created, one at a time, each sized by picking the");
        Console.Error.WriteLine("smallest candidate sheet size that fits the largest still-unplaced drawing -");
        Console.Error.WriteLine("applied identically to every engine, since Nest() itself has no say over its");
        Console.Error.WriteLine("own plate's size. Scoring: aggregate material utilization across every plate");
        Console.Error.WriteLine("used, then (if everything requested was placed) fewer plates as the tie-break.");
        Console.Error.WriteLine("An invalid layout (out of bounds, overlapping, or over-quantity) scores zero.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  OpenNest.Benchmark <file.nest | folder> [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --sheet-sizes W1xL1,W2xL2,...  Candidate sheet-size pool for the whole nest");
        Console.Error.WriteLine("                                 (default: the distinct sizes already in each file)");
        Console.Error.WriteLine("  --spacing <value>               Override part spacing for every job");
        Console.Error.WriteLine("  --engines Name1,Name2,...       Only benchmark these registered engines (default: all)");
        Console.Error.WriteLine("  --csv <path>                    Write a flat CSV of all results");
        Console.Error.WriteLine("  --help                          Show this message");
    }

    private class Options
    {
        public string InputPath;
        public List<Size> SheetSizes = new();
        public double? PartSpacing;
        public List<string> EngineNames = new();
        public string CsvPath;
    }
}
