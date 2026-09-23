using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OpenNest;
using OpenNest.Benchmark;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;
using PepCodes = PepLib.Codes;
using PepModels = PepLib.Models;
using PepVector = PepLib.Geometry.Vector;

// Converts PEP nests (downloaded through PepApi, parsed with PepLib) into OpenNest .nest files
// for OpenNest.Benchmark. Each .nest keeps PEP's own layout - sheet sizes, duplicates, part
// spacing, edge spacing, quadrant and every placement - so the benchmark scores PEP as its
// "Baseline" row and offers engines exactly the sheet sizes PEP used.

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

var options = Options.Parse(args);
if (options == null)
{
    Console.Error.WriteLine(
        """
        Usage: PepNestExport <output-directory> [options]

          --year <yyyy>              PEP year to export (default 2026)
          --api <url>                PepApi base URL (default http://10.10.100.134:8085)
          --nests N1,N2,...          Only these nest names (default: every nest in the year)
          --status S1,S2,...         Only these PEP statuses, e.g. "Has been cut,To be cut"
                                     (default: every status except Deleted)
          --quantity nested|required Part demand written to the .nest (default nested):
                                       nested   = what PEP actually nested, so the PEP layout is a
                                                  valid, fully placed baseline
                                       required = PEP's required qty; where PEP over-nested, the
                                                  baseline is flagged over-quantity
          --parallel <n>             Nests converted at once (default 4)
          --force                    Re-download and re-convert nests that already exist

        Writes <output>/<nest>.nest, caches the raw files in <output>/pep/, and writes a
        per-nest summary to <output>/pep-baseline.csv. Benchmark the output folder with:
          OpenNest.Benchmark <output-directory> --engines Opus55 --csv results.csv
        """
    );
    return 1;
}

Directory.CreateDirectory(options.OutputDirectory);
var pepDirectory = Path.Combine(options.OutputDirectory, "pep");
Directory.CreateDirectory(pepDirectory);

using var http = new HttpClient
{
    BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromMinutes(2),
};
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

List<NestSummary> summaries;
try
{
    summaries =
        await http.GetFromJsonAsync<List<NestSummary>>($"nests/{options.Year}", json)
        ?? new List<NestSummary>();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not list {options.Year} nests from {http.BaseAddress}: {ex.Message}");
    return 1;
}

var selected = summaries
    .Where(s => options.Nests.Count == 0 || options.Nests.Contains(s.Name))
    .Where(s =>
        options.Statuses.Count > 0
            ? options.Statuses.Contains(s.Status)
            : !string.Equals(s.Status, "Deleted", StringComparison.OrdinalIgnoreCase)
    )
    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
    .ToList();

var missingNests = options.Nests.Except(summaries.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
foreach (var name in missingNests)
    Console.Error.WriteLine($"Warning: {name} is not a {options.Year} nest in PepApi.");

Console.WriteLine(
    $"{summaries.Count} nests in {options.Year}; converting {selected.Count} (quantity = {options.Quantity})."
);

var rows = new ConcurrentBag<ReportRow>();
var completed = 0;

await Parallel.ForEachAsync(
    selected,
    new ParallelOptions { MaxDegreeOfParallelism = options.Parallel },
    async (summary, cancellation) =>
    {
        var row = new ReportRow { Nest = summary.Name, PepStatus = summary.Status };
        try
        {
            var nestPath = Path.Combine(options.OutputDirectory, summary.Name + ".nest");
            if (File.Exists(nestPath) && !options.Force)
            {
                row.Result = "skipped (exists; --force to redo)";
            }
            else
            {
                var pepPath = Path.Combine(pepDirectory, summary.Name + ".pep");
                if (!File.Exists(pepPath) || options.Force)
                {
                    var url = $"nests/{options.Year}/{Uri.EscapeDataString(summary.Name)}/download";
                    var bytes = await http.GetByteArrayAsync(url, cancellation);
                    await File.WriteAllBytesAsync(pepPath, bytes, cancellation);
                }

                PepModels.Nest pep;
                using (var stream = File.OpenRead(pepPath))
                    pep = PepModels.Nest.Load(stream);

                PepConverter.Convert(summary, pep, options.Quantity, row, nestPath);
            }
        }
        catch (Exception ex)
        {
            row.Result = "error: " + ex.Message.ReplaceLineEndings(" ");
        }

        rows.Add(row);
        var done = Interlocked.Increment(ref completed);
        Console.WriteLine($"[{done}/{selected.Count}] {row.Nest}: {row.Result}");
    }
);

var ordered = rows.OrderBy(r => r.Nest, StringComparer.OrdinalIgnoreCase).ToList();
var csvPath = Path.Combine(options.OutputDirectory, "pep-baseline.csv");
ReportRow.WriteCsv(csvPath, ordered);

var converted = ordered.Where(r => r.Result == "ok").ToList();
Console.WriteLine();
Console.WriteLine(
    $"Converted {converted.Count} (with geometry warnings: {converted.Count(r => r.GeometryWarnings.Count > 0)}; "
        + $"PEP layout fails relaxed check: {converted.Count(r => !r.ValidationTimedOut && !r.RelaxedValid)}; "
        + $"fails strict benchmark check: {converted.Count(r => !r.ValidationTimedOut && !r.BaselineValid)}; "
        + $"validation timed out: {converted.Count(r => r.ValidationTimedOut)}), "
        + $"skipped {ordered.Count(r => r.Result.StartsWith("skipped"))}, "
        + $"errors {ordered.Count(r => r.Result.StartsWith("error"))}."
);
if (converted.Count > 0)
{
    var sheet = converted.Sum(r => r.SheetArea);
    var part = converted.Sum(r => r.PartArea);
    Console.WriteLine(
        $"PEP across converted nests: {converted.Sum(r => r.Sheets)} sheets, {sheet:F0} sq in of sheet, "
            + $"{part:F0} sq in of parts, {100 * part / sheet:F1}% utilization."
    );
}
Console.WriteLine($"Summary: {csvPath}");
return 0;

static class PepConverter
{
    public static void Convert(
        NestSummary summary,
        PepModels.Nest pep,
        QuantityMode quantityMode,
        ReportRow row,
        string nestPath
    )
    {
        var required = pep
            .Drawings.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.QtyRequired), StringComparer.OrdinalIgnoreCase);

        var pepPlates = pep
            .Plates.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => (Plate: p, Parts: p.Parts.Where(IsRealPart).ToList()))
            .Where(p => p.Parts.Count > 0)
            .ToList();

        if (pepPlates.Count == 0)
        {
            row.Result = "skipped (no nested parts)";
            return;
        }

        var first = pepPlates[0].Plate;
        var nest = new Nest(summary.Name)
        {
            Units = Units.Inches,
            Customer = summary.Customer,
            Thickness = first.Thickness,
            Material = new Material(summary.MaterialNumber.ToString(), summary.MaterialGrade),
            DateCreated = summary.DateCreated,
            DateLastModified = DateTime.Now,
        };

        var drawings = new Dictionary<string, Drawing>(StringComparer.OrdinalIgnoreCase);
        var drawingBoxes = new Dictionary<string, Box>(StringComparer.OrdinalIgnoreCase);
        var loopShapes = new Dictionary<string, (OpenNest.CNC.Program Program, Box CutBox)>();
        var warnings = new List<string>();

        foreach (var (pepPlate, pepParts) in pepPlates)
        {
            // PEP "PLATE SCALING = 60X120" is Y x X; OpenNest Size is (Width = Y, Length = X).
            var plate = new Plate(pepPlate.Size.Height, pepPlate.Size.Width)
            {
                Quantity = System.Math.Max(1, pepPlate.Duplicates),
                Quadrant = pepPlate.Quadrant is >= 1 and <= 4 ? pepPlate.Quadrant : 1,
                PartSpacing = pepPlate.PartSpacing,
                EdgeSpacing = new Spacing(
                    pepPlate.EdgeSpacing.Left,
                    pepPlate.EdgeSpacing.Bottom,
                    pepPlate.EdgeSpacing.Right,
                    pepPlate.EdgeSpacing.Top
                ),
            };

            foreach (var pepPart in pepParts)
            {
                if (!loopShapes.TryGetValue(pepPart.Name, out var shape))
                {
                    var loop =
                        pep.Loops.FirstOrDefault(l => l.Name == pepPart.Name)
                        ?? throw new InvalidDataException($"Loop {pepPart.Name} not found");
                    var program = ToProgram(loop);
                    shape = (program, CutBox(program));
                    loopShapes.Add(pepPart.Name, shape);
                }

                if (!drawings.TryGetValue(pepPart.DrawingName, out var drawing))
                {
                    drawing = new Drawing(pepPart.DrawingName, shape.Program)
                    {
                        Customer = summary.Customer,
                        Material = nest.Material,
                        Color = Drawing.GetNextColor(),
                    };
                    drawings.Add(pepPart.DrawingName, drawing);
                    drawingBoxes.Add(pepPart.DrawingName, shape.CutBox);
                    nest.Drawings.Add(drawing);
                    if (!HasClosedPerimeter(shape.Program))
                        warnings.Add($"{pepPart.DrawingName}: no closed outer contour; engines cannot place it");
                }

                // PEP can place one drawing through several loops, each starting at its own
                // pierce point, so each loop's frame is a translation of the drawing's.
                var drawingBox = drawingBoxes[pepPart.DrawingName];
                var delta = new Vector(
                    shape.CutBox.Left - drawingBox.Left,
                    shape.CutBox.Bottom - drawingBox.Bottom
                );
                if (
                    System.Math.Abs(shape.CutBox.Width - drawingBox.Width) > 0.01
                    || System.Math.Abs(shape.CutBox.Length - drawingBox.Length) > 0.01
                )
                {
                    warnings.Add($"{pepPart.DrawingName}: loop {pepPart.Name} differs in size from the drawing's first loop");
                }

                // Same convention in both systems: rotate the program about its origin, then
                // place that origin at the part location.
                var part = new Part(drawing);
                if (!OpenNest.Math.Tolerance.IsEqualTo(pepPart.Rotation, 0))
                    part.Rotate(pepPart.Rotation);
                part.Location = new Vector(pepPart.Location.X, pepPart.Location.Y) + delta.Rotate(pepPart.Rotation);
                plate.Parts.Add(part);
            }

            nest.Plates.Add(plate);
        }

        nest.PlateDefaults.SetFromExisting(nest.Plates[0]);
        nest.UpdateDrawingQuantities();

        foreach (var drawing in nest.Drawings)
        {
            var pepRequired = required.GetValueOrDefault(drawing.Name);
            var nested = drawing.Quantity.Nested;
            drawing.Quantity.Required =
                quantityMode == QuantityMode.Required && pepRequired > 0 ? pepRequired : nested;
            if (pepRequired != nested)
                row.QuantityMismatches.Add($"{drawing.Name} req {pepRequired} nested {nested}");
        }

        var unnested = required
            .Where(r => r.Value > 0 && !drawings.ContainsKey(r.Key) && !IsSkeleton(r.Key))
            .Select(r => r.Key)
            .ToList();
        foreach (var name in unnested)
            row.QuantityMismatches.Add($"{name} req {required[name]} nested 0 (no geometry; not exported)");

        nest.Notes =
            $"Converted from PEP {summary.Name} ({summary.Status}; {summary.Comments}). "
            + $"Plates are PEP's own layout. Quantities = PEP {quantityMode.ToString().ToLowerInvariant()} counts.";

        new NestWriter(nest).Write(nestPath);
        // Report on the saved file (the writer rounds coordinates), which is what the benchmark reads.
        FillReport(new NestReader(nestPath).Read(), row, warnings);

        var violationsPath = Path.ChangeExtension(nestPath, ".violations.txt");
        if (row.Violations.Count > 0)
            File.WriteAllLines(violationsPath, row.Violations);
        else
            File.Delete(violationsPath);
        row.Result = "ok";
    }

    private static void FillReport(Nest nest, ReportRow row, List<string> warnings)
    {
        var plateRuns = new List<(Plate Plate, List<Part> Parts)>();
        foreach (var plate in nest.Plates)
            for (var copy = 0; copy < plate.Quantity; copy++)
                plateRuns.Add((plate, plate.Parts.ToList()));

        var requirements = nest.Drawings.ToDictionary(
            d => d,
            d => (d.Name, d.Quantity.Required)
        );
        // PEP stores placements to ~4 decimals and spaces parts at exactly the nominal gap, which
        // the validator's circumscribed arc polygons read as slightly short. A relaxed pass
        // separates that from real conversion problems (overlaps, parts off the sheet).
        var relaxedRuns = plateRuns
            .Select(run =>
            {
                var edge = run.Plate.EdgeSpacing;
                var relaxed = new Plate(run.Plate.Size)
                {
                    Quadrant = run.Plate.Quadrant,
                    PartSpacing = System.Math.Max(0, run.Plate.PartSpacing - RelaxedSpacingTolerance),
                    EdgeSpacing = new Spacing(
                        System.Math.Max(0, edge.Left - RelaxedEdgeTolerance),
                        System.Math.Max(0, edge.Bottom - RelaxedEdgeTolerance),
                        System.Math.Max(0, edge.Right - RelaxedEdgeTolerance),
                        System.Math.Max(0, edge.Top - RelaxedEdgeTolerance)
                    ),
                };
                return (relaxed, run.Parts);
            })
            .ToList();

        row.Material = $"{nest.Material.Name} {nest.Material.Grade} {nest.Thickness:0.###}";
        row.Drawings = nest.Drawings.Count;
        row.PartsRequested = nest.Drawings.Sum(d => d.Quantity.Required);
        row.PartsNested = nest.Drawings.Sum(d => d.Quantity.Nested);
        row.Sheets = nest.Plates.Sum(p => p.Quantity);
        row.SheetSizes = string.Join(
            " ",
            nest.Plates.GroupBy(p => (p.Size.Width, p.Size.Length))
                .Select(g => $"{g.Key.Width:0.###}x{g.Key.Length:0.###}*{g.Sum(p => p.Quantity)}")
        );
        row.PartSpacing = string.Join(" ", nest.Plates.Select(p => p.PartSpacing.ToString("0.###")).Distinct());
        row.EdgeSpacing = string.Join(
            " ",
            nest.Plates.Select(p =>
                    $"{p.EdgeSpacing.Left:0.###}/{p.EdgeSpacing.Bottom:0.###}/{p.EdgeSpacing.Right:0.###}/{p.EdgeSpacing.Top:0.###}"
                )
                .Distinct()
        );
        row.SheetArea = nest.Plates.Sum(p => p.Size.Width * p.Size.Length * p.Quantity);
        row.PartArea = nest.Drawings.Sum(d => d.Area * d.Quantity.Nested);

        // NestValidator can take minutes on parts with hundreds of outline segments and many
        // holes; don't let one nest stall the batch. An abandoned check keeps running on a
        // pool thread until the process exits.
        var check = Task.Run(() =>
            (
                Strict: NestValidator.Validate(plateRuns, requirements),
                Relaxed: NestValidator.Validate(relaxedRuns, requirements)
            )
        );
        row.GeometryWarnings = warnings;
        if (!check.Wait(ValidationTimeout))
        {
            row.ValidationTimedOut = true;
            row.Violations = warnings
                .Prepend($"validation timed out after {ValidationTimeout.TotalSeconds:0}s (layout not checked)")
                .ToList();
            return;
        }

        var (validation, relaxedValidation) = check.Result;
        row.BaselineValid = validation.Valid;
        row.StrictViolations = validation.Violations.Count;
        row.RelaxedValid = relaxedValidation.Valid;
        row.Violations = relaxedValidation
            .Violations.Select(v => "relaxed: " + v)
            .Concat(warnings)
            .Concat(validation.Violations.Select(v => "strict: " + v))
            .ToList();
    }

    private const double RelaxedSpacingTolerance = 0.025;
    private const double RelaxedEdgeTolerance = 0.001;
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(60);

    private static Box CutBox(OpenNest.CNC.Program program) =>
        OpenNest.Converters.ConvertProgram.ToGeometry(program)
            .Where(e => e.Layer != SpecialLayers.Rapid)
            .Cast<IBoundable>()
            .GetBoundingBox();

    private static bool HasClosedPerimeter(OpenNest.CNC.Program program)
    {
        var entities = OpenNest.Converters.ConvertProgram.ToGeometry(program)
            .Where(e => e.Layer != SpecialLayers.Rapid)
            .ToList();
        return entities.Count > 0 && new ShapeProfile(entities).Perimeter?.Area() > 1e-9;
    }

    private static bool IsRealPart(PepModels.Part part) =>
        !part.IsDisplayOnly && !string.IsNullOrWhiteSpace(part.DrawingName) && !IsSkeleton(part.DrawingName);

    private static bool IsSkeleton(string name) =>
        name.StartsWith("Skeleton", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Flattens a PEP loop (incremental, with sub-loop calls for holes) into an absolute OpenNest
    /// program in the loop's own frame. Only contour cuts are kept as cut motion; display, scribe,
    /// lead-in/out and destruct (slug-chopping) moves become rapids so they never shape the part
    /// for nesting. Contours PEP leaves open by a micro-joint are closed.
    /// </summary>
    private static OpenNest.CNC.Program ToProgram(PepModels.Loop loop)
    {
        var codes = new List<ICode>();
        Emit(loop, new PepVector(0, 0), codes);
        var program = new OpenNest.CNC.Program(Mode.Absolute);
        program.Codes.AddRange(CollapseRapids(CloseMicroJoints(codes)));
        // Built absolute, stored incremental like CAD-imported drawings: the desktop renderer
        // only applies a part's location to incremental programs.
        program.Mode = Mode.Incremental;
        return program;
    }

    /// <summary>
    /// Replaces each run of non-cut moves with one rapid onto the next contour's start and drops
    /// trailing ones. Lead-in/out endpoints lie outside the part and a program's bounding box
    /// counts rapid endpoints, so leaving them in would inflate the part for edge checks.
    /// </summary>
    private static IEnumerable<ICode> CollapseRapids(IEnumerable<ICode> codes)
    {
        var pos = new Vector(0, 0);
        var pendingRapid = false;
        foreach (var code in codes)
        {
            if (code is LinearMove or ArcMove)
            {
                if (pendingRapid)
                    yield return new RapidMove(pos);
                pendingRapid = false;
                yield return code;
            }
            else if (code is Motion)
            {
                pendingRapid = true;
            }

            if (code is Motion motion)
                pos = motion.EndPoint;
        }
    }

    /// <summary>Largest uncut tab (micro-joint) gap that is bridged to close a contour.</summary>
    private const double MaxMicroJointGap = 0.25;

    /// <summary>
    /// PEP leaves tabs uncut to hold parts and cutouts in place: the cut stops, jumps the tab
    /// with a rapid, and carries on (e.g. a cutout cut as two halves 0.02 apart, or an outer
    /// contour stopping 0.03 short of its start). The part still occupies that material, so
    /// open cut runs are chained end to start across gaps up to <see cref="MaxMicroJointGap"/>
    /// and each chain that closes into a loop gets a cut line across every tab. Links are
    /// matched shortest gap first, and only closed loops are kept, so separate contours that
    /// happen to lie close together (closed ones never take part) are not merged.
    /// </summary>
    private static IEnumerable<ICode> CloseMicroJoints(List<ICode> codes)
    {
        var runs = SplitCutRuns(codes);
        var open = Enumerable
            .Range(0, runs.Count)
            .Where(i => runs[i].Start.DistanceTo(runs[i].End) > OpenNest.Math.Tolerance.Epsilon)
            .ToList();

        var next = new Dictionary<int, int>();
        var previous = new Dictionary<int, int>();
        var links =
            from i in open
            from j in open
            let gap = runs[i].End.DistanceTo(runs[j].Start)
            where gap <= MaxMicroJointGap
            orderby gap
            select (From: i, To: j);

        foreach (var (from, to) in links)
        {
            if (next.ContainsKey(from) || previous.ContainsKey(to))
                continue;
            next[from] = to;
            previous[to] = from;
        }

        // Keep only links that close a loop; a chain that dead-ends is left as it was.
        var cycleOf = new Dictionary<int, List<int>>();
        foreach (var first in open.Where(next.ContainsKey))
        {
            if (cycleOf.ContainsKey(first))
                continue;

            var cycle = new List<int> { first };
            var current = next[first];
            while (current != first && next.TryGetValue(current, out var following) && !cycle.Contains(current))
            {
                cycle.Add(current);
                current = following;
            }

            if (current != first)
                continue;

            foreach (var index in cycle)
                cycleOf[index] = cycle;
        }

        var emitted = new HashSet<int>();
        for (var i = 0; i < runs.Count; i++)
        {
            if (emitted.Contains(i))
                continue;

            if (!cycleOf.TryGetValue(i, out var cycle))
            {
                emitted.Add(i);
                yield return new RapidMove(runs[i].Start);
                foreach (var code in runs[i].Codes)
                    yield return code;
                continue;
            }

            // Start the loop at the run that comes first in the program.
            var offset = cycle.IndexOf(i);
            yield return new RapidMove(runs[i].Start);

            for (var k = 0; k < cycle.Count; k++)
            {
                var run = runs[cycle[(offset + k) % cycle.Count]];
                var following = runs[cycle[(offset + k + 1) % cycle.Count]];
                emitted.Add(cycle[(offset + k) % cycle.Count]);

                foreach (var code in run.Codes)
                    yield return code;

                if (run.End.DistanceTo(following.Start) > OpenNest.Math.Tolerance.Epsilon)
                    yield return new LinearMove(following.Start) { Layer = LayerType.Cut };
            }
        }
    }

    private sealed record CutRun(Vector Start, Vector End, List<ICode> Codes);

    /// <summary>
    /// Splits an absolute program into runs of consecutive cut moves. Everything else only
    /// positions the head, and <see cref="CollapseRapids"/> rebuilds it afterwards.
    /// </summary>
    private static List<CutRun> SplitCutRuns(List<ICode> codes)
    {
        var runs = new List<CutRun>();
        var pos = new Vector(0, 0);
        List<ICode> current = null;
        var start = pos;

        foreach (var code in codes)
        {
            if (code is LinearMove or ArcMove)
            {
                if (current == null)
                {
                    current = new List<ICode>();
                    start = pos;
                }
                current.Add(code);
            }
            else if (current != null)
            {
                runs.Add(new CutRun(start, pos, current));
                current = null;
            }

            if (code is Motion motion)
                pos = motion.EndPoint;
        }

        if (current != null)
            runs.Add(new CutRun(start, pos, current));

        return runs;
    }

    private static PepVector Emit(PepModels.Program source, PepVector start, List<ICode> codes)
    {
        var pos = start;
        var inDestructCut = false;
        foreach (var code in source)
        {
            switch (code)
            {
                case PepCodes.Comment comment:
                    if (comment.Value.StartsWith("DESTRUCT CUT START", StringComparison.OrdinalIgnoreCase))
                        inDestructCut = true;
                    else if (comment.Value.StartsWith("DESTRUCT CUT END", StringComparison.OrdinalIgnoreCase))
                        inDestructCut = false;
                    break;

                case PepCodes.RapidMove rapid:
                    pos = Advance(pos, rapid.EndPoint, source.Mode);
                    codes.Add(new RapidMove(ToVector(pos)));
                    break;

                case PepCodes.LinearMove line:
                    pos = Advance(pos, line.EndPoint, source.Mode);
                    codes.Add(
                        line.Type == PepCodes.EntityType.Cut && !inDestructCut
                            ? new LinearMove(ToVector(pos)) { Layer = LayerType.Cut }
                            : new RapidMove(ToVector(pos))
                    );
                    break;

                case PepCodes.CircularMove arc:
                    var arcStart = pos;
                    pos = Advance(pos, arc.EndPoint, source.Mode);
                    var center = EquidistantCenter(arcStart, pos, Advance(arcStart, arc.CenterPoint, source.Mode));
                    codes.Add(
                        arc.Type == PepCodes.EntityType.Cut && !inDestructCut
                            ? new ArcMove(
                                ToVector(pos),
                                ToVector(center),
                                arc.Rotation == PepLib.Enums.RotationType.CW
                                    ? RotationType.CW
                                    : RotationType.CCW
                            )
                            {
                                Layer = LayerType.Cut,
                            }
                            : new RapidMove(ToVector(pos))
                    );
                    break;

                case PepCodes.SubProgramCall call when call.Loop != null:
                    // Incremental position carries through the sub-loop: the caller resumes
                    // where the sub-loop ended (e.g. identical holes called 13.8125 apart after
                    // a 0.1875 lead-in sit on a 14.000 pitch).
                    pos = Emit(call.Loop, pos, codes);
                    break;
            }
        }
        return pos;
    }

    /// <summary>
    /// PEP stores some small arcs with a center that is not equidistant from both ends (e.g. a
    /// 0.06 notch with radii 0.0300 and 0.0298). OpenNest rebuilds the arc from its end point, so
    /// its start would miss the previous move and break the contour. Projecting the center onto
    /// the chord's perpendicular bisector keeps both endpoints exact. Full circles are unchanged.
    /// </summary>
    private static PepVector EquidistantCenter(PepVector start, PepVector end, PepVector center)
    {
        var chordX = end.X - start.X;
        var chordY = end.Y - start.Y;
        var chord = System.Math.Sqrt(chordX * chordX + chordY * chordY);
        if (chord < 1e-9)
            return center;

        var midX = (start.X + end.X) / 2;
        var midY = (start.Y + end.Y) / 2;
        var normalX = -chordY / chord;
        var normalY = chordX / chord;
        var along = (center.X - midX) * normalX + (center.Y - midY) * normalY;
        return new PepVector(midX + along * normalX, midY + along * normalY);
    }

    private static PepVector Advance(PepVector current, PepVector offset, PepLib.Enums.ProgrammingMode mode) =>
        mode == PepLib.Enums.ProgrammingMode.Incremental ? current + offset : offset;

    private static Vector ToVector(PepVector v) => new(v.X, v.Y);
}

enum QuantityMode
{
    Nested,
    Required,
}

sealed record NestSummary(
    string Name,
    DateTime DateCreated,
    string Status,
    string Comments,
    string Customer,
    int MaterialNumber,
    string MaterialGrade,
    string Application
);

sealed class Options
{
    public string OutputDirectory;
    public int Year = 2026;
    public string ApiBaseUrl = "http://10.10.100.134:8085";
    public HashSet<string> Nests = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase);
    public QuantityMode Quantity = QuantityMode.Nested;
    public int Parallel = 4;
    public bool Force;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--year" when i + 1 < args.Length:
                    o.Year = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--api" when i + 1 < args.Length:
                    o.ApiBaseUrl = args[++i];
                    break;
                case "--nests" when i + 1 < args.Length:
                    o.Nests.UnionWith(SplitList(args[++i]));
                    break;
                case "--status" when i + 1 < args.Length:
                    o.Statuses.UnionWith(SplitList(args[++i]));
                    break;
                case "--quantity" when i + 1 < args.Length:
                    if (!Enum.TryParse(args[++i], ignoreCase: true, out o.Quantity))
                        return null;
                    break;
                case "--parallel" when i + 1 < args.Length:
                    o.Parallel = System.Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                case "--force":
                    o.Force = true;
                    break;
                default:
                    if (args[i].StartsWith("--") || o.OutputDirectory != null)
                        return null;
                    o.OutputDirectory = Path.GetFullPath(args[i]);
                    break;
            }
        }
        return o.OutputDirectory == null ? null : o;
    }

    private static IEnumerable<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

sealed class ReportRow
{
    public string Nest;
    public string PepStatus;
    public string Result = "";
    public string Material = "";
    public int Drawings;
    public int PartsRequested;
    public int PartsNested;
    public int Sheets;
    public string SheetSizes = "";
    public string PartSpacing = "";
    public string EdgeSpacing = "";
    public double SheetArea;
    public double PartArea;
    public bool BaselineValid;
    public int StrictViolations;
    public bool RelaxedValid;
    public bool ValidationTimedOut;
    public List<string> Violations = new();
    public List<string> GeometryWarnings = new();
    public List<string> QuantityMismatches = new();

    public static void WriteCsv(string path, IEnumerable<ReportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "Nest,PepStatus,Result,Material,Drawings,PartsRequested,PartsNested,Sheets,SheetSizes,"
                + "PartSpacing,EdgeSpacing(L/B/R/T),SheetArea,PartArea,Utilization%,RelaxedValid,"
                + "StrictValid,StrictViolations,GeometryWarnings,Violations,QuantityMismatches"
        );
        foreach (var r in rows)
        {
            var utilization = r.SheetArea > 0 ? 100 * r.PartArea / r.SheetArea : 0;
            sb.AppendLine(
                string.Join(
                    ",",
                    Csv(r.Nest),
                    Csv(r.PepStatus),
                    Csv(r.Result),
                    Csv(r.Material),
                    r.Drawings,
                    r.PartsRequested,
                    r.PartsNested,
                    r.Sheets,
                    Csv(r.SheetSizes),
                    Csv(r.PartSpacing),
                    Csv(r.EdgeSpacing),
                    r.SheetArea.ToString("F2"),
                    r.PartArea.ToString("F2"),
                    utilization.ToString("F2"),
                    r.Result != "ok" ? "" : r.ValidationTimedOut ? "timeout" : r.RelaxedValid.ToString(),
                    r.Result != "ok" ? "" : r.ValidationTimedOut ? "timeout" : r.BaselineValid.ToString(),
                    r.Result != "ok" || r.ValidationTimedOut ? "" : r.StrictViolations.ToString(),
                    Csv(string.Join(" | ", r.GeometryWarnings)),
                    Csv(string.Join(" | ", r.Violations.Take(5)) + (r.Violations.Count > 5 ? $" | +{r.Violations.Count - 5} more" : "")),
                    Csv(string.Join(" | ", r.QuantityMismatches))
                )
            );
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static string Csv(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
