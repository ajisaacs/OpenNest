using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using OpenNest;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bending;

return NestConsole.Run(args);

static class NestConsole
{
    public static int Run(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            return RunCore(args, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Nesting canceled; nothing saved.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    static int RunCore(string[] args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        NestingEngineRegistry.LoadPlugins(Path.Combine(AppContext.BaseDirectory, "Engines"));
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

        if (!options.AutoNest && (options.MaxSheets != 1 || options.Stock != null))
        {
            Console.Error.WriteLine("Error: --max-sheets and --stock require --autonest.");
            return 1;
        }

        if (options.MaxSheets < 1 || options.Stock is { Count: 0 })
        {
            Console.Error.WriteLine(
                "Error: --max-sheets needs a whole number >= 1 and --stock needs WxL[:cost] sizes separated by commas (for example 60x120,48x96:85)."
            );
            return 1;
        }

        // Validate --engine up front: autonest names a jobs engine, plain fill names a
        // single-plate placement strategy. Unknown names exit with the valid choices.
        if (options.AutoNest)
        {
            // ResolveName also accepts renamed engines' old names (for example Opus55NestingEngine).
            if (NestingEngineRegistry.ResolveName(options.Engine) == null)
            {
                Console.Error.WriteLine(
                    $"Error: unknown engine '{options.Engine}'. Jobs engines: {string.Join(", ", NestingEngineRegistry.AvailableEngines.Select(e => e.Name))}"
                );
                return 1;
            }
        }
        else
        {
            var engine = EngineNameResolver.Classify(options.Engine);
            if (!engine.IsFillStrategy)
            {
                Console.Error.WriteLine(
                    engine.IsJobEngine
                        ? $"Error: engine '{engine.Name}' is a whole-job engine; single-plate fill supports: {string.Join(", ", PlateFillService.BuiltInStrategies)}. Use --autonest for whole-job engines."
                        : $"Error: unknown engine '{engine.Name}'. Fill strategies: {string.Join(", ", PlateFillService.BuiltInStrategies)} (whole-job engines such as Irregular require --autonest)"
                );
                return 1;
            }
        }

        if (!options.PlanCutting && (options.CuttingParamsFile != null || options.KeepPartOrder
            || options.AcceptUnverifiedPlan || options.AllPlates))
        {
            Console.Error.WriteLine(
                "Error: --cutting-params, --keep-part-order, --accept-unverified-plan and --all-plates require --plan-cutting."
            );
            return 1;
        }

        if (options.NoFill && (options.AutoNest || options.KeepParts || options.DrawingName != null
            || options.Quantity != 0 || options.Spacing.HasValue || options.PlateSize.HasValue
            || options.OutlineFile != null || options.TemplateFile != null || options.InputFiles.Any(IsCadFile)))
        {
            Console.Error.WriteLine(
                "Error: --no-fill keeps the nest as loaded; it cannot be combined with CAD inputs, --autonest, --keep-parts, --drawing, --quantity, --spacing, --size, --outline or --template."
            );
            return 1;
        }

        if (options.OutlineFile != null && !options.AutoNest)
        {
            Console.Error.WriteLine("Error: --outline needs --autonest; single-plate fill strategies fill rectangles only.");
            return 1;
        }

        if (options.OutlineFile != null && !File.Exists(options.OutlineFile))
        {
            Console.Error.WriteLine($"Error: Outline file not found: {options.OutlineFile}");
            return 1;
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

        // Read explicit settings before nesting, so a bad file fails without wasting the fill.
        CuttingParameters cuttingParameters = null;
        if (options.CuttingParamsFile != null)
        {
            try
            {
                cuttingParameters = CuttingParametersSerializer.DeserializeExplicit(
                    File.ReadAllText(options.CuttingParamsFile));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                or NotSupportedException)
            {
                Console.Error.WriteLine($"Error: cannot read cutting settings '{options.CuttingParamsFile}': {ex.Message}");
                return 1;
            }
        }

        var nest = LoadOrCreateNest(options);

        if (nest == null)
            return 1;

        var plate = nest.Plates[options.PlateIndex];
        IReadOnlyList<Plate> plates = new[] { plate };
        var overlapCount = 0;

        if (options.NoFill)
        {
            Console.WriteLine($"Nest: {nest.Name}");
            overlapCount = CheckOverlaps(nest, plates, options);
        }
        else
        {
            if (options.AutoNest && options.KeepParts && plate.Parts.Count > 0)
            {
                Console.Error.WriteLine("Error: --autonest --keep-parts cannot use an occupied plate. Use plain fill for existing obstacles.");
                return 2;
            }

            ApplyTemplate(plate, options);
            ApplyOverrides(plate, options);

            if (!options.AutoNest && PlateFillService.Refusal(plate) is { } refusal)
            {
                Console.Error.WriteLine($"Error: {refusal} Use --autonest.");
                return 2;
            }

            var drawing = ResolveDrawing(nest, options);

            if (drawing == null)
                return 1;

            var existingCount = plate.Parts.Count;

            if (!options.AutoNest && !options.KeepParts)
                plate.Parts.Clear();

            PrintHeader(nest, plate, drawing, existingCount, options);

            var (success, elapsed, accepted, filled) = Fill(nest, plate, drawing, options, token);
            if (!accepted)
                return 2; // No save or post may run after a rejected proposal.

            plates = filled;
            overlapCount = CheckOverlaps(nest, plates, options);

            PrintResults(success, nest, plates, elapsed);
        }

        token.ThrowIfCancellationRequested();
        if (options.PlanCutting && !PlanCutting(nest, options.AllPlates ? nest.Plates : plates,
            cuttingParameters, options, token))
            return 2; // Like a rejected nest: nothing is saved or posted.
        if (!SaveAndPost(nest, options))
            return 1;

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
                case "--outline" when i + 1 < args.Length:
                    o.OutlineFile = args[++i];
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
                case "--allow-invalid":
                    o.AllowInvalid = true;
                    break;
                case "--autonest":
                    o.AutoNest = true;
                    break;
                case "--max-sheets" when i + 1 < args.Length:
                    o.MaxSheets = int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sheets)
                        ? sheets
                        : 0;
                    break;
                case "--stock" when i + 1 < args.Length:
                    o.Stock = ParseStock(args[++i]);
                    break;
                case "--engine" when i + 1 < args.Length:
                    o.Engine = args[++i];
                    break;
                case "--post" when i + 1 < args.Length:
                    o.PostName = args[++i];
                    break;
                case "--acknowledge-post-risks":
                    o.AcknowledgePostRisks = true;
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
                case "--no-fill":
                    o.NoFill = true;
                    break;
                case "--plan-cutting":
                    o.PlanCutting = true;
                    break;
                case "--cutting-params" when i + 1 < args.Length:
                    o.CuttingParamsFile = args[++i];
                    break;
                case "--keep-part-order":
                    o.KeepPartOrder = true;
                    break;
                case "--accept-unverified-plan":
                    o.AcceptUnverifiedPlan = true;
                    break;
                case "--all-plates":
                    o.AllPlates = true;
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

    /// <summary>Parses "WxL[:cost],..."; any malformed entry returns an empty list, which validation rejects.</summary>
    static List<PlateOption> ParseStock(string value)
    {
        var stock = new List<PlateOption>();
        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            var cost = 0.0;
            if (
                parts.Length > 2
                || !Size.TryParse(parts[0], out var size)
                || !double.IsFinite(size.Width)
                || !double.IsFinite(size.Length)
                || size.Width <= 0
                || size.Length <= 0
                || (
                    parts.Length == 2
                    && (
                        !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out cost)
                        || !double.IsFinite(cost)
                        || cost <= 0
                    )
                )
            )
                return new List<PlateOption>();

            stock.Add(new PlateOption { Width = size.Width, Length = size.Length, Cost = cost });
        }

        return stock;
    }

    static bool IsCadFile(string path) =>
        path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase);

    static Nest LoadOrCreateNest(Options options)
    {
        var nestFile = options.InputFiles.FirstOrDefault(f =>
            f.EndsWith(NestFormat.FileExtension, StringComparison.OrdinalIgnoreCase)
            || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        );
        var dxfFiles = options.InputFiles.Where(IsCadFile).ToList();

        // If we have a nest file, load it and optionally add DXFs.
        if (nestFile != null)
        {
            var nest = new NestReader(nestFile) { BestFitStore = BestFitCacheStore.Shared }.Read();

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

        // Build the plate as the desktop does: Quantity = 1 so part counts and the
        // drawings' nested quantities include it.
        var newNest = new Nest { Name = "DXF Import" };
        newNest.PlateDefaults.Size = options.PlateSize.Value;
        newNest.CreatePlate();

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

        var templateNest = new NestReader(options.TemplateFile) { BestFitStore = BestFitCacheStore.Shared }.Read();
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

        if (options.OutlineFile != null)
        {
            // The outline is the sheet: its bounds replace any size given.
            plate.Outline = SheetOutline.FromGeometry(CadImporter.Import(options.OutlineFile).Entities);
            Console.WriteLine($"Outline: {options.OutlineFile} ({plate.Outline})");
        }
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
        var existingPartsMessage = options.KeepParts
            ? $"Keeping {existingCount} existing parts"
            : options.AutoNest
                ? $"Will replace {existingCount} existing parts only after acceptance"
                : $"Cleared {existingCount} existing parts";
        Console.WriteLine(
            $"""
            Drawing: {drawing.Name}
            {existingPartsMessage}
            ---
            """
        );
    }

    static (bool success, long elapsedMs, bool accepted, IReadOnlyList<Plate> plates) Fill(
        Nest nest,
        Plate plate,
        Drawing drawing,
        Options options,
        CancellationToken token
    )
    {
        var sw = Stopwatch.StartNew();
        bool success;
        var accepted = true;
        IReadOnlyList<Plate> plates = new[] { plate };

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

            var outcome = AutoNestJob(nest, plate, nestItems, options, token);
            accepted = outcome.accepted;
            success = outcome.committed > 0;
            if (accepted)
                plates = outcome.plates;
        }
        else
        {
            // Single-plate fill: explicit placement strategy through the public service;
            // the process-global engine registry is never consulted.
            var strategy = EngineNameResolver.Classify(options.Engine).FillStrategy;
            var item = new NestItem { Drawing = drawing, Quantity = options.Quantity };
            var parts = PlateFillService.FillItem(
                strategy,
                plate,
                item,
                plate.WorkArea(),
                null,
                token
            );
            token.ThrowIfCancellationRequested();

            if (parts.Count > 0)
                plate.Parts.AddRange(parts);
            success = parts.Count > 0;
        }

        sw.Stop();
        return (success, sw.ElapsedMilliseconds, accepted, plates);
    }

    static (bool accepted, int committed, IReadOnlyList<Plate> plates) AutoNestJob(
        Nest nest, Plate plate, List<NestItem> nestItems, Options options, CancellationToken token)
    {
        var rejected = (false, 0, (IReadOnlyList<Plate>)Array.Empty<Plate>());
        // One sheet of the target's size keeps the original single-sheet stock exactly;
        // otherwise the target is the template for every offered size, unlimited per size
        // and capped by --max-sheets physical sheets in total.
        var singleSheet = options.MaxSheets == 1 && options.Stock == null;
        var stock = singleSheet
            ? NestStockBuilder.SinglePlate(plate)
            : NestStockBuilder.FromTemplate(plate, options.Stock);
        var result = NestPipeline.Run(new NestPipelineRequest(
            options.Engine, nestItems, stock,
            singleSheet ? null : new NestJobOptions(maxPlates: options.MaxSheets)), token: token);
        foreach (var violation in result.Violations)
            Console.Error.WriteLine($"Violation: {violation}");
        foreach (var unplaced in result.Unplaced)
            Console.Error.WriteLine($"Unplaced: {unplaced.Describe()}");

        var tooManySheets = result.Raw.Plates.Count > options.MaxSheets;
        if (!result.CanKeep || tooManySheets || (!result.IsValid && !options.AllowInvalid))
        {
            Console.Error.WriteLine(tooManySheets
                ? $"Error: result uses {result.Raw.Plates.Count} sheets, more than --max-sheets {options.MaxSheets}, even with --allow-invalid. Nothing saved."
                : !result.CanKeep
                    ? "Error: result cannot be represented faithfully, even with --allow-invalid. Nothing saved."
                    : "Error: invalid result discarded. Use --allow-invalid to explicitly keep its violations. Nothing saved.");
            return rejected;
        }

        token.ThrowIfCancellationRequested();
        var committed = result.Plates.Sum(p => p.Parts.Count * p.Quantity);
        if (committed == 0)
        {
            Console.Error.WriteLine("Error: no placements returned. Existing layout and output left unchanged.");
            return rejected;
        }

        var costs = result.IsValid && options.Stock != null ? NestCostSummary.FromAccepted(result) : null;

        // The first layout replaces the target's parts; further layouts become new sheets
        // right after it, carrying the target's cutting settings. Other plates are unchanged.
        var insertAt = nest.Plates.IndexOf(plate) + 1;
        var targetUsed = false;
        var applied = NestPipelineCommit.ApplyToPlates(result, () =>
        {
            if (!targetUsed)
            {
                targetUsed = true;
                plate.Parts.Clear();
                return plate;
            }

            var created = new Plate(plate.Size)
            {
                GrainAngle = plate.GrainAngle,
                CuttingParameters = plate.CuttingParameters,
            };
            nest.Plates.Insert(insertAt++, created);
            return created;
        }, options.AllowInvalid, token);

        Console.WriteLine($"Engine: {options.Engine} — committed {committed} placements on {applied.Sum(p => p.Quantity)} sheet(s)");
        if (costs != null)
            Console.WriteLine($"Cost ({costs.Basis}): gross {costs.GrossTotal:F2}, salvage credit {costs.SalvageCredit:F2}, net {costs.NetScore:F2}");
        return (true, committed, applied);
    }

    static int CheckOverlaps(Nest nest, IReadOnlyList<Plate> plates, Options options)
    {
        if (!options.CheckOverlaps)
            return 0;

        var total = 0;
        foreach (var plate in plates.Where(p => p.Parts.Count > 0))
        {
            var hasOverlaps = plate.HasOverlappingParts(out var overlapPts);
            var label = plates.Count > 1 ? $"Plate {nest.Plates.IndexOf(plate)}: " : "";
            Console.WriteLine(
                hasOverlaps
                    ? $"{label}OVERLAPS DETECTED: {overlapPts.Count} intersection points"
                    : $"{label}Overlap check: PASS"
            );
            total += overlapPts.Count;
        }

        return total;
    }

    static void PrintResults(bool success, Nest nest, IReadOnlyList<Plate> plates, long elapsedMs)
    {
        if (plates.Count > 1)
        {
            foreach (var plate in plates)
                Console.WriteLine(
                    $"Plate {nest.Plates.IndexOf(plate)}: {plate.Size.Width:F1} x {plate.Size.Length:F1} x{plate.Quantity}, {plate.Parts.Count} parts, utilization {plate.Utilization():P1}"
                );
        }

        var area = plates.Sum(p => p.Size.Width * p.Size.Length * p.Quantity);
        var utilization = area > 0
            ? plates.Sum(p => p.Utilization() * p.Size.Width * p.Size.Length * p.Quantity) / area
            : 0;
        Console.WriteLine(
            $"""
            Result: {(success ? "success" : "failed")}
            Parts placed: {plates.Sum(p => p.Parts.Count * p.Quantity)}
            Utilization: {utilization:P1}
            Time: {elapsedMs}ms
            """
        );
    }

    /// <summary>
    /// Plans and applies cutting (part order, contour order, entries and lead-ins) on the
    /// occupied plates in scope, all or nothing. Settings are the explicit file or the plates'
    /// own saved settings; none are invented. Unverified plans apply only with per-invocation
    /// acceptance, which is never consent to post.
    /// </summary>
    static bool PlanCutting(Nest nest, IEnumerable<Plate> plates, CuttingParameters explicitParameters,
        Options options, CancellationToken token)
    {
        var scope = plates.Where(p => p.Parts.Count > 0).ToArray();
        if (scope.Length == 0)
        {
            Console.Error.WriteLine("Error: no occupied plates to plan cutting for. Nothing saved.");
            return false;
        }

        var parameters = explicitParameters ?? SavedCuttingParameters(scope);
        if (parameters == null)
        {
            Console.Error.WriteLine(
                "Error: --plan-cutting needs confirmed cutting settings: pass --cutting-params <settings.json>, or plan plates that all carry the same saved settings. Nothing saved."
            );
            return false;
        }

        Console.WriteLine("---");
        Console.WriteLine($"Cutting settings: {(explicitParameters != null ? options.CuttingParamsFile : "saved with the plates")}");
        var batch = CuttingPlanBatch.Capture(scope, parameters, options.KeepPartOrder,
            scope.Select(p => nest.Plates.IndexOf(p)).ToArray(), token);
        var proposal = batch.Plan(new PlanningProgress(scope.Length), token);
        token.ThrowIfCancellationRequested();

        foreach (var line in proposal.Describe(UnitsHelper.GetShortString(nest.Units),
            "Pass --cutting-params settings that select"))
            Console.WriteLine(line);

        if (!(proposal.CanApply || options.AcceptUnverifiedPlan && proposal.CanApplyWithWarnings))
        {
            Console.Error.WriteLine(proposal.RequiresWarningAcceptance
                ? "Cutting plan not applied: it is unverified. Review the warnings above; to apply it anyway, explicitly use --accept-unverified-plan for this invocation. Nothing saved."
                : "Cutting plan not applied: every plate must be ready. Nothing saved.");
            return false;
        }

        var commit = proposal.Apply(options.AcceptUnverifiedPlan, token);
        if (commit.Status != CuttingCommitStatus.Applied)
        {
            Console.Error.WriteLine($"Error: cutting plan not applied ({commit.Status}): {commit.Message} Nothing saved.");
            return false;
        }

        Console.WriteLine($"Cutting plan applied to {scope.Length} plate(s).");
        return true;
    }

    /// <summary>The plates' saved settings when every plate carries the same ones; otherwise null.</summary>
    static CuttingParameters SavedCuttingParameters(IReadOnlyList<Plate> plates)
    {
        var first = plates[0].CuttingParameters;
        if (first == null)
            return null;
        var json = CuttingParametersSerializer.Serialize(first);
        return plates.All(p => p.CuttingParameters != null
            && CuttingParametersSerializer.Serialize(p.CuttingParameters) == json) ? first : null;
    }

    /// <summary>Reports synchronously, so phase lines print in order with the summary.</summary>
    sealed class PlanningProgress(int plateCount) : IProgress<CuttingPlanProgress>
    {
        public void Report(CuttingPlanProgress value) => Console.WriteLine(
            (plateCount > 1 ? $"Planning plate {value.PlateNumber}: " : "Planning: ") + value.Phase switch
            {
                CuttingPlanPhase.Reordering => "finding a part order...",
                CuttingPlanPhase.KeepingOrder => "keeping the current order...",
                CuttingPlanPhase.SafestRoute => "finding the safest route...",
                CuttingPlanPhase.CheckingOverlap => "checking for overlaps...",
                _ => value.Phase + "...",
            });
    }

    static void Save(Nest nest, Options options)
    {
        if (options.NoSave)
            return;

        var outputFile = NestOutputPath(options);

        new NestWriter(nest) { BestFitStore = BestFitCacheStore.Shared }.Write(outputFile);
        Console.WriteLine($"Saved: {outputFile}");
    }

    static string NestOutputPath(Options options) => options.OutputFile
        ?? Path.Combine(Path.GetDirectoryName(options.InputFiles[0]),
            $"{Path.GetFileNameWithoutExtension(options.InputFiles[0])}-result{NestFormat.FileExtension}");

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
            Console.WriteLine($"  {p.Name,-30} {p.Description}");
    }

    static bool SaveAndPost(Nest nest, Options options)
    {
        if (options.PostName == null)
        {
            Save(nest, options);
            return true;
        }

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

            return false;
        }

        var verification = PostVerificationAnalyzer.AnalyzeForPost(nest, post);
        Console.WriteLine(verification.ToDisplayText());
        if (!verification.CanPost(options.AcknowledgePostRisks))
        {
            Console.Error.WriteLine("Posting blocked: review these warnings. To accept the risks, including possible head crashes and machine or material damage, explicitly use --acknowledge-post-risks for this invocation.");
            return false;
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

        var outputFiles = post is IMultiFilePostProcessor multiFile
            ? multiFile.GetOutputFiles(nest, outputFile)
            : new[] { outputFile };

        if (!options.NoSave && outputFiles.Any(file => string.Equals(
            Path.GetFullPath(file), Path.GetFullPath(NestOutputPath(options)), StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Error: nest save and CNC output paths must be different. No output was written.");
            return false;
        }

        Save(nest, options);
        post.Post(nest, outputFile);
        foreach (var file in outputFiles)
            Console.WriteLine($"Post: {post.Name} -> {file}");
        return true;
    }

    static void PrintUsage()
    {
        Console.Error.WriteLine(
            """
            Usage: OpenNest.Console <input-files...> [options]

            Arguments:
              input-files            One or more .nest nest files or .dxf/.dwg drawing files

            Modes:
              <nest.nest>             Load nest and fill (existing behavior)
              <part.dxf> --size WxL     Import DXF, create plate, and fill
              <nest.nest> <part.dxf>  Load nest and add imported DXF drawings

            Options:
              --repair-bends-mm <n>   Opt-in endpoint/tick repair, limit >0.001 to 3.175 physical mm
              --cad-units inches|mm  Explicit source coordinate units required for bend repair
              --drawing <name>       Drawing name to fill with (default: first drawing)
              --plate <index>        Plate index to fill (default: 0)
              --quantity <n>          Max parts to place (default: 0 = unlimited)
              --spacing <value>      Override part spacing
              --size <WxL>           Override plate size (e.g. 60x120); required for DXF-only mode
              --outline <dxf>        Make the plate an irregular sheet: the file's largest closed contour is
                                     the sheet, other closed contours are obstacles; sets the size. Only
                                     --autonest with the Irregular (or Default) engine nests on it
              --output <path>        Output nest file path (default: <input>-result.nest)
              --template <path>      Nest template for plate defaults (thickness, quadrant, material, spacing)
              --autonest             Validated whole-job nesting (one sheet unless --max-sheets); replaces only after acceptance
              --max-sheets <n>       With --autonest: physical sheets the job may use (default: 1); extra
                                     layouts are inserted as new plates after the target plate
              --stock <WxL[:cost],...>  With --autonest: sheet sizes to offer, each unlimited (default: the
                                     target plate's size); spacing, quadrant and clamps follow the target
              --allow-invalid        Explicitly keep representable invalid autonest layouts (default: reject, exit 2)
              --engine <name>        With --autonest: jobs engine (default: Default; also Irregular, Rectangles, Fill, ...).
                                     Without --autonest: fill strategy (Fill (default), Strip, Vertical Remnant, Horizontal Remnant)
              --keep-parts           Don't clear existing parts before filling
              --no-fill              Keep the loaded .nest as it is (no fill); plan, save or post it
              --plan-cutting         After filling (or loading with --no-fill), plan cutting (part order,
                                     contour order, entries, lead-ins); applied only when every plate is
                                     ready or --accept-unverified-plan is given (else exit 2)
              --cutting-params <path>  Cutting settings JSON for --plan-cutting (default: the plates' own
                                     saved settings, which must agree; never invented)
              --keep-part-order      With --plan-cutting: keep the current whole-part order
              --all-plates           With --plan-cutting: plan every occupied plate, not just the target
              --accept-unverified-plan  Explicitly apply an unverified cutting plan for this invocation
              --check-overlaps       Run overlap detection after fill (exit code 1 if found)
              --no-save              Skip saving output file
              --post <name>          Run a post processor after nesting
              --post-output <path>   Output file for post processor (default: <input>.cnc)
              --acknowledge-post-risks  Explicitly accept displayed verification risks for this invocation
              --posts-dir <path>     Directory containing post processor DLLs (default: Posts/)
              --list-posts           List available post processors and exit
              -h, --help             Show this help
            """
        );
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
        public string OutlineFile;
        public bool CheckOverlaps;
        public bool NoSave;
        public bool KeepParts;
        public bool AutoNest;
        public bool AllowInvalid;
        public int MaxSheets = 1;
        public List<PlateOption> Stock;
        public string Engine = "Default";
        public string TemplateFile;
        public string PostName;
        public bool AcknowledgePostRisks;
        public string PostOutput;
        public string PostsDir;
        public bool ListPosts;
        public bool NoFill;
        public bool PlanCutting;
        public string CuttingParamsFile;
        public bool KeepPartOrder;
        public bool AcceptUnverifiedPlan;
        public bool AllPlates;
        public double? RepairBendsMillimeters;
        public BendRepairUnits CadUnits;
    }
}
