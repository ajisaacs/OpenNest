using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenNest;
using OpenNest.Geometry;
using OpenNest.IO;
using Color = System.Drawing.Color;
using OpenNest.Console;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.ML;
using OpenNest.Gpu;

// Parse arguments.
var nestFile = (string)null;
var drawingName = (string)null;
var plateIndex = 0;
var outputFile = (string)null;
var quantity = 0;
var spacing = (double?)null;
var plateWidth = (double?)null;
var plateHeight = (double?)null;
var checkOverlaps = false;
var noSave = false;
var noLog = false;
var keepParts = false;
var autoNest = false;
var collectDataDir = (string)null;
var dbPath = "nesting_training.db";
var saveNestsDir = (string)null;
var templateFile = (string)null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db" when i + 1 < args.Length:
            dbPath = args[++i];
            break;
        case "--save-nests" when i + 1 < args.Length:
            saveNestsDir = args[++i];
            break;
        case "--template" when i + 1 < args.Length:
            templateFile = args[++i];
            break;
        case "--collect" when i + 1 < args.Length:
            collectDataDir = args[++i];
            break;
        case "--drawing" when i + 1 < args.Length:
            drawingName = args[++i];
            break;
        case "--plate" when i + 1 < args.Length:
            plateIndex = int.Parse(args[++i]);
            break;
        case "--output" when i + 1 < args.Length:
            outputFile = args[++i];
            break;
        case "--quantity" when i + 1 < args.Length:
            quantity = int.Parse(args[++i]);
            break;
        case "--spacing" when i + 1 < args.Length:
            spacing = double.Parse(args[++i]);
            break;
        case "--size" when i + 1 < args.Length:
            var parts = args[++i].Split('x');
            if (parts.Length == 2)
            {
                plateWidth = double.Parse(parts[0]);
                plateHeight = double.Parse(parts[1]);
            }
            break;
        case "--check-overlaps":
            checkOverlaps = true;
            break;
        case "--no-save":
            noSave = true;
            break;
        case "--no-log":
            noLog = true;
            break;
        case "--keep-parts":
            keepParts = true;
            break;
        case "--autonest":
            autoNest = true;
            break;
        case "--help":
        case "-h":
            PrintUsage();
            return 0;
        default:
            if (!args[i].StartsWith("--") && nestFile == null)
                nestFile = args[i];
            break;
    }
}

// Initialize GPU if available.
if (GpuEvaluatorFactory.GpuAvailable)
{
    BestFitCache.CreateSlideComputer = () => GpuEvaluatorFactory.CreateSlideComputer();
    Console.WriteLine($"GPU: {GpuEvaluatorFactory.DeviceName}");
}
else
{
    Console.WriteLine("GPU: not available (using CPU)");
}

if (collectDataDir != null)
{
    return RunDataCollection(collectDataDir, dbPath, saveNestsDir, spacing ?? 0.5, templateFile);
}

if (string.IsNullOrEmpty(nestFile) || !File.Exists(nestFile))
{
    PrintUsage();
    return 1;
}

// Set up debug log file.
StreamWriter logWriter = null;

if (!noLog)
{
    var logDir = Path.Combine(Path.GetDirectoryName(nestFile), "test-harness-logs");
    Directory.CreateDirectory(logDir);
    var logFile = Path.Combine(logDir, $"debug-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    logWriter = new StreamWriter(logFile) { AutoFlush = true };
    Trace.Listeners.Add(new TextWriterTraceListener(logWriter));
    Console.WriteLine($"Debug log: {logFile}");
}

// Load nest.
var reader = new NestReader(nestFile);
var nest = reader.Read();

if (nest.Plates.Count == 0)
{
    Console.Error.WriteLine("Error: nest file contains no plates");
    return 1;
}

if (plateIndex >= nest.Plates.Count)
{
    Console.Error.WriteLine($"Error: plate index {plateIndex} out of range (0-{nest.Plates.Count - 1})");
    return 1;
}

var plate = nest.Plates[plateIndex];

// Apply overrides.
if (spacing.HasValue)
    plate.PartSpacing = spacing.Value;

if (plateWidth.HasValue && plateHeight.HasValue)
    plate.Size = new Size(plateWidth.Value, plateHeight.Value);

// Find drawing.
var drawing = drawingName != null
    ? nest.Drawings.FirstOrDefault(d => d.Name == drawingName)
    : nest.Drawings.FirstOrDefault();

if (drawing == null)
{
    Console.Error.WriteLine(drawingName != null
        ? $"Error: drawing '{drawingName}' not found. Available: {string.Join(", ", nest.Drawings.Select(d => d.Name))}"
        : "Error: nest file contains no drawings");
    return 1;
}

// Clear existing parts.
var existingCount = plate.Parts.Count;

if (!keepParts)
    plate.Parts.Clear();

Console.WriteLine($"Nest: {nest.Name}");
Console.WriteLine($"Plate: {plateIndex} ({plate.Size.Width:F1} x {plate.Size.Length:F1}), spacing={plate.PartSpacing:F2}");
Console.WriteLine($"Drawing: {drawing.Name}");

if (!keepParts)
    Console.WriteLine($"Cleared {existingCount} existing parts");
else
    Console.WriteLine($"Keeping {existingCount} existing parts");

Console.WriteLine("---");

// Run fill or autonest.
var sw = Stopwatch.StartNew();
bool success;

if (autoNest)
{
    // AutoNest: use all drawings (or specific drawing if --drawing given).
    var nestItems = new List<NestItem>();

    if (drawingName != null)
    {
        nestItems.Add(new NestItem { Drawing = drawing, Quantity = quantity > 0 ? quantity : 1 });
    }
    else
    {
        foreach (var d in nest.Drawings)
            nestItems.Add(new NestItem { Drawing = d, Quantity = quantity > 0 ? quantity : 1 });
    }

    Console.WriteLine($"AutoNest: {nestItems.Count} drawing(s), {nestItems.Sum(i => i.Quantity)} total parts");

    var parts = NestEngine.AutoNest(nestItems, plate);
    plate.Parts.AddRange(parts);
    success = parts.Count > 0;
}
else
{
    var engine = new NestEngine(plate);
    var item = new NestItem { Drawing = drawing, Quantity = quantity };
    success = engine.Fill(item);
}

sw.Stop();

// Check overlaps.
var overlapCount = 0;

if (checkOverlaps && plate.Parts.Count > 0)
{
    List<Vector> overlapPts;
    var hasOverlaps = plate.HasOverlappingParts(out overlapPts);
    overlapCount = overlapPts.Count;

    if (hasOverlaps)
        Console.WriteLine($"OVERLAPS DETECTED: {overlapCount} intersection points");
    else
        Console.WriteLine("Overlap check: PASS");
}

// Flush and close the log.
Trace.Flush();
logWriter?.Dispose();

// Print results.
Console.WriteLine($"Result: {(success ? "success" : "failed")}");
Console.WriteLine($"Parts placed: {plate.Parts.Count}");
Console.WriteLine($"Utilization: {plate.Utilization():P1}");
Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");

// Save output.
if (!noSave)
{
    if (outputFile == null)
    {
        var dir = Path.GetDirectoryName(nestFile);
        var name = Path.GetFileNameWithoutExtension(nestFile);
        outputFile = Path.Combine(dir, $"{name}-result.zip");
    }

    var writer = new NestWriter(nest);
    writer.Write(outputFile);
    Console.WriteLine($"Saved: {outputFile}");
}

return checkOverlaps && overlapCount > 0 ? 1 : 0;

int RunDataCollection(string dir, string dbPath, string saveDir, double s, string template)
{
    if (!Directory.Exists(dir))
    {
        Console.Error.WriteLine($"Error: Directory not found: {dir}");
        return 1;
    }

    // Load template nest for plate defaults if provided.
    Nest templateNest = null;
    if (template != null)
    {
        if (!File.Exists(template))
        {
            Console.Error.WriteLine($"Error: Template not found: {template}");
            return 1;
        }
        templateNest = new NestReader(template).Read();
        Console.WriteLine($"Using template: {template}");
    }

    var PartColors = new[]
    {
        Color.FromArgb(205, 92, 92),
        Color.FromArgb(148, 103, 189),
        Color.FromArgb(75, 180, 175),
        Color.FromArgb(210, 190, 75),
        Color.FromArgb(190, 85, 175),
        Color.FromArgb(185, 115, 85),
        Color.FromArgb(120, 100, 190),
        Color.FromArgb(200, 100, 140),
        Color.FromArgb(80, 175, 155),
        Color.FromArgb(195, 160, 85),
        Color.FromArgb(175, 95, 160),
        Color.FromArgb(215, 130, 130),
    };

    var sheetSuite = new[]
    {
        new Size(96, 48), new Size(120, 48), new Size(144, 48),
        new Size(96, 60), new Size(120, 60), new Size(144, 60),
        new Size(96, 72), new Size(120, 72), new Size(144, 72),
        new Size(48, 24), new Size(120, 10)
    };

    var dxfFiles = Directory.GetFiles(dir, "*.dxf", SearchOption.AllDirectories);
    Console.WriteLine($"Found {dxfFiles.Length} DXF files");
    Console.WriteLine($"Database: {Path.GetFullPath(dbPath)}");
    Console.WriteLine($"Sheet sizes: {sheetSuite.Length} configurations");
    Console.WriteLine($"Spacing: {s:F2}");
    if (saveDir != null) Console.WriteLine($"Saving nests to: {saveDir}");
    Console.WriteLine("---");

    using var db = new TrainingDatabase(dbPath);

    var importer = new DxfImporter();
    var colorIndex = 0;
    var processed = 0;
    var skippedGeometry = 0;
    var skippedFeatures = 0;
    var skippedExisting = 0;
    var totalRuns = 0;
    var totalSw = Stopwatch.StartNew();

    foreach (var file in dxfFiles)
    {
        var fileNum = processed + skippedGeometry + skippedFeatures + skippedExisting + 1;
        var partNo = Path.GetFileNameWithoutExtension(file);
        Console.Write($"[{fileNum}/{dxfFiles.Length}] {partNo}");

        try
        {
            var existingRuns = db.RunCount(Path.GetFileName(file));
            if (existingRuns >= sheetSuite.Length)
            {
                Console.WriteLine(" - SKIP (all sizes done)");
                skippedExisting++;
                continue;
            }

            if (!importer.GetGeometry(file, out var entities))
            {
                Console.WriteLine(" - SKIP (no geometry)");
                skippedGeometry++;
                continue;
            }

            var drawing = new Drawing(Path.GetFileName(file));
            drawing.Program = OpenNest.Converters.ConvertGeometry.ToProgram(entities);
            drawing.UpdateArea();
            drawing.Color = PartColors[colorIndex % PartColors.Length];
            colorIndex++;

            var features = FeatureExtractor.Extract(drawing);
            if (features == null)
            {
                Console.WriteLine(" - SKIP (feature extraction failed)");
                skippedFeatures++;
                continue;
            }

            Console.WriteLine($" (area={features.Area:F1}, verts={features.VertexCount})");

            // Precompute best-fits once for all sheet sizes.
            var sizes = sheetSuite.Select(sz => (sz.Width, sz.Length)).ToList();
            var bfSw = Stopwatch.StartNew();
            BestFitCache.ComputeForSizes(drawing, s, sizes);
            bfSw.Stop();
            Console.WriteLine($"  Best-fits computed in {bfSw.ElapsedMilliseconds}ms");

            using var txn = db.BeginTransaction();

            var partId = db.GetOrAddPart(Path.GetFileName(file), features, drawing.Program.ToString());
            var partSw = Stopwatch.StartNew();
            var runsThisPart = 0;
            var bestUtil = 0.0;
            var bestCount = 0;

            foreach (var size in sheetSuite)
            {
                if (db.HasRun(Path.GetFileName(file), size.Width, size.Length, s))
                {
                    Console.WriteLine($"  {size.Length}x{size.Width} - skip (exists)");
                    continue;
                }

                Plate runPlate;
                if (templateNest != null)
                {
                    runPlate = templateNest.PlateDefaults.CreateNew();
                    runPlate.Size = size;
                    runPlate.PartSpacing = s;
                }
                else
                {
                    runPlate = new Plate { Size = size, PartSpacing = s };
                }

                var sizeSw = Stopwatch.StartNew();
                var result = BruteForceRunner.Run(drawing, runPlate);
                sizeSw.Stop();

                if (result == null)
                {
                    Console.WriteLine($"  {size.Length}x{size.Width} - no fit");
                    continue;
                }

                if (result.Utilization > bestUtil)
                {
                    bestUtil = result.Utilization;
                    bestCount = result.PartCount;
                }

                Console.WriteLine($"  {size.Length}x{size.Width} - {result.PartCount}pcs, {result.Utilization:P1}, {sizeSw.ElapsedMilliseconds}ms");

                string savedFilePath = null;
                if (saveDir != null)
                {
                    // Deterministic bucket (00-FF) based on filename hash
                    uint hash = 0;
                    foreach (char c in partNo) hash = (hash * 31) + c;
                    var bucket = (hash % 256).ToString("X2");

                    var partDir = Path.Combine(saveDir, bucket, partNo);
                    Directory.CreateDirectory(partDir);

                    var nestName = $"{partNo}-{size.Length}x{size.Width}-{result.PartCount}pcs";
                    var fileName = nestName + ".zip";
                    savedFilePath = Path.Combine(partDir, fileName);

                    // Create nest from template or from scratch
                    Nest nestObj;
                    if (templateNest != null)
                    {
                        nestObj = new Nest(nestName)
                        {
                            Units = templateNest.Units,
                            DateCreated = DateTime.Now
                        };
                        nestObj.PlateDefaults.SetFromExisting(templateNest.PlateDefaults.CreateNew());
                    }
                    else
                    {
                        nestObj = new Nest(nestName) { Units = Units.Inches, DateCreated = DateTime.Now };
                    }

                    nestObj.Drawings.Add(drawing);
                    var plateObj = nestObj.CreatePlate();
                    plateObj.Size = size;
                    plateObj.PartSpacing = s;
                    plateObj.Parts.AddRange(result.PlacedParts);

                    var writer = new NestWriter(nestObj);
                    writer.Write(savedFilePath);
                }

                db.AddRun(partId, size.Width, size.Length, s, result, savedFilePath);
                runsThisPart++;
                totalRuns++;
            }

            txn.Commit();
            BestFitCache.Invalidate(drawing);
            partSw.Stop();
            processed++;
            Console.WriteLine($"  Total: {runsThisPart} runs, best={bestCount}pcs @ {bestUtil:P1}, {partSw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.Error.WriteLine($"  ERROR: {ex.Message}");
        }
    }

    totalSw.Stop();
    Console.WriteLine("---");
    Console.WriteLine($"Processed: {processed} parts, {totalRuns} total runs");
    Console.WriteLine($"Skipped:   {skippedExisting} (existing) + {skippedGeometry} (no geometry) + {skippedFeatures} (no features)");
    Console.WriteLine($"Time:      {totalSw.Elapsed:h\\:mm\\:ss}");
    Console.WriteLine($"Database:  {Path.GetFullPath(dbPath)}");
    return 0;
}

void PrintUsage()
{
    Console.Error.WriteLine("Usage: OpenNest.Console <nest-file> [options]");
    Console.Error.WriteLine("       OpenNest.Console --collect <dxf-dir> [options]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Arguments:");
    Console.Error.WriteLine("  nest-file              Path to a .zip nest file");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Options:");
    Console.Error.WriteLine("  --drawing <name>       Drawing name to fill with (default: first drawing)");
    Console.WriteLine("  --plate <index>        Plate index to fill (default: 0)");
    Console.WriteLine("  --quantity <n>          Max parts to place (default: 0 = unlimited)");
    Console.WriteLine("  --spacing <value>      Override part spacing");
    Console.WriteLine("  --size <WxH>           Override plate size (e.g. 120x60)");
    Console.WriteLine("  --output <path>        Output nest file path (default: <input>-result.zip)");
    Console.WriteLine("  --autonest             Use NFP-based mixed-part autonesting instead of linear fill");
    Console.WriteLine("  --keep-parts           Don't clear existing parts before filling");
    Console.WriteLine("  --check-overlaps       Run overlap detection after fill (exit code 1 if found)");
    Console.WriteLine("  --no-save              Skip saving output file");
    Console.WriteLine("  --no-log               Skip writing debug log file");
    Console.WriteLine("  --collect <dir>        Brute-force process all DXFs in directory to SQLite");
    Console.WriteLine("  --db <path>            Path to the SQLite training database (default: nesting_training.db)");
    Console.WriteLine("  --save-nests <dir>     Directory to save individual .zip nests for each winner");
    Console.WriteLine("  --template <path>      Nest template (.nstdot) for plate defaults");
    Console.WriteLine("  -h, --help             Show this help");
}
