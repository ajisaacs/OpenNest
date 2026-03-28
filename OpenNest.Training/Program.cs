using OpenNest;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.ML;
using OpenNest.Geometry;
using OpenNest.Gpu;
using OpenNest.IO;
using OpenNest.Training;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Color = System.Drawing.Color;

// Parse arguments.
var dbPath = "OpenNestTraining";
var saveNestsDir = (string)null;
var templateFile = (string)null;
var spacing = 0.5;
var collectDir = (string)null;

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
        case "--spacing" when i + 1 < args.Length:
            spacing = double.Parse(args[++i]);
            break;
        case "--help":
        case "-h":
            PrintUsage();
            return 0;
        default:
            if (!args[i].StartsWith("--") && collectDir == null)
                collectDir = args[i];
            break;
    }
}

if (string.IsNullOrEmpty(collectDir) || !Directory.Exists(collectDir))
{
    PrintUsage();
    return 1;
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

return RunDataCollection(collectDir, dbPath, saveNestsDir, spacing, templateFile);

int RunDataCollection(string dir, string dbPath, string saveDir, double s, string template)
{
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
    var resolvedDb = dbPath.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ? dbPath : dbPath + ".db";
    Console.WriteLine($"Database: {Path.GetFullPath(resolvedDb)}");
    Console.WriteLine($"Sheet sizes: {sheetSuite.Length} configurations");
    Console.WriteLine($"Spacing: {s:F2}");
    if (saveDir != null) Console.WriteLine($"Saving nests to: {saveDir}");
    Console.WriteLine("---");

    using var db = new TrainingDatabase(dbPath);

    var backfilled = db.BackfillPerimeterToAreaRatio();
    if (backfilled > 0)
        Console.WriteLine($"Backfilled PerimeterToAreaRatio for {backfilled} existing parts");

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
            var normalized = ShapeProfile.NormalizeEntities(entities);
            drawing.Program = OpenNest.Converters.ConvertGeometry.ToProgram(normalized);
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
                var result = BruteForceRunner.Run(drawing, runPlate, forceFullAngleSweep: true);
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

                var engineInfo = $"{result.WinnerEngine}({result.WinnerTimeMs}ms)";
                if (!string.IsNullOrEmpty(result.RunnerUpEngine))
                    engineInfo += $", 2nd={result.RunnerUpEngine}({result.RunnerUpPartCount}pcs/{result.RunnerUpTimeMs}ms)";
                if (!string.IsNullOrEmpty(result.ThirdPlaceEngine))
                    engineInfo += $", 3rd={result.ThirdPlaceEngine}({result.ThirdPlacePartCount}pcs/{result.ThirdPlaceTimeMs}ms)";
                Console.WriteLine($"  {size.Length}x{size.Width} - {result.PartCount}pcs, {result.Utilization:P1}, {sizeSw.ElapsedMilliseconds}ms [{engineInfo}] angles={result.AngleResults.Count}");

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
                    var fileName = nestName + NestFormat.FileExtension;
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

                db.AddRun(partId, size.Width, size.Length, s, result, savedFilePath, result.AngleResults);
                runsThisPart++;
                totalRuns++;
            }

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
    Console.WriteLine($"Database:  {Path.GetFullPath(resolvedDb)}");
    return 0;
}

void PrintUsage()
{
    Console.Error.WriteLine("Usage: OpenNest.Training <dxf-dir> [options]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Arguments:");
    Console.Error.WriteLine("  dxf-dir                Directory containing DXF files to process");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Options:");
    Console.Error.WriteLine("  --spacing <value>      Part spacing (default: 0.5)");
    Console.Error.WriteLine("  --db <path>            SQLite database path (default: OpenNestTraining.db)");
    Console.Error.WriteLine("  --save-nests <dir>     Directory to save individual .nest nests for each winner");
    Console.Error.WriteLine("  --template <path>      Nest template (.nstdot) for plate defaults");
    Console.Error.WriteLine("  -h, --help             Show this help");
}
