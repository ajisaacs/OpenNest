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
/// its applied lead-ins and cut-offs cleared and default scrap cut-offs added. Opt-in and private:
/// the nests and their baselines stay on the server, never in the repository. See docs/problem-nests.md.
/// </summary>
[Trait("Category", "ProblemNests")]
public partial class ProblemNestRegressionTests(ITestOutputHelper output)
{
    internal const string ServerVariable = "OPENNEST_PROBLEM_NESTS_SERVER";
    internal const string TimeoutVariable = "OPENNEST_PROBLEM_NESTS_TIMEOUT";
    internal const string CutOffsVariable = "OPENNEST_PROBLEM_NESTS_CUTOFFS";
    internal const string Timeout = "Timeout";
    internal const string PlanningLine = "planning:";
    internal const string CutOffPlanningLine = "planning with cut-offs:";
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

        var failures = new List<string>();
        foreach (var record in records)
        {
            var bytes = await repository.GetFileAsync(record.Id)
                ?? throw new InvalidOperationException($"'{record.Name}' has no file.");
            var baseline = Baseline(record.Comments);
            var observed = PlanAll(record.Name, bytes, false, baseline, limit, failures);
            if (baseline.Count == 0)
                failures.Add($"{record.Name} has no baseline. Add this line to its comments: {Line(PlanningLine, observed)}");
            if (Environment.GetEnvironmentVariable(CutOffsVariable) == "1")
                PlanAll(record.Name, bytes, true, [], limit, failures);
            else if (Baseline(record.Comments, CutOffPlanningLine) is { Count: > 0 } withCutOffs)
                PlanAll(record.Name, bytes, true, withCutOffs, limit, failures);
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // Plans each plate with parts and compares it with the baseline; plate number to outcome.
    private SortedDictionary<int, string> PlanAll(string name, byte[] bytes, bool cutOffs,
        Dictionary<int, string> baseline, TimeSpan limit, List<string> failures)
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
            var (outcome, planned) = Plan(plate, number, limit);
            observed[number] = outcome;
            var expected = baseline.GetValueOrDefault(number);
            output.WriteLine($"{label} plate {number}: {outcome} (baseline {expected ?? "none"}), "
                + $"{planned?.Result.Expansions ?? 0} expansions, {watch.Elapsed.TotalSeconds:0.0} s");
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
    internal static Dictionary<int, string> Baseline(string comments, string prefix = PlanningLine)
    {
        var line = (comments ?? "").Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return line == null ? []
            : BaselineEntry().Matches(line).ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);
    }

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
    public void Rank_OrdersReadyAboveBestEffortAboveBudgetAboveBlockedAboveTimeout()
    {
        var ranks = new[] { "Ready", "BestEffort", "NoSolutionWithinBudget", "UnsupportedGeometry", "Overlap", "Timeout" }
            .Select(ProblemNestRegressionTests.Rank).ToArray();

        Assert.Equal([3, 2, 1, 0, 0, -1], ranks);
    }
}
