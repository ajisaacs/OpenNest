using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OpenNest;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

return NestConsole.Run(args);

static class NestConsole
{
    public static int Run(string[] args)
    {
        var options = ParseArgs(args);

        if (options == null)
            return 0; // --help was requested

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

        using var log = SetUpLog(options);
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

        // Flush and close the log before printing results.
        Trace.Flush();
        log?.Dispose();

        PrintResults(success, plate, elapsed);
        Save(nest, options);

        return options.CheckOverlaps && overlapCount > 0 ? 1 : 0;
    }

    static Options ParseArgs(string[] args)
    {
        var o = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
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
                    var parts = args[++i].Split('x');
                    if (parts.Length == 2)
                    {
                        o.PlateHeight = double.Parse(parts[0]);
                        o.PlateWidth = double.Parse(parts[1]);
                    }
                    break;
                case "--check-overlaps":
                    o.CheckOverlaps = true;
                    break;
                case "--no-save":
                    o.NoSave = true;
                    break;
                case "--no-log":
                    o.NoLog = true;
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

    static StreamWriter SetUpLog(Options options)
    {
        if (options.NoLog)
            return null;

        var baseDir = Path.GetDirectoryName(options.InputFiles[0]);
        var logDir = Path.Combine(baseDir, "test-harness-logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"debug-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var writer = new StreamWriter(logFile) { AutoFlush = true };
        Trace.Listeners.Add(new TextWriterTraceListener(writer));
        Console.WriteLine($"Debug log: {logFile}");
        return writer;
    }

    static Nest LoadOrCreateNest(Options options)
    {
        var nestFile = options.InputFiles.FirstOrDefault(f =>
            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        var dxfFiles = options.InputFiles.Where(f =>
            f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)).ToList();

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
                Console.Error.WriteLine($"Error: plate index {options.PlateIndex} out of range (0-{nest.Plates.Count - 1})");
                return null;
            }

            foreach (var dxf in dxfFiles)
            {
                var drawing = ImportDxf(dxf);

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
            Console.Error.WriteLine("Error: no nest (.zip) or DXF (.dxf) files specified");
            return null;
        }

        if (!options.PlateWidth.HasValue || !options.PlateHeight.HasValue)
        {
            Console.Error.WriteLine("Error: --size WxH is required when importing DXF files without a nest");
            return null;
        }

        var newNest = new Nest { Name = "DXF Import" };
        var plate = new Plate { Size = new Size(options.PlateWidth.Value, options.PlateHeight.Value) };
        newNest.Plates.Add(plate);

        foreach (var dxf in dxfFiles)
        {
            var drawing = ImportDxf(dxf);

            if (drawing == null)
                return null;

            newNest.Drawings.Add(drawing);
            Console.WriteLine($"Imported: {drawing.Name}");
        }

        return newNest;
    }

    static Drawing ImportDxf(string path)
    {
        var importer = new DxfImporter();

        if (!importer.GetGeometry(path, out var geometry))
        {
            Console.Error.WriteLine($"Error: failed to read DXF file: {path}");
            return null;
        }

        if (geometry.Count == 0)
        {
            Console.Error.WriteLine($"Error: no geometry found in DXF file: {path}");
            return null;
        }

        var pgm = ConvertGeometry.ToProgram(geometry);

        if (pgm == null)
        {
            Console.Error.WriteLine($"Error: failed to convert geometry: {path}");
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return new Drawing(name, pgm);
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

        var templatePlate = new NestReader(options.TemplateFile).Read().PlateDefaults.CreateNew();
        plate.Thickness = templatePlate.Thickness;
        plate.Quadrant = templatePlate.Quadrant;
        plate.Material = templatePlate.Material;
        plate.EdgeSpacing = templatePlate.EdgeSpacing;
        plate.PartSpacing = templatePlate.PartSpacing;
        Console.WriteLine($"Template: {options.TemplateFile}");
    }

    static void ApplyOverrides(Plate plate, Options options)
    {
        if (options.Spacing.HasValue)
            plate.PartSpacing = options.Spacing.Value;

        // Only apply size override when it wasn't already used to create the plate.
        var hasDxfOnly = !options.InputFiles.Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (options.PlateWidth.HasValue && options.PlateHeight.HasValue && !hasDxfOnly)
            plate.Size = new Size(options.PlateWidth.Value, options.PlateHeight.Value);
    }

    static Drawing ResolveDrawing(Nest nest, Options options)
    {
        var drawing = options.DrawingName != null
            ? nest.Drawings.FirstOrDefault(d => d.Name == options.DrawingName)
            : nest.Drawings.FirstOrDefault();

        if (drawing != null)
            return drawing;

        Console.Error.WriteLine(options.DrawingName != null
            ? $"Error: drawing '{options.DrawingName}' not found. Available: {string.Join(", ", nest.Drawings.Select(d => d.Name))}"
            : "Error: nest file contains no drawings");

        return null;
    }

    static void PrintHeader(Nest nest, Plate plate, Drawing drawing, int existingCount, Options options)
    {
        Console.WriteLine($"Nest: {nest.Name}");
        var wa = plate.WorkArea();
        Console.WriteLine($"Plate: {options.PlateIndex} ({plate.Size.Length:F1} x {plate.Size.Width:F1}), spacing={plate.PartSpacing:F2}, edge=({plate.EdgeSpacing.Left},{plate.EdgeSpacing.Bottom},{plate.EdgeSpacing.Right},{plate.EdgeSpacing.Top}), workArea={wa.Length:F1}x{wa.Width:F1}");
        Console.WriteLine($"Drawing: {drawing.Name}");
        Console.WriteLine(options.KeepParts
            ? $"Keeping {existingCount} existing parts"
            : $"Cleared {existingCount} existing parts");
        Console.WriteLine("---");
    }

    static (bool success, long elapsedMs) Fill(Nest nest, Plate plate, Drawing drawing, Options options)
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

            Console.WriteLine($"AutoNest: {nestItems.Count} drawing(s), {nestItems.Sum(i => i.Quantity)} total parts");

            var engine = NestEngineRegistry.Create(plate);
            var nestParts = engine.Nest(nestItems, null, CancellationToken.None);
            plate.Parts.AddRange(nestParts);
            success = nestParts.Count > 0;
        }
        else
        {
            var engine = NestEngineRegistry.Create(plate);
            var item = new NestItem { Drawing = drawing, Quantity = options.Quantity };
            success = engine.Fill(item);
        }

        sw.Stop();
        return (success, sw.ElapsedMilliseconds);
    }

    static int CheckOverlaps(Plate plate, Options options)
    {
        if (!options.CheckOverlaps || plate.Parts.Count == 0)
            return 0;

        var hasOverlaps = plate.HasOverlappingParts(out var overlapPts);
        Console.WriteLine(hasOverlaps
            ? $"OVERLAPS DETECTED: {overlapPts.Count} intersection points"
            : "Overlap check: PASS");

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
        var outputFile = options.OutputFile ?? Path.Combine(
            Path.GetDirectoryName(firstInput),
            $"{Path.GetFileNameWithoutExtension(firstInput)}-result.zip");

        new NestWriter(nest).Write(outputFile);
        Console.WriteLine($"Saved: {outputFile}");
    }

    static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: OpenNest.Console <input-files...> [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Arguments:");
        Console.Error.WriteLine("  input-files            One or more .zip nest files or .dxf drawing files");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Modes:");
        Console.Error.WriteLine("  <nest.zip>             Load nest and fill (existing behavior)");
        Console.Error.WriteLine("  <part.dxf> --size LxW  Import DXF, create plate, and fill");
        Console.Error.WriteLine("  <nest.zip> <part.dxf>  Load nest and add imported DXF drawings");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --drawing <name>       Drawing name to fill with (default: first drawing)");
        Console.Error.WriteLine("  --plate <index>        Plate index to fill (default: 0)");
        Console.Error.WriteLine("  --quantity <n>          Max parts to place (default: 0 = unlimited)");
        Console.Error.WriteLine("  --spacing <value>      Override part spacing");
        Console.Error.WriteLine("  --size <LxW>           Override plate size (e.g. 120x60); required for DXF-only mode");
        Console.Error.WriteLine("  --output <path>        Output nest file path (default: <input>-result.zip)");
        Console.Error.WriteLine("  --template <path>      Nest template for plate defaults (thickness, quadrant, material, spacing)");
        Console.Error.WriteLine("  --autonest             Use NFP-based mixed-part autonesting instead of linear fill");
        Console.Error.WriteLine("  --keep-parts           Don't clear existing parts before filling");
        Console.Error.WriteLine("  --check-overlaps       Run overlap detection after fill (exit code 1 if found)");
        Console.Error.WriteLine("  --no-save              Skip saving output file");
        Console.Error.WriteLine("  --no-log               Skip writing debug log file");
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
        public double? PlateWidth;
        public double? PlateHeight;
        public bool CheckOverlaps;
        public bool NoSave;
        public bool NoLog;
        public bool KeepParts;
        public bool AutoNest;
        public string TemplateFile;
    }
}
