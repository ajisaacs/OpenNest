using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;
using OpenNest.Engine.CuttingPlanning;
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
    internal const string Timeout = "Timeout";
    internal const string PlanningLine = "planning:";
    internal const string CutOffPlanningLine = "planning with cut-offs:";
    internal const string ChecksLine = "checks:";

    /// <summary>The planner areas a nest can guard; docs/problem-nests.md says which changes touch each.</summary>
    internal static readonly string[] Areas = ["part-order", "entries", "contours", "geometry-checks", "cut-offs", "grid"];

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
        var wanted = Wanted(named);
        if (wanted.Length != named.Length)
            output.WriteLine($"{string.Join(", ", named.Intersect(WholeCorpusAreas))} change the search on every plate, "
                + "so every nest is planned.");

        var failures = new List<string>();
        var usage = new List<(string Plate, double Share)>();
        foreach (var record in records)
        {
            var guards = Checks(CommentLine(record.Comments, ChecksLine));
            if (!Selected(guards, wanted))
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
            return (planned.Result.Status == CuttingPlanStatus.Cancelled ? Timeout : Outcome(planned), planned);
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
