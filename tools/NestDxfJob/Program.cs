using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNest;
using OpenNest.Benchmark;
using OpenNest.CNC;
using OpenNest.IO;
using OpenNest.IO.Bom;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length != 5)
{
    Console.Error.WriteLine(
        "Usage: NestDxfJob <dxf-directory> <quantities.xlsx> <settings.nest> <new-output-directory> <engine|import-only>"
    );
    return 1;
}

try
{
    var directory = Path.GetFullPath(args[0]);
    var workbookPath = Path.GetFullPath(args[1]);
    var templatePath = Path.GetFullPath(args[2]);
    var outputDirectory = Path.GetFullPath(args[3]);
    if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
        throw new IOException(
            "Output directory must be new; existing inputs/results are never overwritten."
        );
    var engineInfo =
        args[4] == "import-only"
            ? null
            : NestingEngineRegistry.AvailableEngines.SingleOrDefault(e => e.Name == args[4])
                ?? throw new ArgumentException($"Unknown engine: {args[4]}");
    var quantities = PartQuantityReader.Read(workbookPath);
    var paths = Directory
        .GetFiles(directory)
        .Where(p => Path.GetExtension(p).Equals(".dxf", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal)
        .ToArray();
    var files = paths.ToDictionary(Path.GetFileNameWithoutExtension, StringComparer.Ordinal);
    var demand = quantities
        .Where(p => p.Value > 0)
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    var missing = demand.Keys.Except(files.Keys, StringComparer.Ordinal).ToArray();
    if (missing.Length > 0)
        throw new InvalidDataException($"Missing exact-name DXFs: {string.Join(", ", missing)}");
    var unrequested = files.Keys.Except(demand.Keys, StringComparer.Ordinal).ToArray();
    foreach (var name in unrequested)
        Console.WriteLine($"Not requested by workbook (not imported): {files[name]}");
    var hashes = paths
        .Append(workbookPath)
        .Append(templatePath)
        .Distinct()
        .ToDictionary(p => p, Hash);
    var template = new NestReader(templatePath).Read();
    // Saved placements and drawing geometry are NOT imported from the template.
    var input = new Nest(template.Name)
    {
        Units = template.Units,
        Material = template.Material,
        Thickness = template.Thickness,
        Customer = template.Customer,
        DateCreated = DateTime.Now,
        Notes =
            "Fresh DXF import; ETCH/SCRIBE excluded and bend generation disabled. Quantities from workbook. Template supplies stock settings only.",
    };
    var stocks =
        template.Plates.Count > 0
            ? template.Plates.ToList()
            : new List<Plate> { template.PlateDefaults.CreateNew() };
    var distinctStocks = stocks
        .GroupBy(p => new
        {
            p.Size,
            p.Quadrant,
            p.PartSpacing,
            p.EdgeSpacing.Left,
            p.EdgeSpacing.Right,
            p.EdgeSpacing.Top,
            p.EdgeSpacing.Bottom,
        })
        .Select(g => g.First())
        .ToList();
    foreach (var stock in distinctStocks)
    {
        input.PlateDefaults.SetFromExisting(stock);
        input.Plates.Add(input.PlateDefaults.CreateNew());
    }
    input.PlateDefaults.SetFromExisting(distinctStocks[0]);
    var imports = new List<object>();
    foreach (var (name, quantity) in demand)
    {
        var import = CadImporter.Import(files[name], new CadImportOptions { DetectBends = false });
        var sourceUnits = (int)import.Document.Header.InsUnits;
        var expectedUnits = template.Units == Units.Inches ? 1 : 4;
        if (sourceUnits != 0 && sourceUnits != expectedUnits)
            throw new InvalidDataException($"DXF units conflict with template: {name}");
        var marks = import.Document.Entities.Count(e => IsMark(e.Layer?.Name));
        if (import.Entities.Any(e => IsMark(e.Layer?.Name)) || import.Bends.Count != 0)
            throw new InvalidDataException($"Cut-only import retained markings: {name}");
        var drawing = CadImporter.BuildDrawing(
            import,
            import.Entities,
            import.Bends,
            quantity,
            template.Customer,
            null
        );
        drawing.Material = template.Material;
        var previous = template.Drawings.SingleOrDefault(d => d.Name == name);
        if (previous != null)
        {
            drawing.Constraints = previous.Constraints;
            drawing.Priority = previous.Priority;
        }
        EnsureCutOnly(drawing);
        input.Drawings.Add(drawing);
        imports.Add(
            new
            {
                Name = name,
                Required = quantity,
                RemovedMarks = marks,
                SourceUnits = sourceUnits,
                CutEntities = import.Entities.Count,
                drawing.Area,
            }
        );
    }
    var job = new NestJob(
        input.Drawings.Select(d => DrawingJobMapper.FromDrawing(d.Name, d, d.Quantity.Required)),
        distinctStocks.Select((p, i) => DrawingJobMapper.FromPlate($"stock-{i + 1}", p, null)),
        new NestJobOptions(maxPlates: 40)
    );
    NestJobValidator.Validate(job);
    VerifySources(hashes);
    Directory.CreateDirectory(outputDirectory);
    var inputPath = Path.Combine(outputDirectory, "imported-cut-only.nest");
    new NestWriter(input).Write(inputPath);
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(
        Path.Combine(outputDirectory, "import-report.json"),
        JsonSerializer.Serialize(
            new
            {
                Workbook = workbookPath,
                Template = templatePath,
                Units = template.Units.ToString(),
                UnitPolicy = "Explicit DXF units must agree; unitless DXFs use template units without scaling.",
                StockPolicy = "Unlimited copies of the template's distinct physical sheet settings; maximum 40 sheets. Not an inventory assertion.",
                SourceHashes = hashes,
                UnrequestedDxfs = unrequested,
                Imports = imports,
                Requested = demand.Values.Sum(),
                Drawings = demand.Count,
            },
            jsonOptions
        )
    );
    Console.WriteLine(
        $"Imported {demand.Count} drawings / {demand.Values.Sum()} pieces. Input: {inputPath}"
    );
    if (engineInfo == null)
        return 0;
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    var timer = Stopwatch.StartNew();
    var result = engineInfo.Factory().Solve(job, new JobProgress(), cancellation.Token);
    Console.WriteLine(
        $"{result.Status}: {result.Fulfillment.Sum(f => f.Placed)}/{demand.Values.Sum()}, {result.Plates.Count} sheets, {timer.Elapsed.TotalSeconds:F2}s"
    );
    var materialized = NestResultMaterializer.Materialize(job, result);
    var output = materialized.Nest;
    output.Name = input.Name;
    output.Units = input.Units;
    output.Material = input.Material;
    output.Thickness = input.Thickness;
    output.Customer = input.Customer;
    output.Notes = input.Notes;
    output.DateCreated = input.DateCreated;
    output.PlateDefaults = input.PlateDefaults;
    foreach (var drawing in input.Drawings)
    {
        var placed = materialized.DrawingsByPartId[drawing.Name];
        placed.Name = drawing.Name;
        placed.Color = drawing.Color;
        placed.Source = drawing.Source;
        placed.SourceEntities = drawing.SourceEntities;
        placed.SuppressedEntityIds = drawing.SuppressedEntityIds;
        placed.Material = drawing.Material;
        placed.Customer = drawing.Customer;
    }
    var violations = Validate(output, demand);
    if (result.Status != NestJobStatus.Complete)
        violations.Add($"Incomplete job: {result.StopReason}");
    VerifySources(hashes);
    var resultPath = Path.Combine(outputDirectory, $"{input.Name}-{engineInfo.Name}.nest");
    if (violations.Count == 0)
    {
        new NestWriter(output).Write(resultPath);
        var reloaded = new NestReader(resultPath).Read();
        violations.AddRange(Validate(reloaded, demand));
        NestJobValidator.Validate(
            new NestJob(
                reloaded.Drawings.Select(d =>
                    DrawingJobMapper.FromDrawing(d.Name, d, d.Quantity.Required)
                ),
                job.Plates
            )
        );
        if (violations.Count != 0)
            File.Move(resultPath, resultPath + ".invalid");
    }
    File.WriteAllText(
        Path.Combine(outputDirectory, "validation-report.json"),
        JsonSerializer.Serialize(
            new
            {
                Engine = engineInfo.Name,
                Status = result.Status.ToString(),
                StopReason = result.StopReason.ToString(),
                ElapsedSeconds = timer.Elapsed.TotalSeconds,
                Requested = demand.Values.Sum(),
                Placed = result.Fulfillment.Sum(f => f.Placed),
                result.Fulfillment,
                result.StockUsage,
                result.Plates,
                Violations = violations,
                SavedNestReloadPassed = violations.Count == 0,
                SourceHashesUnchanged = true,
            },
            jsonOptions
        )
    );
    if (violations.Count != 0)
        throw new InvalidDataException(string.Join(Environment.NewLine, violations));
    Console.WriteLine($"Verified complete saved nest: {resultPath}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.ToString());
    return 1;
}

static bool IsMark(string layer) =>
    string.Equals(layer, "ETCH", StringComparison.OrdinalIgnoreCase)
    || string.Equals(layer, "SCRIBE", StringComparison.OrdinalIgnoreCase);
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
static void VerifySources(Dictionary<string, string> hashes)
{
    foreach (var (path, hash) in hashes)
        if (Hash(path) != hash)
            throw new IOException($"Source changed during run: {path}");
}
static void EnsureCutOnly(Drawing drawing)
{
    if (
        drawing.Program.Codes.Any(c =>
            c is LinearMove l && l.Layer != LayerType.Cut
            || c is ArcMove a && a.Layer != LayerType.Cut
        )
    )
        throw new InvalidDataException($"Non-cut motion in {drawing.Name}");
}
static List<string> Validate(Nest nest, IReadOnlyDictionary<string, int> demand)
{
    var requirements = nest.Drawings.ToDictionary(d => d, d => (d.Name, demand[d.Name]));
    var validation = NestValidator.Validate(
        nest.Plates.Select(p => (p, p.Parts.ToList())).ToList(),
        requirements
    );
    var counts = nest
        .Plates.SelectMany(p => p.Parts)
        .GroupBy(p => p.BaseDrawing.Name)
        .ToDictionary(g => g.Key, g => g.Count());
    foreach (var (name, required) in demand)
        if (counts.GetValueOrDefault(name) != required)
            validation.Violations.Add(
                $"{name}: {counts.GetValueOrDefault(name)} placed, {required} required"
            );
    foreach (var drawing in nest.Drawings)
        EnsureCutOnly(drawing);
    return validation.Violations;
}

sealed class JobProgress : IProgress<NestJobProgress>
{
    public void Report(NestJobProgress value)
    {
        if (value.Stage == NestJobStage.PlateCommitted)
            Console.WriteLine(
                $"Committed {value.CommittedPlates} sheets / {value.CommittedParts} parts"
            );
    }
}
