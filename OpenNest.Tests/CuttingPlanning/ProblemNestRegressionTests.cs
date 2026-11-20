using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.CutOffs;
using OpenNest.Data;
using OpenNest.Engine;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.Sequencing;
using OpenNest.IO;
using Xunit.Abstractions;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Plans every nest on a dedicated problem-nest OpenNest server and fails when a plate's outcome is
/// worse than its recorded baseline. A nest with a cut-off baseline is planned a second time with
/// its applied lead-ins and cut-offs cleared and default scrap cut-offs added. A "checks:" comment
/// line names the planner areas a nest guards; a run limited to some areas skips nests that guard
/// none of them, and a nest without the line is always planned. A run touching the part order or
/// entries plans every nest, since those change the search on every plate. Each plate's share of
/// its expansion budget is reported, and plates near it are listed last: they are the ones a change
/// to the search order tips into <see cref="CuttingPlanStatus.NoSolutionWithinBudget"/>. Opt-in and private:
/// the nests and their baselines stay on the server, never in the repository. See docs/problem-nests.md.
/// </summary>
[Trait("Category", "ProblemNests")]
public partial class ProblemNestRegressionTests(ITestOutputHelper output)
{
    internal const string ServerVariable = "OPENNEST_PROBLEM_NESTS_SERVER";
    internal const string TimeoutVariable = "OPENNEST_PROBLEM_NESTS_TIMEOUT";
    internal const string CutOffsVariable = "OPENNEST_PROBLEM_NESTS_CUTOFFS";
    internal const string ChecksVariable = "OPENNEST_PROBLEM_NESTS_CHECKS";
    internal const string RecordVariable = "OPENNEST_PROBLEM_NESTS_RECORD";
    internal const string Timeout = "Timeout";
    internal const string PlanningLine = "planning:";
    internal const string CutOffPlanningLine = "planning with cut-offs:";
    internal const string ChecksLine = "checks:";
    internal const string AutoNestLine = "auto-nest:";

    /// <summary>Ends an "auto-nest:" line whose nest holds the layout the line describes.</summary>
    internal const string LayoutSavedMark = "layout saved";

    /// <summary>The area of a nest whose problem was Auto Nest's layout, not its cutting plan.</summary>
    internal const string AutoNestArea = "auto-nest";

    /// <summary>The area of a nest whose problem was a plate fill's layout.</summary>
    internal const string FillArea = "fill";

    /// <summary>The planner areas a nest can guard; docs/problem-nests.md says which changes touch each.</summary>
    internal static readonly string[] PlannerAreas = ["part-order", "entries", "contours", "geometry-checks", "cut-offs", "grid"];

    /// <summary>Every area a nest can guard: the planner's, Auto Nest's and the plate fill's.</summary>
    internal static readonly string[] Areas = [.. PlannerAreas, AutoNestArea, FillArea];

    /// <summary>
    /// Areas whose changes reorder the search on every plate, not just the nests tagged with them:
    /// a run naming one plans the whole corpus.
    /// </summary>
    internal static readonly string[] WholeCorpusAreas = ["part-order", "entries"];

    /// <summary>A plate whose strict search spent at least this share of its budget is near it.</summary>
    internal const double NearBudget = 0.8;
    // A corpus is a handful of nests; more means the variable names a production archive.
    internal const int MaximumNests = 200;

    [SkippableFact]
    public async Task ProblemNests_PlanNoWorseThanTheirBaselines()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        Skip.If(string.IsNullOrWhiteSpace(server), $"Set {ServerVariable} to the OpenNest server URL to plan its problem nests.");
        using var repository = new RemoteNestRepository(server!);
        var records = await ProblemNests(repository);
        var limit = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable(TimeoutVariable), out var seconds)
            && seconds > 0 ? seconds : 120);
        Assert.True(records.Count > 0, $"No nests on {server}.");
        var named = Checks(Environment.GetEnvironmentVariable(ChecksVariable));
        Skip.If(named.Length != 0 && !named.Intersect(PlannerAreas).Any(),
            $"{string.Join(", ", named)} names no planner area, so no nest is planned.");
        var wanted = Wanted(named);
        if (wanted.Length != named.Length)
            output.WriteLine($"{string.Join(", ", named.Intersect(WholeCorpusAreas))} change the search on every plate, "
                + "so every nest is planned.");

        var failures = new List<string>();
        var usage = new List<(string Plate, double Share)>();
        foreach (var record in records)
        {
            var guards = Checks(CommentLine(record.Comments, ChecksLine));
            if (!Selected(guards, wanted) || (guards.Length != 0 && !guards.Intersect(PlannerAreas).Any()))
            {
                output.WriteLine($"{record.Name}: skipped, checks {string.Join(", ", guards)}");
                continue;
            }
            if (guards.Length == 0)
                output.WriteLine($"{record.Name}: no {ChecksLine} line, so always planned; add one naming the areas it guards.");
            var bytes = await repository.GetFileAsync(record.Id)
                ?? throw new InvalidOperationException($"'{record.Name}' has no file.");
            var baseline = Baseline(record.Comments);
            var observed = PlanAll(record.Name, bytes, false, baseline, limit, failures, usage);
            if (baseline.Count == 0)
                failures.Add($"{record.Name} has no baseline. Add this line to its comments: {Line(PlanningLine, observed)}");
            if (Environment.GetEnvironmentVariable(CutOffsVariable) == "1")
                PlanAll(record.Name, bytes, true, [], limit, failures, usage);
            else if (Baseline(record.Comments, CutOffPlanningLine) is { Count: > 0 } withCutOffs)
                PlanAll(record.Name, bytes, true, withCutOffs, limit, failures, usage);
        }
        ReportNearBudget(usage);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Runs Auto Nest (the Default engine, the nest's own sheet sizes, spacing and clamps, as the
    /// benchmark builds the job) on every nest that guards <see cref="AutoNestArea"/>, and fails
    /// when a layout is invalid, leaves parts unplaced or needs more sheets than its
    /// "auto-nest: 2 sheets" baseline. A baseline that also says where the last sheet ends
    /// ("auto-nest: 1 sheets, ends 62.2") fails a layout whose last sheet reaches further along
    /// its length: a job that does not fill its last sheet can get worse without another sheet.
    /// A line ending in "layout saved" says the nest's plates are that baseline layout, and the
    /// run reports whether Auto Nest still makes it. With <see cref="RecordVariable"/> set to 1,
    /// a nest whose layout is no worse than its baseline is saved back with that layout as its
    /// plates and the line brought up to date, so the baseline can be opened, or compared with,
    /// without nesting the old code again.
    /// </summary>
    [SkippableFact]
    public async Task ProblemNests_AutoNestNoWorseThanTheirBaselines()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        Skip.If(string.IsNullOrWhiteSpace(server), $"Set {ServerVariable} to the OpenNest server URL to nest its problem nests.");
        var named = Checks(Environment.GetEnvironmentVariable(ChecksVariable));
        Skip.If(named.Length != 0 && !named.Contains(AutoNestArea), $"{string.Join(", ", named)} does not name {AutoNestArea}.");
        using var repository = new RemoteNestRepository(server!);
        var records = await ProblemNests(repository);
        var limit = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable(TimeoutVariable), out var seconds)
            && seconds > 0 ? seconds : 120);

        var recording = Environment.GetEnvironmentVariable(RecordVariable) == "1";
        var failures = new List<string>();
        var nested = 0;
        foreach (var record in records)
        {
            var guards = Checks(CommentLine(record.Comments, ChecksLine));
            if (!guards.Contains(AutoNestArea))
                continue;
            nested++;
            var bytes = await repository.GetFileAsync(record.Id)
                ?? throw new InvalidOperationException($"'{record.Name}' has no file.");
            var baseline = SheetBaseline(record.Comments);
            var endBaseline = EndBaseline(record.Comments);
            var watch = Stopwatch.StartNew();
            var run = AutoNest(bytes, limit);
            var (sheets, end, problem) = (run.Sheets, run.End, run.Problem);
            output.WriteLine($"{record.Name}: {(problem ?? $"{sheets} sheets, ends {Number(end)}")} "
                + $"(baseline {baseline?.ToString() ?? "none"}{(endBaseline is { } recorded ? $", ends {Number(recorded)}" : "")}), "
                + $"{watch.Elapsed.TotalSeconds:0.0} s");
            var failed = failures.Count;
            if (problem != null)
                failures.Add($"{record.Name}: {problem}");
            else if (baseline == null && recording && CannotSave(guards, run) == null)
                output.WriteLine("  no baseline yet.");
            else if (baseline == null)
                failures.Add($"{record.Name} has no Auto Nest baseline. Add this line to its comments: "
                    + AutoNestBaselineLine(sheets, end));
            else if (sheets > baseline)
                failures.Add($"{record.Name}: {sheets} sheets, worse than baseline {baseline}.");
            else if (sheets < baseline)
                output.WriteLine($"  better than {baseline}; update the baseline: {AutoNestBaselineLine(sheets, end)}");
            else if (endBaseline is { } ends && end > ends + EndTolerance)
                failures.Add($"{record.Name}: the last sheet ends at {Number(end)}, worse than baseline {Number(ends)}.");
            else if (endBaseline is { } before && end < before - 2 * EndTolerance)
                output.WriteLine($"  ends before {Number(before)}; update the baseline: {AutoNestBaselineLine(sheets, end)}");
            if (problem != null)
                continue;

            var kept = LayoutSaved(record.Comments);
            var same = kept && SameLayout(bytes, run.Job, run.Result);
            if (kept)
                output.WriteLine(same ? "  the saved layout, unchanged." : "  not the saved layout.");
            if (!recording || failures.Count != failed)
                continue;
            // A layout inside the tolerance past the recorded end keeps that end: recording
            // again and again must not let the baseline creep.
            var keptEnd = endBaseline is { } known && sheets == baseline && end > known ? known : (double?)null;
            var numbers = keptEnd is { } stays ? $"{AutoNestLine} {sheets} sheets, ends {Number(stays)}"
                : AutoNestBaselineLine(sheets, end);
            if (CannotSave(guards, run) is { } why)
            {
                output.WriteLine($"  not saved: {why}. Its line would be: {numbers}");
                continue;
            }
            var line = numbers + ", " + LayoutSavedMark;
            if (same && string.Equals(CommentLine(record.Comments, AutoNestLine)?.Trim(), line[AutoNestLine.Length..].Trim(),
                StringComparison.OrdinalIgnoreCase))
                continue;
            var (archive, updated) = WithLayout(bytes, record, run.Job, run.Result,
                WithAutoNestLine(record.Comments, line));
            var saved = await repository.UpdateFileAsync(record.Id, archive, updated, record.Revision);
            output.WriteLine($"  saved as revision {saved.Revision}: {line}");
        }
        Skip.If(nested == 0, $"No nest on {server} checks {AutoNestArea}.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>One Auto Nest of a problem nest: its sheet count and where its last sheet ends,
    /// or why the layout cannot be kept; the job and result when the engine returned one.</summary>
    internal sealed record AutoNestRun(int Sheets, double End, string Problem, NestJob Job, NestJobResult Result);

    // Auto Nest on the nest's demand.
    private static AutoNestRun AutoNest(byte[] bytes, TimeSpan limit)
    {
        try
        {
            var job = AutoNestJob(bytes);
            using var cancellation = new CancellationTokenSource(limit);
            var result = OpenNest.Engine.Jobs.NestingEngineRegistry.Create("Default").Solve(job, token: cancellation.Token);
            var violations = OpenNest.Engine.Jobs.NestLayoutCheck.Violations(job, result);
            if (violations.Count != 0)
                return new AutoNestRun(result.Plates.Count, 0, "invalid layout: " + string.Join(" | ", violations.Take(3)), job, result);
            var unplaced = result.Fulfillment.Sum(f => f.Unplaced);
            return unplaced != 0 ? new AutoNestRun(result.Plates.Count, 0, $"{unplaced} parts unplaced", job, result)
                : new AutoNestRun(result.Plates.Count, LastSheetEnd(job, result), null, job, result);
        }
        catch (OperationCanceledException)
        {
            return new AutoNestRun(0, 0, Timeout, null, null);
        }
    }

    /// <summary>
    /// The Auto Nest job of a problem nest, as the benchmark builds it from a saved nest: every
    /// drawing's required quantity, the sheet sizes of the nest's plates, and the first plate's
    /// spacing, quadrant and clamps. Its parts are the nest's drawings with a required quantity,
    /// in the nest's order.
    /// </summary>
    internal static NestJob AutoNestJob(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"opennest-problem-{Guid.NewGuid():N}.nest");
        try
        {
            File.WriteAllBytes(path, bytes);
            return OpenNest.Benchmark.JobLoader.Load(path).Single().BuildNestJob(maxPlates: 100);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Whether the "auto-nest:" line says the nest's plates are its baseline layout.</summary>
    internal static bool LayoutSaved(string comments) =>
        CommentLine(comments, AutoNestLine)?.Contains(LayoutSavedMark, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The comments with their "auto-nest:" line replaced by <paramref name="line"/>,
    /// or with it added as the last line when they have none.</summary>
    internal static string WithAutoNestLine(string comments, string line)
    {
        var text = comments ?? "";
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var index = lines.FindIndex(l => l.Trim().StartsWith(AutoNestLine, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            lines[index] = line;
        else
        {
            while (lines.Count > 0 && lines[^1].Trim().Length == 0)
                lines.RemoveAt(lines.Count - 1);
            lines.Add(line);
        }
        return string.Join(newline, lines);
    }

    /// <summary>
    /// Why a run's layout cannot replace the nest's plates; null when it can. Planning and fill
    /// tests read a nest's plates as their input, and the Auto Nest job takes its sheet sizes
    /// from them, so a layout on only some of those sizes would change the job it came from.
    /// </summary>
    internal static string CannotSave(IReadOnlyCollection<string> guards, AutoNestRun run)
    {
        var others = guards.Where(g => g != AutoNestArea).ToList();
        if (others.Count != 0)
            return $"it also guards {string.Join(", ", others)}, whose tests read its plates";
        var offered = run.Job.Plates.Select(p => (p.Size.Width, p.Size.Length)).Distinct().ToList();
        var used = run.Result.Plates.Select(p => (p.Stock.Size.Width, p.Stock.Size.Length)).Distinct().ToList();
        return offered.Except(used).Any()
            ? "the layout leaves a sheet size of the nest unused, and the job takes its sizes from the plates"
            : null;
    }

    /// <summary>
    /// The nest with its plates replaced by <paramref name="result"/>'s sheets and its notes by
    /// <paramref name="comments"/>, as an archive and the record to save it with. Each sheet is
    /// the plate its stock describes (the job's spacing, quadrant and clamps), so the saved nest
    /// gives the same job again; parts are bound to the nest's own drawings.
    /// </summary>
    internal static (byte[] Archive, NestRecord Record) WithLayout(byte[] bytes, NestRecord record,
        NestJob job, NestJobResult result, string comments)
    {
        using var input = new MemoryStream(bytes);
        var nest = new NestReader(input).Read();
        NestRecordFactory.ApplyMetadata(nest, record);
        nest.Notes = comments;
        var drawings = JobDrawings(nest, job);
        nest.Plates.Clear();
        foreach (var sheet in result.Plates)
        {
            var plate = OpenNest.Engine.Jobs.Adapters.DrawingJobMapper.CreatePlate(sheet.Stock);
            foreach (var pose in sheet.Placements)
            {
                var part = new Part(drawings[pose.PartId]);
                part.Rotate(pose.Rotation);
                part.Location = new OpenNest.Geometry.Vector(pose.X, pose.Y);
                part.UpdateBounds();
                plate.Parts.Add(part);
            }
            nest.Plates.Add(plate);
        }
        nest.UpdateDrawingQuantities();
        using var written = new MemoryStream();
        if (!new NestWriter(nest).Write(written))
            throw new InvalidOperationException($"'{record.Name}' could not be written with its new layout.");
        var archive = written.ToArray();
        return (archive, NestRecordFactory.FromNest(nest, record.Id, archive.LongLength));
    }

    /// <summary>
    /// Whether the nest's plates hold exactly <paramref name="result"/>: the same sheets in
    /// order, each of the same size with the same drawings at the same poses (within the file's
    /// coordinate precision), in any part order.
    /// </summary>
    internal static bool SameLayout(byte[] bytes, NestJob job, NestJobResult result)
    {
        using var input = new MemoryStream(bytes);
        var nest = new NestReader(input).Read();
        var drawings = JobDrawings(nest, job);
        var plates = nest.Plates.Where(p => p.Parts.Any(part => !part.BaseDrawing.IsCutOff)).ToList();
        if (plates.Count != result.Plates.Count || plates.Any(p => p.Quantity != 1))
            return false;
        for (var i = 0; i < plates.Count; i++)
        {
            var sheet = result.Plates[i];
            var size = sheet.Stock.Size;
            if (System.Math.Abs(plates[i].Size.Width - size.Width) > PoseTolerance
                || System.Math.Abs(plates[i].Size.Length - size.Length) > PoseTolerance)
                return false;
            var left = plates[i].Parts.Where(part => !part.BaseDrawing.IsCutOff).ToList();
            if (left.Count != sheet.Placements.Count)
                return false;
            foreach (var pose in sheet.Placements)
            {
                var match = left.FindIndex(part => ReferenceEquals(part.BaseDrawing, drawings[pose.PartId])
                    && System.Math.Abs(part.Location.X - pose.X) <= PoseTolerance
                    && System.Math.Abs(part.Location.Y - pose.Y) <= PoseTolerance
                    && SameAngle(part.Rotation, pose.Rotation));
                if (match < 0)
                    return false;
                left.RemoveAt(match);
            }
        }
        return true;
    }

    // The job's part ids to the nest's drawings: a drawing's id is new on every read, so they
    // pair up by position, the job's parts being the nest's wanted drawings in order.
    private static Dictionary<string, Drawing> JobDrawings(Nest nest, NestJob job)
    {
        var wanted = nest.Drawings.Where(d => d.Quantity.Required > 0).ToList();
        if (wanted.Count != job.Parts.Count)
            throw new InvalidOperationException(
                $"The nest wants {wanted.Count} drawings and the job has {job.Parts.Count} parts.");
        return job.Parts.Select((part, index) => (part.Id, Drawing: wanted[index]))
            .ToDictionary(p => p.Id, p => p.Drawing);
    }

    // A saved coordinate keeps ten decimals; poses this close are the same pose.
    private const double PoseTolerance = 1e-6;

    private static bool SameAngle(double a, double b)
    {
        var turn = 2 * System.Math.PI;
        var difference = System.Math.Abs(a - b) % turn;
        return System.Math.Min(difference, turn - difference) <= PoseTolerance;
    }

    /// <summary>The sheet count of an "auto-nest: 2 sheets" comment line; null when there is none.</summary>
    internal static int? SheetBaseline(string comments) =>
        CommentLine(comments, AutoNestLine) is { } line && Regex.Match(line, @"\d+") is { Success: true } count
            ? int.Parse(count.Value) : null;

    /// <summary>
    /// Where the last sheet ends, from an "auto-nest: 1 sheets, ends 62.2" comment line; null when
    /// the line records none, which leaves the end unchecked.
    /// </summary>
    internal static double? EndBaseline(string comments) =>
        CommentLine(comments, AutoNestLine) is { } line && EndEntry().Match(line) is { Success: true } end
            ? double.Parse(end.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;

    // A recorded end is rounded to a tenth; a layout within half of that is the same layout.
    internal const double EndTolerance = 0.05;

    /// <summary>The baseline line for a result, its end rounded up to a tenth so the result meets it.</summary>
    internal static string AutoNestBaselineLine(int sheets, double end) =>
        $"{AutoNestLine} {sheets} sheets, ends {Number(System.Math.Ceiling(end * 10 - 1e-6) / 10)}";

    private static string Number(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// How far along the sheet's length the last sheet's material reaches, measured from the end
    /// of the sheet the layout starts at: the length less the wider strip left free at either end.
    /// The same figure in every quadrant, and the right edge of a layout packed from the left.
    /// </summary>
    internal static double LastSheetEnd(NestJob job, NestJobResult result)
    {
        if (result.Plates.Count == 0)
            return 0;
        var sheet = result.Plates[^1];
        if (sheet.Placements.Count == 0)
            return 0;
        var parts = job.Parts.ToDictionary(p => p.Id);
        double left = double.PositiveInfinity, right = double.NegativeInfinity;
        foreach (var placement in sheet.Placements)
        {
            var part = new Part(OpenNest.Engine.Jobs.Adapters.DrawingJobMapper.CreateDrawing(parts[placement.PartId]));
            part.Rotate(placement.Rotation);
            part.Location = new OpenNest.Geometry.Vector(placement.X, placement.Y);
            part.UpdateBounds();
            var box = NestLayoutCheck.MaterialBounds(part);
            left = System.Math.Min(left, box.Left);
            right = System.Math.Max(right, box.Right);
        }
        var length = sheet.Stock.Size.Length;
        var sheetLeft = sheet.Stock.Quadrant is 1 or 4 ? 0 : -length;
        return length - System.Math.Max(left - sheetLeft, sheetLeft + length - right);
    }

    /// <summary>
    /// Fills each plate listed in the "fill: 1=33" baseline of every nest that guards
    /// <see cref="FillArea"/> with that plate's one drawing, the way Fill Plate does: the plate's
    /// parts are cleared and the strategy fills its work area with the plate's own size, spacing,
    /// edge spacing, quadrant and clamps. Fails when a plate places fewer parts than its baseline,
    /// its layout breaks the layout check or the fill times out; more parts are only reported.
    /// </summary>
    [SkippableFact]
    public async Task ProblemNests_FillNoWorseThanTheirBaselines()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        Skip.If(string.IsNullOrWhiteSpace(server), $"Set {ServerVariable} to the OpenNest server URL to fill its problem nests.");
        var named = Checks(Environment.GetEnvironmentVariable(ChecksVariable));
        Skip.If(named.Length != 0 && !named.Contains(FillArea), $"{string.Join(", ", named)} does not name {FillArea}.");
        using var repository = new RemoteNestRepository(server!);
        var records = await ProblemNests(repository);
        var limit = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable(TimeoutVariable), out var seconds)
            && seconds > 0 ? seconds : 120);

        var failures = new List<string>();
        var filled = 0;
        foreach (var record in records)
        {
            if (!Checks(CommentLine(record.Comments, ChecksLine)).Contains(FillArea))
                continue;
            filled++;
            var bytes = await repository.GetFileAsync(record.Id)
                ?? throw new InvalidOperationException($"'{record.Name}' has no file.");
            FillAll(record.Name, bytes, FillBaseline(record.Comments), limit, failures);
        }
        Skip.If(filled == 0, $"No nest on {server} checks {FillArea}.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // Fills the baseline's plates, or every single-drawing plate when there is no baseline.
    private void FillAll(string name, byte[] bytes, FillBaselineLine baseline, TimeSpan limit, List<string> failures)
    {
        using var stream = new MemoryStream(bytes);
        var nest = new NestReader(stream).Read();
        var strategy = baseline?.Strategy ?? PlateFillService.DefaultStrategy;
        var label = strategy == PlateFillService.DefaultStrategy ? name : $"{name} ({strategy})";
        // A line naming no plate is no baseline: every single-drawing plate is filled to record one.
        var counts = baseline?.Counts ?? new Dictionary<int, int>();
        var observed = new SortedDictionary<int, string>();
        var mismatched = new List<string>();
        for (var index = 0; index < nest.Plates.Count; index++)
        {
            var number = index + 1;
            var listed = counts.ContainsKey(number);
            if (counts.Count != 0 && !listed)
                continue;
            var plate = nest.Plates[index];
            var drawings = plate.Parts.Select(p => p.BaseDrawing).Where(d => !d.IsCutOff)
                .Distinct<Drawing>(ReferenceEqualityComparer.Instance).ToList();
            if (drawings.Count != 1)
            {
                var why = $"plate {number} holds {drawings.Count} drawings; a fill baseline takes plates holding one";
                if (listed)
                    mismatched.Add(why);
                else if (drawings.Count > 1)
                    output.WriteLine($"{label}: skipped {why}.");
                continue;
            }

            // Shaped like the planning test's line, so a nightly runner parses every area alike.
            var subject = strategy == PlateFillService.DefaultStrategy
                ? $"{name} plate {number}" : $"{name} plate {number} ({strategy})";
            var watch = Stopwatch.StartNew();
            var (parts, problem) = Fill(strategy, plate, drawings[0], limit);
            var expected = listed ? counts[number] : (int?)null;
            output.WriteLine($"{subject}: {problem ?? $"{parts} parts"} (baseline {expected?.ToString() ?? "none"}), "
                + $"{watch.Elapsed.TotalSeconds:0.0} s");
            if (problem != null)
            {
                failures.Add($"{subject}: {problem}");
                continue;
            }
            observed[number] = parts.ToString();
            if (parts < expected)
                failures.Add($"{subject}: {parts} parts, worse than baseline {expected}.");
            else if (parts > expected)
                output.WriteLine($"  better than {expected}; update the baseline.");
        }
        foreach (var number in counts.Keys.Where(n => n < 1 || n > nest.Plates.Count))
            mismatched.Add($"the nest has no plate {number}");

        var line = FillLinePrefix(strategy) + " " + string.Join(" ", observed.Select(p => $"{p.Key}={p.Value}"));
        if (mismatched.Count != 0)
            failures.Add($"{label}: baseline plates do not match the nest's plates ({string.Join("; ", mismatched)}). "
                + $"Observed: {line}");
        else if (counts.Count == 0)
            failures.Add(observed.Count == 0
                ? $"{label} has no fill baseline and no plate holding a single drawing to fill."
                : $"{label} has no fill baseline. Add this line to its comments: {line}");
    }

    // Fill Plate on a cleared plate: the part count, or why the fill cannot be kept.
    private static (int Parts, string Problem) Fill(string strategy, Plate plate, Drawing drawing, TimeSpan limit)
    {
        // Cut-offs stay: Fill Plate fills around the plate's cut-offs.
        for (var i = plate.Parts.Count - 1; i >= 0; i--)
            if (!plate.Parts[i].BaseDrawing.IsCutOff)
                plate.Parts.RemoveAt(i);
        using var cancellation = new CancellationTokenSource(limit);
        List<Part> parts;
        try
        {
            parts = PlateFillService.FillItem(strategy, plate, new NestItem { Drawing = drawing, Quantity = 0 },
                plate.WorkArea(), null, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return (0, Timeout);
        }
        if (cancellation.IsCancellationRequested)
            return (parts.Count, Timeout);
        var violations = NestLayoutCheck.Validate([(plate, parts)],
            new Dictionary<Drawing, (string Name, int Quantity)>(ReferenceEqualityComparer.Instance)
            {
                [drawing] = (drawing.Name, int.MaxValue),
            });
        return violations.Count != 0
            ? (parts.Count, $"invalid layout ({parts.Count} parts): " + string.Join(" | ", violations.Take(3)))
            : (parts.Count, null);
    }

    /// <summary>A "fill: 1=33" or "fill (Strip): 1=33" baseline: the strategy and each plate's part count.</summary>
    internal sealed record FillBaselineLine(string Strategy, IReadOnlyDictionary<int, int> Counts);

    /// <summary>
    /// The first "fill: 1=33 3=120" comment line, plate number (counted from 1) to part count, with
    /// the strategy named in parentheses ("fill (Strip): 1=33") or Lattice; null when there is none.
    /// An unknown strategy throws, so a misspelt line cannot quietly fill with another.
    /// </summary>
    internal static FillBaselineLine FillBaseline(string comments)
    {
        var match = (comments ?? "").Split('\n').Select(l => FillLinePattern().Match(l.Trim())).FirstOrDefault(m => m.Success);
        if (match == null)
            return null;
        var strategy = match.Groups["strategy"].Success
            ? PlateFillService.ResolveStrategy(match.Groups["strategy"].Value.Trim())
            : PlateFillService.DefaultStrategy;
        return new FillBaselineLine(strategy, FillEntry().Matches(match.Groups["counts"].Value)
            .ToDictionary(m => int.Parse(m.Groups[1].Value), m => int.Parse(m.Groups[2].Value)));
    }

    internal static string FillLinePrefix(string strategy) =>
        strategy == PlateFillService.DefaultStrategy ? $"{FillArea}:" : $"{FillArea} ({strategy}):";

    // Plates near their budget pass today but are the first a reordered search pushes over it.
    private void ReportNearBudget(List<(string Plate, double Share)> usage)
    {
        var near = usage.Where(u => u.Share >= NearBudget).OrderByDescending(u => u.Share).ToList();
        output.WriteLine(near.Count == 0
            ? $"No plate's search used {NearBudget:P0} of its budget."
            : $"Near the budget ({near.Count}; a change that adds plates here or raises their share is a regression):");
        foreach (var (plate, share) in near)
            output.WriteLine($"  {plate}: {share:P0}");
    }

    // Plans each plate with parts and compares it with the baseline; plate number to outcome.
    private SortedDictionary<int, string> PlanAll(string name, byte[] bytes, bool cutOffs,
        Dictionary<int, string> baseline, TimeSpan limit, List<string> failures, List<(string Plate, double Share)> usage)
    {
        using var stream = new MemoryStream(bytes);
        var nest = new NestReader(stream).Read();
        var label = cutOffs ? $"{name} with cut-offs" : name;
        var observed = new SortedDictionary<int, string>();
        for (var index = 0; index < nest.Plates.Count; index++)
        {
            var plate = nest.Plates[index];
            if (plate.Parts.Count == 0)
                continue;
            var number = index + 1;
            var watch = Stopwatch.StartNew();
            if (cutOffs)
                AddScrapCutOffs(plate, number, nest.Units);
            var budget = CuttingPlanBatch.PlateBudget(plate.Parts.Count);
            var (outcome, planned) = Plan(plate, number, limit);
            observed[number] = outcome;
            var expected = baseline.GetValueOrDefault(number);
            var share = BudgetShare(planned, budget);
            if (share is { } used)
                usage.Add(($"{label} plate {number}", used));
            output.WriteLine($"{label} plate {number}: {outcome} (baseline {expected ?? "none"}), "
                + $"{planned?.Result.Expansions ?? 0} of {budget} expansions"
                + (share is { } shown ? $" ({shown:P0})" : "")
                + (planned?.KeptCurrentOrder == true ? ", new order ran out" : "")
                + $", {watch.Elapsed.TotalSeconds:0.0} s");
            if (expected == null)
                continue;
            if (Rank(outcome) < Rank(expected))
                failures.Add($"{label} plate {number}: {outcome}, worse than baseline {expected}. "
                    + string.Join(" | ", planned?.Result.Findings.Take(3).Select(f => f.Message) ?? []));
            else if (outcome != expected)
                output.WriteLine($"  changed from {expected}; update the baseline if this is intended.");
        }
        if (baseline.Count != 0 && (baseline.Keys.Except(observed.Keys).Any() || observed.Keys.Except(baseline.Keys).Any()))
            failures.Add($"{label}: baseline plates do not match the nest's plates. Observed: "
                + Line(cutOffs ? CutOffPlanningLine : PlanningLine, observed));
        return observed;
    }

    private static string Line(string prefix, SortedDictionary<int, string> observed) =>
        prefix + " " + string.Join(" ", observed.Select(p => $"{p.Key}={p.Value}"));

    // As Plan Cutting after "Clear Lead-ins & Cut-offs", with "Add scrap cut-offs" at its defaults.
    private static void AddScrapCutOffs(Plate plate, int number, Units units)
    {
        AppliedCuttingWork.Clear([plate], [number]);
        var spacing = units == Units.Millimeters ? 889 : 35;
        var staging = AutomaticCutOffBatch.Stage([plate], new AutomaticCutOffOptions
        {
            Spacing = spacing,
            MinimumStripLength = spacing / 2.0,
            MinimumTailLength = units == Units.Millimeters ? 304.8 : 12,
            KeepHorizontalRemnant = true, // As both desktop entry points set it.
        }, new CutOffSettings());
        Assert.False(staging.IsBlocked, $"Plate {number}'s scrap cut-offs are blocked.");
        PlateSequencing.PlaceCutOffs(plate, staging.Plans[0].Definitions);
    }

    // Capture and planning share the limit; a plate that never finishes must not hang the run.
    private static (string Outcome, CuttingPlanPlateResult Planned) Plan(Plate plate, int number, TimeSpan limit)
    {
        using var cancellation = new CancellationTokenSource(limit);
        try
        {
            var planned = CuttingPlanBatch.Capture([plate], plate.CuttingParameters ?? DefaultParameters(),
                preservePartOrder: false, [number], cancellation.Token).Plan(token: cancellation.Token).Plates[0];
            return (planned.Result.Status == CuttingPlanStatus.Canceled ? Timeout : Outcome(planned), planned);
        }
        catch (OperationCanceledException)
        {
            return (Timeout, null);
        }
    }

    internal static async Task<IReadOnlyList<NestRecord>> ProblemNests(RemoteNestRepository repository)
    {
        var page = await repository.QueryAsync(new NestQuery
        {
            Sort = NestSortField.Name,
            Descending = false,
            Limit = MaximumNests,
        });
        if (page.Total > MaximumNests)
            throw new InvalidOperationException($"The server holds {page.Total} nests, more than a problem-nest "
                + $"corpus of at most {MaximumNests}. Point {ServerVariable} at the dedicated corpus server.");
        return page.Items;
    }

    /// <summary>
    /// Plate number to outcome, from a "planning: 1=Ready 2=BestEffort" comment line, or from the
    /// line starting with <paramref name="prefix"/>.
    /// </summary>
    internal static Dictionary<int, string> Baseline(string comments, string prefix = PlanningLine) =>
        CommentLine(comments, prefix) is not { } line ? []
            : BaselineEntry().Matches(line).ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);

    // The rest of the first comment line starting with the prefix; null when there is none.
    private static string CommentLine(string comments, string prefix) =>
        (comments ?? "").Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    /// <summary>
    /// The areas in a comma- or space-separated list, such as "part-order, entries". An area not in
    /// <see cref="Areas"/> throws, so a misspelt line or filter cannot quietly skip a nest.
    /// </summary>
    internal static string[] Checks(string list)
    {
        var areas = (list ?? "").Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToLowerInvariant()).Distinct().ToArray();
        var unknown = areas.Except(Areas).ToArray();
        if (unknown.Length != 0)
            throw new ArgumentException(
                $"Unknown problem-nest area {string.Join(", ", unknown)}; the areas are {string.Join(", ", Areas)}.");
        return areas;
    }

    /// <summary>The areas a run plans: none, meaning every nest, when one changes the search on every plate.</summary>
    internal static string[] Wanted(string[] named) => named.Intersect(WholeCorpusAreas).Any() ? [] : named;

    // Every nest when no area is wanted; otherwise one guarding a wanted area, or one naming none.
    internal static bool Selected(IReadOnlyCollection<string> guards, IReadOnlyCollection<string> wanted) =>
        wanted.Count == 0 || guards.Count == 0 || guards.Intersect(wanted).Any();

    /// <summary>
    /// The share of the plate's budget its applied strict search spent; null when no strict search
    /// found the route (a refusal, the safest-route fallback or the best-effort plan), since then
    /// there is no margin left to measure.
    /// </summary>
    internal static double? BudgetShare(CuttingPlanPlateResult planned, int budget) =>
        planned?.Result is { IsSafestRouteFallback: false, Expansions: > 0 } result && budget > 0
            && result.Status is CuttingPlanStatus.Ready or CuttingPlanStatus.BestEffort
            ? (double)result.Expansions / budget : null;

    // Ready means replayed and overlap-clear; a ready route with overlapping parts is still blocked.
    internal static string Outcome(CuttingPlanPlateResult planned) =>
        planned.IsReady ? "Ready" : planned.IsRouteReady ? "Overlap" : planned.Result.Status.ToString();

    internal static int Rank(string outcome) => outcome switch
    {
        "Ready" => 3,
        nameof(CuttingPlanStatus.BestEffort) => 2,
        nameof(CuttingPlanStatus.NoSolutionWithinBudget) => 1,
        Timeout => -1,
        _ => 0,
    };

    // A plate without its own settings uses the lead-ins problem nests were reported with.
    internal static CuttingParameters DefaultParameters()
    {
        var parameters = new CuttingParameters();
        parameters.ExternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
        parameters.InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
        return parameters;
    }

    [GeneratedRegex(@"(\d+)\s*=\s*(\w+)")]
    private static partial Regex BaselineEntry();

    [GeneratedRegex(@"^fill\s*(?:\((?<strategy>[^)]*)\))?\s*:(?<counts>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FillLinePattern();

    [GeneratedRegex(@"(\d+)\s*=\s*(\d+)")]
    private static partial Regex FillEntry();

    [GeneratedRegex(@"\bends\s+(?:at\s+)?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex EndEntry();
}

public class ProblemNestBaselineTests
{
    [Fact]
    public void Baseline_ReadsThePlanningLineAmongOtherComments()
    {
        var baseline = ProblemNestRegressionTests.Baseline(
            "Plan blocked on unsupported geometry.\r\nplanning: 1=NoSolutionWithinBudget 2 = BestEffort\nnotes");

        Assert.Equal(new Dictionary<int, string> { [1] = "NoSolutionWithinBudget", [2] = "BestEffort" }, baseline);
        Assert.Empty(ProblemNestRegressionTests.Baseline("no baseline yet"));
    }

    [Fact]
    public void Baseline_ReadsTheCutOffLineApartFromThePlanningLine()
    {
        const string comments = "planning: 1=Ready 2=BestEffort\nplanning with cut-offs: 1=Ready 2=Ready";

        Assert.Equal(new Dictionary<int, string> { [1] = "Ready", [2] = "BestEffort" },
            ProblemNestRegressionTests.Baseline(comments));
        Assert.Equal(new Dictionary<int, string> { [1] = "Ready", [2] = "Ready" },
            ProblemNestRegressionTests.Baseline(comments, ProblemNestRegressionTests.CutOffPlanningLine));
    }

    [Fact]
    public void Checks_SelectNestsGuardingAWantedAreaAndThoseNamingNone()
    {
        var guards = ProblemNestRegressionTests.Checks(" Part-Order, entries ");

        Assert.Equal(["part-order", "entries"], guards);
        Assert.True(ProblemNestRegressionTests.Selected(guards, []));
        Assert.True(ProblemNestRegressionTests.Selected(guards, ["entries", "grid"]));
        Assert.False(ProblemNestRegressionTests.Selected(guards, ["grid"]));
        Assert.True(ProblemNestRegressionTests.Selected([], ["grid"]));
        Assert.Throws<ArgumentException>(() => ProblemNestRegressionTests.Checks("part-order, lead-ins"));
    }

    [Fact]
    public void SheetBaseline_ReadsTheAutoNestLine()
    {
        Assert.Equal(2, ProblemNestRegressionTests.SheetBaseline("Three sheets before the fix.\nchecks: auto-nest\nauto-nest: 2 sheets"));
        Assert.Null(ProblemNestRegressionTests.SheetBaseline("planning: 1=Ready"));
        Assert.Equal(["auto-nest"], ProblemNestRegressionTests.Checks("auto-nest"));
    }

    [Fact]
    public void EndBaseline_ReadsWhereTheLastSheetEndsWhenTheLineSaysSo()
    {
        const string comments = "Ended at 75.2 before the fix.\nchecks: auto-nest\nauto-nest: 1 sheets, ends 62.2";

        Assert.Equal(1, ProblemNestRegressionTests.SheetBaseline(comments));
        Assert.Equal(62.2, ProblemNestRegressionTests.EndBaseline(comments));
        Assert.Equal(1575, ProblemNestRegressionTests.EndBaseline("auto-nest: 2 sheets, ends at 1575"));
        Assert.Null(ProblemNestRegressionTests.EndBaseline("It ends 80 along.\nauto-nest: 2 sheets"));
        Assert.Null(ProblemNestRegressionTests.EndBaseline("planning: 1=Ready"));
    }

    [Fact]
    public void AutoNestBaselineLine_RoundsTheEndUpSoTheResultMeetsIt()
    {
        var line = ProblemNestRegressionTests.AutoNestBaselineLine(1, 62.132);

        Assert.Equal("auto-nest: 1 sheets, ends 62.2", line);
        Assert.Equal(1, ProblemNestRegressionTests.SheetBaseline(line));
        Assert.True(62.132 <= ProblemNestRegressionTests.EndBaseline(line) + ProblemNestRegressionTests.EndTolerance);
        Assert.Equal("auto-nest: 2 sheets, ends 40", ProblemNestRegressionTests.AutoNestBaselineLine(2, 40));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void LastSheetEnd_MeasuresFromTheNearerEndInEveryQuadrant(int quadrant)
    {
        // A 4 x 3 rectangle 2 from one end of a 20-long sheet ends 6 along, whichever end that is.
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 3);
        program.LineTo(0, 3);
        program.LineTo(0, 0);
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(program), 1);
        var stock = new NestPlateStock("sheet", new OpenNest.Geometry.Size(10, 20), quadrant: quadrant);
        var job = new NestJob(new[] { part }, new[] { stock });
        var sheetLeft = quadrant is 1 or 4 ? 0 : -20;

        foreach (var x in new[] { sheetLeft + 2.0, sheetLeft + 20 - 2 - 4.0 })
        {
            var builder = new NestJobResultBuilder(job);
            builder.AddSheet(stock, new[] { (part.Id, x, stock.WorkArea.Bottom, 0.0) });

            Assert.Equal(6, ProblemNestRegressionTests.LastSheetEnd(job, builder.Build(NestJobStopReason.Completed)), 6);
        }
    }

    [Fact]
    public void LayoutSaved_ReadsTheMarkAndLeavesTheNumbersReadable()
    {
        const string comments = "The layout is saved: see below.\nchecks: auto-nest\nauto-nest: 1 sheets, ends 62.2, layout saved";

        Assert.True(ProblemNestRegressionTests.LayoutSaved(comments));
        Assert.Equal(1, ProblemNestRegressionTests.SheetBaseline(comments));
        Assert.Equal(62.2, ProblemNestRegressionTests.EndBaseline(comments));
        Assert.False(ProblemNestRegressionTests.LayoutSaved("Hand layout saved in revision 1.\nauto-nest: 2 sheets"));
        Assert.False(ProblemNestRegressionTests.LayoutSaved(null));
    }

    [Fact]
    public void WithAutoNestLine_ReplacesTheLineOrAddsOne()
    {
        const string line = "auto-nest: 1 sheets, ends 62.2, layout saved";

        Assert.Equal("Ended at 75.2.\r\nchecks: auto-nest\r\n" + line + "\r\nnotes",
            ProblemNestRegressionTests.WithAutoNestLine("Ended at 75.2.\r\nchecks: auto-nest\r\n  Auto-Nest: 2 sheets\r\nnotes", line));
        Assert.Equal("checks: auto-nest\n" + line, ProblemNestRegressionTests.WithAutoNestLine("checks: auto-nest\n\n", line));
        Assert.Equal(line, ProblemNestRegressionTests.WithAutoNestLine(null, line));
    }

    [Fact]
    public void WithLayout_SavesTheResultAsTheNestsPlatesAndGivesTheSameJobAgain()
    {
        var bytes = HandLaidNest(out var record);
        var job = ProblemNestRegressionTests.AutoNestJob(bytes);
        var stock = Assert.Single(job.Plates);
        var part = Assert.Single(job.Parts);
        var builder = new NestJobResultBuilder(job);
        builder.AddSheet(stock, new[]
        {
            (part.Id, 1.0, 2.0, 0.0),
            (part.Id, 9.5, 2.0, System.Math.PI / 2),
            (part.Id, 1.0, 5.25, 0.0),
        });
        var result = builder.Build(NestJobStopReason.Completed);
        const string comments = "checks: auto-nest\nauto-nest: 1 sheets, ends 9.5, layout saved";

        var (archive, updated) = ProblemNestRegressionTests.WithLayout(bytes, record, job, result, comments);

        Assert.False(ProblemNestRegressionTests.SameLayout(bytes, job, result));
        Assert.True(ProblemNestRegressionTests.SameLayout(archive, job, result));
        // Read again, the nest's drawings have new ids: the layout is still the saved one.
        var reread = ProblemNestRegressionTests.AutoNestJob(archive);
        Assert.True(ProblemNestRegressionTests.SameLayout(archive, reread, Relaid(reread, result)));
        Assert.Equal(comments, updated.Comments);
        Assert.Equal(record.Id, updated.Id);
        Assert.Equal("problem", updated.Name);
        Assert.Equal(1, updated.PlateCount);
        Assert.Equal(3, updated.PartCount);
        using var stream = new MemoryStream(archive);
        var nest = new NestReader(stream).Read();
        Assert.Equal(comments, nest.Notes);
        Assert.Equal(3, Assert.Single(nest.Drawings).Quantity.Nested);
        // The saved plate is the sheet the job nested on, so the nest gives that job again.
        var sheet = Assert.Single(reread.Plates);
        Assert.Equal(stock.Size, sheet.Size);
        Assert.Equal(stock.PartSpacing, sheet.PartSpacing);
        Assert.Equal(stock.EdgeSpacing, sheet.EdgeSpacing);
        Assert.Equal(stock.Quadrant, sheet.Quadrant);
        Assert.Equal(stock.ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)),
            sheet.ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)));
        Assert.Equal(3, Assert.Single(reread.Parts).Quantity);
    }

    [Fact]
    public void SameLayout_TellsAMovedOrTurnedPartAndAnotherSheetCount()
    {
        var bytes = HandLaidNest(out var record);
        var job = ProblemNestRegressionTests.AutoNestJob(bytes);
        var stock = job.Plates[0];
        var id = job.Parts[0].Id;
        NestJobResult Layout(params (string, double, double, double)[][] sheets)
        {
            var builder = new NestJobResultBuilder(job);
            foreach (var sheet in sheets)
                builder.AddSheet(stock, sheet);
            return builder.Build(NestJobStopReason.Completed);
        }
        var saved = Layout([(id, 1.0, 2.0, 0.0), (id, 6.0, 2.0, 0.0), (id, 11.0, 2.0, 0.0)]);
        var (archive, _) = ProblemNestRegressionTests.WithLayout(bytes, record, job, saved, "");

        // Part order does not matter, and neither does a whole turn.
        Assert.True(ProblemNestRegressionTests.SameLayout(archive, job,
            Layout([(id, 11.0, 2.0, 2 * System.Math.PI), (id, 1.0, 2.0, 0.0), (id, 6.0, 2.0, 0.0)])));
        Assert.False(ProblemNestRegressionTests.SameLayout(archive, job,
            Layout([(id, 1.0, 2.0, 0.0), (id, 6.0, 2.0, 0.0), (id, 11.001, 2.0, 0.0)])));
        Assert.False(ProblemNestRegressionTests.SameLayout(archive, job,
            Layout([(id, 1.0, 2.0, 0.0), (id, 6.0, 2.0, 0.0), (id, 11.0, 2.0, System.Math.PI)])));
        Assert.False(ProblemNestRegressionTests.SameLayout(archive, job,
            Layout([(id, 1.0, 2.0, 0.0), (id, 6.0, 2.0, 0.0)], [(id, 1.0, 2.0, 0.0)])));
    }

    [Fact]
    public void CannotSave_KeepsPlatesOtherTestsReadAndSheetSizesTheJobNeeds()
    {
        var bytes = HandLaidNest(out _);
        var job = ProblemNestRegressionTests.AutoNestJob(bytes);
        var builder = new NestJobResultBuilder(job);
        builder.AddSheet(job.Plates[0], new[] { (job.Parts[0].Id, 1.0, 2.0, 0.0) });
        var run = new ProblemNestRegressionTests.AutoNestRun(1, 5, null, job, builder.Build(NestJobStopReason.Completed));

        Assert.Null(ProblemNestRegressionTests.CannotSave(["auto-nest"], run));
        Assert.Contains("part-order", ProblemNestRegressionTests.CannotSave(["auto-nest", "part-order"], run));
        // A second sheet size the layout does not use would leave the job when the plates are replaced.
        var wider = new NestJob(job.Parts, job.Plates.Append(
            new NestPlateStock("other", new OpenNest.Geometry.Size(30, 40), partSpacing: 0.25)));
        var partial = new NestJobResultBuilder(wider);
        partial.AddSheet(wider.Plates[0], new[] { (wider.Parts[0].Id, 1.0, 2.0, 0.0) });
        Assert.Contains("sheet size", ProblemNestRegressionTests.CannotSave(["auto-nest"],
            new ProblemNestRegressionTests.AutoNestRun(1, 5, null, wider, partial.Build(NestJobStopReason.Completed))));
    }

    // The same poses for another reading of the same nest, whose part ids differ.
    private static NestJobResult Relaid(NestJob job, NestJobResult result)
    {
        var builder = new NestJobResultBuilder(job);
        foreach (var sheet in result.Plates)
            builder.AddSheet(job.Plates[0], sheet.Placements.Select(p => (job.Parts[0].Id, p.X, p.Y, p.Rotation)));
        return builder.Build(NestJobStopReason.Completed);
    }

    // Three 4 x 3 rectangles wanted, one laid by hand on a 20 x 40 sheet with a clamp.
    private static byte[] HandLaidNest(out NestRecord record)
    {
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 3);
        program.LineTo(0, 3);
        program.LineTo(0, 0);
        var drawing = new Drawing("rectangle", program);
        drawing.Quantity.Required = 3;
        var nest = new Nest("problem");
        nest.Drawings.Add(drawing);
        var plate = new Plate(20, 40)
        {
            PartSpacing = 0.25,
            EdgeSpacing = new OpenNest.Geometry.Spacing(0.3, 0.75, 0.3, 0.25),
        };
        plate.ClampZones.Add(new OpenNest.Clamps.ClampZone("clamp-1", new OpenNest.Geometry.Box(19.25, 0, 1.5, 0.625)));
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new OpenNest.Geometry.Vector(30, 10);
        plate.Parts.Add(part);
        nest.Plates.Add(plate);
        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        record = new NestRecord { Id = Guid.NewGuid(), Revision = 1, Name = "problem", Comments = "checks: auto-nest" };
        return stream.ToArray();
    }

    [Fact]
    public void FillBaseline_ReadsPlateCountsWithLatticeByDefault()
    {
        var baseline = ProblemNestRegressionTests.FillBaseline(
            "Fill placed 30 before the fix.\r\nchecks: fill\nfill: 1=33 3 = 120\nnotes");

        Assert.Equal("Lattice", baseline.Strategy);
        Assert.Equal(new Dictionary<int, int> { [1] = 33, [3] = 120 }, baseline.Counts);
        Assert.Equal(["fill"], ProblemNestRegressionTests.Checks("fill"));
        Assert.Equal("fill:", ProblemNestRegressionTests.FillLinePrefix(baseline.Strategy));
    }

    [Fact]
    public void FillBaseline_ReadsTheStrategyInParentheses()
    {
        var baseline = ProblemNestRegressionTests.FillBaseline("Fill (strip): 2=48");

        Assert.Equal("Strip", baseline.Strategy);
        Assert.Equal(new Dictionary<int, int> { [2] = 48 }, baseline.Counts);
        Assert.Equal("fill (Strip):", ProblemNestRegressionTests.FillLinePrefix(baseline.Strategy));
        Assert.Throws<NotSupportedException>(() => ProblemNestRegressionTests.FillBaseline("fill (Spiral): 1=10"));
    }

    [Fact]
    public void FillBaseline_IgnoresOtherLinesAndIsNullWithoutOne()
    {
        Assert.Null(ProblemNestRegressionTests.FillBaseline("Fill placed 30.\nchecks: fill\nauto-nest: 2 sheets\nplanning: 1=Ready"));
        Assert.Null(ProblemNestRegressionTests.FillBaseline(null));
        Assert.Empty(ProblemNestRegressionTests.FillBaseline("fill:").Counts);
    }

    [Fact]
    public void Wanted_PlansEveryNestForAreasThatReorderEveryPlate()
    {
        Assert.Equal(["cut-offs", "grid"], ProblemNestRegressionTests.Wanted(["cut-offs", "grid"]));
        Assert.Empty(ProblemNestRegressionTests.Wanted(["cut-offs", "part-order"]));
        Assert.Empty(ProblemNestRegressionTests.Wanted(["entries"]));
    }

    [Fact]
    public void Rank_OrdersReadyAboveBestEffortAboveBudgetAboveBlockedAboveTimeout()
    {
        var ranks = new[] { "Ready", "BestEffort", "NoSolutionWithinBudget", "UnsupportedGeometry", "Overlap", "Timeout" }
            .Select(ProblemNestRegressionTests.Rank).ToArray();

        Assert.Equal([3, 2, 1, 0, 0, -1], ranks);
    }
}
