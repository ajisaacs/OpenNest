using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using OpenNest;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bending;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;

return NestConsole.Run(args);

static class NestConsole
{
    public static int Run(string[] args)
    {
        var options = ParseArgs(args);

        if (options == null)
            return 0; // --help was requested

        if (
            options.RepairBendsMillimeters.HasValue
            && (
                options.CadUnits == BendRepairUnits.Unspecified
                || !double.IsFinite(options.RepairBendsMillimeters.Value)
                || options.RepairBendsMillimeters <= 0.001
                || options.RepairBendsMillimeters > 3.175
            )
        )
        {
            Console.Error.WriteLine(
                "Error: --repair-bends-mm requires a limit > 0.001 and <= 3.175 mm and --cad-units inches|mm."
            );
            return 1;
        }

        if (options.ListPosts)
        {
            ListPostProcessors(options);
            return 0;
        }

        // Validate --engine up front: autonest names a jobs engine, plain fill names a
        // single-plate placement strategy. Unknown names exit with the valid choices.
        if (options.AutoNest)
        {
            var isJobsEngine = NestingEngineRegistry.AvailableEngines.Any(e =>
                e.Name.Equals(options.Engine, StringComparison.OrdinalIgnoreCase)
            );
            if (!isJobsEngine)
            {
                Console.Error.WriteLine(
                    $"Error: unknown engine '{options.Engine}'. Jobs engines: {string.Join(", ", NestingEngineRegistry.AvailableEngines.Select(e => e.Name))}"
                );
                return 1;
            }
        }
        else
        {
            try
            {
                PlateFillService.ResolveStrategy(options.Engine);
            }
            catch (NotSupportedException)
            {
                Console.Error.WriteLine(
                    $"Error: unknown engine '{options.Engine}'. Fill strategies: {string.Join(", ", PlateFillService.BuiltInStrategies)} (jobs engines such as StockLadder require --autonest)"
                );
                return 1;
            }
        }

        if (options.InputFiles.Count == 0)
        {
            PrintUsage();
            return 1;
        }

        foreach (var f in options.InputFiles)
        {
            if (!File.Exists(f))
            {
                Console.Error.WriteLine($"Error: file not found: {f}");
                return 1;
            }
        }

        var nest = LoadOrCreateNest(options);

        if (nest == null)
            return 1;

        var plate = nest.Plates[options.PlateIndex];

        ApplyTemplate(plate, options);
        ApplyOverrides(plate, options);

        var drawing = ResolveDrawing(nest, options);

        if (drawing == null)
            return 1;

        var existingCount = plate.Parts.Count;

        if (!options.KeepParts)
            plate.Parts.Clear();

        PrintHeader(nest, plate, drawing, existingCount, options);

        var (success, elapsed) = Fill(nest, plate, drawing, options);

        var overlapCount = CheckOverlaps(plate, options);

        PrintResults(success, plate, elapsed);
        Save(nest, options);
        PostProcess(nest, options);

        return options.CheckOverlaps && overlapCount > 0 ? 1 : 0;
    }

    static Options ParseArgs(string[] args)
    {
        var o = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repair-bends-mm":
                    o.RepairBendsMillimeters =
                        i + 1 < args.Length
                        && double.TryParse(
                            args[++i],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var limit
                        )
                            ? limit
                            : double.NaN;
                    break;
                case "--cad-units" when i + 1 < args.Length:
                    o.CadUnits = args[++i] switch
                    {
                        "inches" => BendRepairUnits.Inches,
                        "mm" => BendRepairUnits.Millimeters,
                        _ => BendRepairUnits.Unspecified,
                    };
                    break;
                case "--drawing" when i + 1 < args.Length:
                    o.DrawingName = args[++i];
                    break;
                case "--plate" when i + 1 < args.Length:
                    o.PlateIndex = int.Parse(args[++i]);
                    break;
                case "--output" when i + 1 < args.Length:
                    o.OutputFile = args[++i];
                    break;
                case "--quantity" when i + 1 < args.Length:
                    o.Quantity = int.Parse(args[++i]);
                    break;
                case "--spacing" when i + 1 < args.Length:
                    o.Spacing = double.Parse(args[++i]);
                    break;
                case "--size" when i + 1 < args.Length:
                    if (Size.TryParse(args[++i], out var sz))
                        o.PlateSize = sz;
                    break;
                case "--check-overlaps":
                    o.CheckOverlaps = true;
                    break;
                case "--no-save":
                    o.NoSave = true;
                    break;
                case "--keep-parts":
                    o.KeepParts = true;
                    break;
                case "--template" when i + 1 < args.Length:
                    o.TemplateFile = args[++i];
                    break;
                case "--autonest":
                    o.AutoNest = true;
                    break;
                case "--engine" when i + 1 < args.Length:
                    o.Engine = args[++i];
                    break;
                case "--post" when i + 1 < args.Length:
                    o.PostName = args[++i];
                    break;
                case "--post-output" when i + 1 < args.Length:
                    o.PostOutput = args[++i];
                    break;
                case "--posts-dir" when i + 1 < args.Length:
                    o.PostsDir = args[++i];
                    break;
                case "--list-posts":
                    o.ListPosts = true;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    return null;
                default:
                    if (!args[i].StartsWith("--"))
                        o.InputFiles.Add(args[i]);
                    break;
            }
        }

        return o;
    }

    static Nest LoadOrCreateNest(Options options)
    {
        var nestFile = options.InputFiles.FirstOrDefault(f =>
            f.EndsWith(NestFormat.FileExtension, StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        );
        var dxfFiles = options
            .InputFiles.Where(f =>
                f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        // If we have a nest file, load it and optionally add DXFs.
        if (nestFile != null)
        {
            var nest = new NestReader(nestFile).Read();

            if (nest.Plates.Count == 0)
            {
                Console.Error.WriteLine("Error: nest file contains no plates");
                return null;
            }

            if (options.PlateIndex >= nest.Plates.Count)
            {
                Console.Error.WriteLine(
                    $"Error: plate index {options.PlateIndex} out of range (0-{nest.Plates.Count - 1})"
                );
                return null;
            }

            foreach (var dxf in dxfFiles)
            {
                var drawing = ImportDxf(dxf, options);

                if (drawing == null)
                    return null;

                nest.Drawings.Add(drawing);
                Console.WriteLine($"Imported: {drawing.Name}");
            }

            return nest;
        }

        // DXF-only mode: create a fresh nest.
        if (dxfFiles.Count == 0)
        {
            Console.Error.WriteLine("Error: no nest (.nest) or CAD (.dxf/.dwg) files specified");
            return null;
        }

        if (!options.PlateSize.HasValue)
        {
            Console.Error.WriteLine(
                "Error: --size WxL is required when importing DXF files without a nest"
            );
            return null;
        }

        var newNest = new Nest { Name = "DXF Import" };
        var plate = new Plate { Size = options.PlateSize.Value };
        newNest.Plates.Add(plate);

        foreach (var dxf in dxfFiles)
        {
            var drawing = ImportDxf(dxf, options);

            if (drawing == null)
                return null;

            newNest.Drawings.Add(drawing);
            Console.WriteLine($"Imported: {drawing.Name}");
        }

        return newNest;
    }

    static Drawing ImportDxf(string path, Options options)
    {
        try
        {
            var result = CadImporter.Import(
                path,
                new CadImportOptions
                {
                    BendRepair = options.RepairBendsMillimeters.HasValue
                        ? new BendRepairOptions
                        {
                            DrawingUnits = options.CadUnits,
                            MaxEndpointMovementMillimeters = options.RepairBendsMillimeters.Value,
                        }
                        : null,
                }
            );
            foreach (var report in result.BendRepairReports)
                Console.WriteLine(
                    $"Bend repair {Path.GetFileName(path)} #{report.BendIndex + 1}: {report.Status}: {report.Reason} ({report.OriginalStart} -> {report.Start}; {report.OriginalEnd} -> {report.End})"
                );
            return CadImporter.BuildDrawing(result, result.Entities, result.Bends, 1, null, null);
        }
        catch (System.Exception ex)
        {
            Console.Error.WriteLine($"Error: failed to import DXF '{path}': {ex.Message}");
            return null;
        }
    }

    static void ApplyTemplate(Plate plate, Options options)
    {
        if (options.TemplateFile == null)
            return;

        if (!File.Exists(options.TemplateFile))
        {
            Console.Error.WriteLine($"Error: Template not found: {options.TemplateFile}");
            return;
        }

        var templateNest = new NestReader(options.TemplateFile).Read();
        var templatePlate = templateNest.PlateDefaults.CreateNew();
        plate.Quadrant = templatePlate.Quadrant;
        plate.EdgeSpacing = templatePlate.EdgeSpacing;
        plate.PartSpacing = templatePlate.PartSpacing;
        Console.WriteLine($"Template: {options.TemplateFile}");
    }

    static void ApplyOverrides(Plate plate, Options options)
    {
        if (options.Spacing.HasValue)
            plate.PartSpacing = options.Spacing.Value;

        // Only apply size override when it wasn't already used to create the plate.
        var hasDxfOnly = !options.InputFiles.Any(f =>
            f.EndsWith(NestFormat.FileExtension, StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        );

        if (options.PlateSize.HasValue && !hasDxfOnly)
            plate.Size = options.PlateSize.Value;
    }

    static Drawing ResolveDrawing(Nest nest, Options options)
    {
        var drawing =
            options.DrawingName != null
                ? nest.Drawings.FirstOrDefault(d => d.Name == options.DrawingName)
                : nest.Drawings.FirstOrDefault();

        if (drawing != null)
            return drawing;

        Console.Error.WriteLine(
            options.DrawingName != null
                ? $"Error: drawing '{options.DrawingName}' not found. Available: {string.Join(", ", nest.Drawings.Select(d => d.Name))}"
                : "Error: nest file contains no drawings"
        );

        return null;
    }

    static void PrintHeader(
        Nest nest,
        Plate plate,
        Drawing drawing,
        int existingCount,
        Options options
    )
    {
        Console.WriteLine($"Nest: {nest.Name}");
        var wa = plate.WorkArea();
        Console.WriteLine(
            $"Plate: {options.PlateIndex} ({plate.Size.Width:F1} x {plate.Size.Length:F1}), spacing={plate.PartSpacing:F2}, edge=({plate.EdgeSpacing.Left},{plate.EdgeSpacing.Bottom},{plate.EdgeSpacing.Right},{plate.EdgeSpacing.Top}), workArea={wa.Width:F1}x{wa.Length:F1}"
        );
        Console.WriteLine($"Drawing: {drawing.Name}");
        Console.WriteLine(
            options.KeepParts
                ? $"Keeping {existingCount} existing parts"
                : $"Cleared {existingCount} existing parts"
        );
        Console.WriteLine("---");
    }

    static (bool success, long elapsedMs) Fill(
        Nest nest,
        Plate plate,
        Drawing drawing,
        Options options
    )
    {
        var sw = Stopwatch.StartNew();
        bool success;

        if (options.AutoNest)
        {
            var nestItems = new List<NestItem>();
            var qty = options.Quantity > 0 ? options.Quantity : 1;

            if (options.DrawingName != null)
            {
                nestItems.Add(new NestItem { Drawing = drawing, Quantity = qty });
            }
            else
            {
                foreach (var d in nest.Drawings)
                    nestItems.Add(new NestItem { Drawing = d, Quantity = qty });
            }

            Console.WriteLine(
                $"AutoNest: {nestItems.Count} drawing(s), {nestItems.Sum(i => i.Quantity)} total parts"
            );

            success = AutoNestJob(plate, nestItems, options.Engine);
        }
        else
        {
            // Single-plate fill: explicit placement strategy through the public service;
            // the process-global engine registry is never consulted.
            var strategy = ResolveFillStrategy(options.Engine);
            var item = new NestItem { Drawing = drawing, Quantity = options.Quantity };
            var parts = PlateFillService.FillItem(
                strategy,
                plate,
                item,
                plate.WorkArea(),
                null,
                CancellationToken.None
            );

            if (parts.Count > 0)
                plate.Parts.AddRange(parts);
            success = parts.Count > 0;
        }

        sw.Stop();
        return (success, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Solves the drawings as one whole job against this single plate using the named jobs
    /// engine, then commits the returned placements onto the plate. Placements are mapped back
    /// onto the caller's original drawings (same pose semantics as NestResultMaterializer), so
    /// the saved nest keeps its existing drawing identities.
    /// </summary>
    static bool AutoNestJob(Plate plate, List<NestItem> nestItems, string engineName)
    {
        var engine = NestingEngineRegistry.Create(engineName);

        var parts = new List<NestJobPart>(nestItems.Count);
        var drawingsByPartId = new Dictionary<string, Drawing>(StringComparer.Ordinal);
        for (var i = 0; i < nestItems.Count; i++)
        {
            var partId = $"part-{i}";
            parts.Add(DrawingJobMapper.FromItem(partId, nestItems[i]));
            drawingsByPartId[partId] = nestItems[i].Drawing;
        }

        // One physical sheet: this plate, this solve — the runner owns stock accounting.
        var stock = DrawingJobMapper.FromPlate("plate-0", plate, 1);
        var job = new NestJob(parts, [stock]);

        var result = engine.Solve(job, null, CancellationToken.None);

        var committed = 0;
        foreach (var plateResult in result.Plates)
        {
            foreach (var pose in plateResult.Placements)
            {
                if (!drawingsByPartId.TryGetValue(pose.PartId, out var drawing))
                    continue;
                var part = new Part(drawing);
                part.Rotate(pose.Rotation);
                part.Location = new Vector(pose.X, pose.Y);
                part.UpdateBounds();
                plate.Parts.Add(part);
                committed++;
            }
        }

        Console.WriteLine($"Engine: {engineName} — committed {committed} placements");
        return committed > 0;
    }

    static string ResolveFillStrategy(string engineName)
    {
        try
        {
            return PlateFillService.ResolveStrategy(engineName);
        }
        catch (NotSupportedException)
        {
            var isJobEngine = NestingEngineRegistry.AvailableEngines.Any(e =>
                e.Name.Equals(engineName, StringComparison.OrdinalIgnoreCase)
            );
            Console.Error.WriteLine(
                isJobEngine
                    ? $"Error: engine '{engineName}' is a whole-job engine; single-plate fill supports: {string.Join(", ", PlateFillService.BuiltInStrategies)}. Use --autonest for whole-job engines."
                    : $"Error: unknown engine '{engineName}'. Engines: {string.Join(", ", NestingEngineRegistry.AvailableEngines.Select(e => e.Name))}"
            );
            Environment.Exit(1);
            throw; // unreachable
        }
    }

    static int CheckOverlaps(Plate plate, Options options)
    {
        if (!options.CheckOverlaps || plate.Parts.Count == 0)
            return 0;

        var hasOverlaps = plate.HasOverlappingParts(out var overlapPts);
        Console.WriteLine(
            hasOverlaps
                ? $"OVERLAPS DETECTED: {overlapPts.Count} intersection points"
                : "Overlap check: PASS"
        );

        return overlapPts.Count;
    }

    static void PrintResults(bool success, Plate plate, long elapsedMs)
    {
        Console.WriteLine($"Result: {(success ? "success" : "failed")}");
        Console.WriteLine($"Parts placed: {plate.Parts.Count}");
        Console.WriteLine($"Utilization: {plate.Utilization():P1}");
        Console.WriteLine($"Time: {elapsedMs}ms");
    }

    static void Save(Nest nest, Options options)
    {
        if (options.NoSave)
            return;

        var firstInput = options.InputFiles[0];
        var outputFile =
            options.OutputFile
            ?? Path.Combine(
                Path.GetDirectoryName(firstInput),
                $"{Path.GetFileNameWithoutExtension(firstInput)}-result{NestFormat.FileExtension}"
            );

        new NestWriter(nest).Write(outputFile);
        Console.WriteLine($"Saved: {outputFile}");
    }

    static string ResolvePostsDir(Options options)
    {
        if (options.PostsDir != null)
            return options.PostsDir;

        var exePath =
            Assembly.GetEntryAssembly()?.Location ?? typeof(NestConsole).Assembly.Location;
        return Path.Combine(Path.GetDirectoryName(exePath), "Posts");
    }

    static List<IPostProcessor> LoadPostProcessors(string postsDir)
    {
        var processors = new List<IPostProcessor>();

        if (!Directory.Exists(postsDir))
            return processors;

        foreach (var file in Directory.GetFiles(postsDir, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(file);

                foreach (var type in assembly.GetTypes())
                {
                    if (
                        !typeof(IPostProcessor).IsAssignableFrom(type)
                        || type.IsInterface
                        || type.IsAbstract
                    )
                        continue;

                    if (Activator.CreateInstance(type) is IPostProcessor processor)
                        processors.Add(processor);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Warning: failed to load post processor from {Path.GetFileName(file)}: {ex.Message}"
                );
            }
        }

        return processors;
    }

    static void ListPostProcessors(Options options)
    {
        var postsDir = ResolvePostsDir(options);
        var processors = LoadPostProcessors(postsDir);

        if (processors.Count == 0)
        {
            Console.WriteLine($"No post processors found in: {postsDir}");
            return;
        }

        Console.WriteLine($"Post processors ({postsDir}):");

        foreach (var p in processors)
            Console.WriteLine($"  {p.Name, -30} {p.Description}");
    }

    static void PostProcess(Nest nest, Options options)
    {
        if (options.PostName == null)
            return;

        var postsDir = ResolvePostsDir(options);
        var processors = LoadPostProcessors(postsDir);
        var post = processors.FirstOrDefault(p =>
            p.Name.Equals(options.PostName, StringComparison.OrdinalIgnoreCase)
        );

        if (post == null)
        {
            Console.Error.WriteLine($"Error: post processor '{options.PostName}' not found");

            if (processors.Count > 0)
                Console.Error.WriteLine(
                    $"Available: {string.Join(", ", processors.Select(p => p.Name))}"
                );
            else
                Console.Error.WriteLine($"No post processors found in: {postsDir}");

            return;
        }

        var outputFile = options.PostOutput;

        if (outputFile == null)
        {
            var firstInput = options.InputFiles[0];
            outputFile = Path.Combine(
                Path.GetDirectoryName(firstInput),
                $"{Path.GetFileNameWithoutExtension(firstInput)}.cnc"
            );
        }

        post.Post(nest, outputFile);
        Console.WriteLine($"Post: {post.Name} -> {outputFile}");
    }

    static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: OpenNest.Console <input-files...> [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Arguments:");
        Console.Error.WriteLine(
            "  input-files            One or more .nest nest files or .dxf/.dwg drawing files"
        );
        Console.Error.WriteLine();
        Console.Error.WriteLine("Modes:");
        Console.Error.WriteLine("  <nest.nest>             Load nest and fill (existing behavior)");
        Console.Error.WriteLine("  <part.dxf> --size WxL     Import DXF, create plate, and fill");
        Console.Error.WriteLine(
            "  <nest.nest> <part.dxf>  Load nest and add imported DXF drawings"
        );
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine(
            "  --repair-bends-mm <n>   Opt-in endpoint/tick repair, limit >0.001 to 3.175 physical mm"
        );
        Console.Error.WriteLine(
            "  --cad-units inches|mm  Explicit source coordinate units required for bend repair"
        );
        Console.Error.WriteLine(
            "  --drawing <name>       Drawing name to fill with (default: first drawing)"
        );
        Console.Error.WriteLine("  --plate <index>        Plate index to fill (default: 0)");
        Console.Error.WriteLine(
            "  --quantity <n>          Max parts to place (default: 0 = unlimited)"
        );
        Console.Error.WriteLine("  --spacing <value>      Override part spacing");
        Console.Error.WriteLine(
            "  --size <WxL>           Override plate size (e.g. 60x120); required for DXF-only mode"
        );
        Console.Error.WriteLine(
            "  --output <path>        Output nest file path (default: <input>-result.nest)"
        );
        Console.Error.WriteLine(
            "  --template <path>      Nest template for plate defaults (thickness, quadrant, material, spacing)"
        );
        Console.Error.WriteLine(
            "  --autonest             Whole-job nesting via the jobs engine (--engine) instead of single-plate fill"
        );
        Console.Error.WriteLine(
            "  --engine <name>        With --autonest: jobs engine (default: Default; also StockLadder, Strip, ...)."
        );
        Console.Error.WriteLine(
            "                         Without --autonest: fill strategy (Default, Strip, Vertical Remnant, Horizontal Remnant)"
        );
        Console.Error.WriteLine(
            "  --keep-parts           Don't clear existing parts before filling"
        );
        Console.Error.WriteLine(
            "  --check-overlaps       Run overlap detection after fill (exit code 1 if found)"
        );
        Console.Error.WriteLine("  --no-save              Skip saving output file");
        Console.Error.WriteLine("  --post <name>          Run a post processor after nesting");
        Console.Error.WriteLine(
            "  --post-output <path>   Output file for post processor (default: <input>.cnc)"
        );
        Console.Error.WriteLine(
            "  --posts-dir <path>     Directory containing post processor DLLs (default: Posts/)"
        );
        Console.Error.WriteLine("  --list-posts           List available post processors and exit");
        Console.Error.WriteLine("  -h, --help             Show this help");
    }

    class Options
    {
        public List<string> InputFiles = new();
        public string DrawingName;
        public int PlateIndex;
        public string OutputFile;
        public int Quantity;
        public double? Spacing;
        public Size? PlateSize;
        public bool CheckOverlaps;
        public bool NoSave;
        public bool KeepParts;
        public bool AutoNest;
        public string Engine = "Default";
        public string TemplateFile;
        public string PostName;
        public string PostOutput;
        public string PostsDir;
        public bool ListPosts;
        public double? RepairBendsMillimeters;
        public BendRepairUnits CadUnits;
    }
}
