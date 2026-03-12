using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenNest;
using OpenNest.Geometry;
using OpenNest.IO;

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

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
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
Console.WriteLine($"Plate: {plateIndex} ({plate.Size.Width:F1} x {plate.Size.Height:F1}), spacing={plate.PartSpacing:F2}");
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

void PrintUsage()
{
    Console.Error.WriteLine("Usage: OpenNest.Console <nest-file> [options]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Arguments:");
    Console.Error.WriteLine("  nest-file              Path to a .zip nest file");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Options:");
    Console.Error.WriteLine("  --drawing <name>       Drawing name to fill with (default: first drawing)");
    Console.Error.WriteLine("  --plate <index>        Plate index to fill (default: 0)");
    Console.Error.WriteLine("  --quantity <n>          Max parts to place (default: 0 = unlimited)");
    Console.Error.WriteLine("  --spacing <value>      Override part spacing");
    Console.Error.WriteLine("  --size <WxH>           Override plate size (e.g. 120x60)");
    Console.Error.WriteLine("  --output <path>        Output nest file path (default: <input>-result.zip)");
    Console.Error.WriteLine("  --autonest             Use NFP-based mixed-part autonesting instead of linear fill");
    Console.Error.WriteLine("  --keep-parts           Don't clear existing parts before filling");
    Console.Error.WriteLine("  --check-overlaps       Run overlap detection after fill (exit code 1 if found)");
    Console.Error.WriteLine("  --no-save              Skip saving output file");
    Console.Error.WriteLine("  --no-log               Skip writing debug log file");
    Console.Error.WriteLine("  -h, --help             Show this help");
}
