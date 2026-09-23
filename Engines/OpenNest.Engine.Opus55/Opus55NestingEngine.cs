using System;
using System.Threading;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.Opus55;

/// <summary>
/// Frontier-advance NFP packer with look-ahead stock selection.
///
/// Per sheet, <see cref="FrontierPacker"/> keeps the exact free region of every
/// (part type, orientation) as inner-fit rectangle minus no-fit polygons, and repeatedly places
/// either the largest part that fills a gap behind the packing front, or the part that advances
/// the front least per unit of area covered. Across sheets, every available stock size is
/// trial-packed and the one with the lowest estimated whole-job cost (its own net area plus the
/// remaining demand at the best efficiency seen) is committed. A handful of deterministic
/// strategy variants (front direction, area exponent) run whole-job, and the cheapest wins.
///
/// Fully deterministic: no clocks or randomness influence any decision.
/// </summary>
public sealed class Opus55NestingEngine : INestingEngine
{
    /// <summary>
    /// Extra clearance beyond the stock's part spacing, in job units. Validators polygonize arcs
    /// circumscribed at 0.01 per side, so two tangent true arcs can read as up to 0.02 closer
    /// than they are; the rest absorbs Clipper's 1e-4 grid and inner-fit clamping.
    /// </summary>
    internal const double ClearanceMargin = 0.022;

    /// <summary>Strategy variants, tried in order: (front direction, area exponent beta).</summary>
    private static readonly (PackAxis Axis, double Beta)[] Variants =
    {
        (PackAxis.X, 1.0),
        (PackAxis.Y, 1.0),
        (PackAxis.X, 0.5),
        (PackAxis.Y, 0.5),
        (PackAxis.X, 1.5),
        (PackAxis.Y, 1.5),
    };

    /// <summary>
    /// Deterministic work budget, in free-region subtractions, after which no further variant
    /// starts. Keeps big jobs well inside benchmark timeouts without consulting a clock.
    /// </summary>
    internal long WorkBudget { get; init; } = 1_500_000;

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        var types = PartCatalog.Build(job);
        var solver = new Solver(job, types, progress, token);

        // Demand that no offered stock can hold in any allowed orientation is reported unplaced.
        var demand = new int[types.Count];
        foreach (var type in types)
        {
            var placeable = job.Plates.Any(stock =>
                stock.Quantity != 0
                && type.Orientations.Any(o => FrontierPacker.Fits(o, FrontierPacker.WorkArea(stock)))
            );
            demand[type.Index] = placeable ? type.Part.Quantity : 0;
        }

        Plan? best = null;
        foreach (var (axis, beta) in Variants)
        {
            token.ThrowIfCancellationRequested();
            if (best != null && solver.Work.Value >= WorkBudget)
                break;
            var plan = solver.Plan(demand, axis, beta);
            if (best == null || plan.IsBetterThan(best))
                best = plan;
            if (best.Unplaced == 0 && best.Sheets.Count == 0)
                break;
        }

        // The last sheets hold the leftovers, which is where waste concentrates; re-plan them.
        best = solver.ImproveTail(best!, WorkBudget * 2);
        return BuildResult(job, types, best, progress);
    }

    /// <summary>Shared state for one solve: job, catalog, NFP caches, effort meter.</summary>
    private sealed class Solver(
        NestJob job,
        IReadOnlyList<PartType> types,
        IProgress<NestJobProgress>? progress,
        CancellationToken token
    )
    {
        private const int MaxTail = 3;
        private readonly Dictionary<double, NoFitCache> caches = new();

        public WorkCounter Work { get; } = new();

        private double Penalty => job.Plates.Count == 0 ? 0 : job.Plates.Max(SheetEconomics.SheetArea);

        public Plan Plan(int[] demand, PackAxis axis, double beta)
        {
            var run = Decode(demand, axis, beta, new Dictionary<string, int>(StringComparer.Ordinal), job.Options.MaxPlates, null);
            var unplaced = types.Sum(t => t.Part.Quantity) - run.Sheets.Sum(s => s.Parts.Count);
            var reason = run.Reason;
            if (unplaced > 0 && reason == NestJobStopReason.Completed)
                reason = NestJobStopReason.NoPlacementFound; // Demand no stock can hold.
            return new Plan(run.Sheets, run.Net + unplaced * Penalty, unplaced, reason);
        }

        /// <summary>
        /// Takes the parts off the last k sheets (k = 1..3) and re-plans just that demand with
        /// every stock forced as the first sheet, under every variant; the cheapest complete
        /// re-plan that beats the current tail replaces it. Tails are small and effort is metered.
        /// </summary>
        public Plan ImproveTail(Plan plan, long budget)
        {
            var sheets = plan.Sheets.ToList();
            for (var k = 1; k <= System.Math.Min(MaxTail, sheets.Count); k++)
            {
                if (Work.Value >= budget)
                    break;
                var prefix = sheets.Take(sheets.Count - k).ToList();
                var tail = sheets.Skip(sheets.Count - k).ToList();
                var tailParts = tail.Sum(s => s.Parts.Count);
                var tailNet = tail.Sum(s => SheetEconomics.NetArea(job.Options, s));
                var tailDemand = new int[types.Count];
                foreach (var part in tail.SelectMany(s => s.Parts))
                    tailDemand[part.Orientation.TypeIndex]++;
                var used = prefix
                    .GroupBy(s => s.Stock.Id)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                int? cap = job.Options.MaxPlates is int max ? max - prefix.Count : null;

                Run? bestRun = null;
                var bestNet = tailNet - 1e-9 * System.Math.Max(1, tailNet);
                foreach (var (axis, beta) in Variants)
                    foreach (var first in job.Plates)
                    {
                        token.ThrowIfCancellationRequested();
                        var run = Decode(tailDemand, axis, beta, used, cap, first);
                        if (run.Sheets.Sum(s => s.Parts.Count) != tailParts || run.Net >= bestNet)
                            continue;
                        bestRun = run;
                        bestNet = run.Net;
                    }

                if (bestRun == null)
                    continue;
                sheets = prefix.Concat(bestRun.Sheets).ToList();
                plan = plan with { Sheets = sheets.ToList(), Cost = plan.Cost - (tailNet - bestRun.Net) };
            }
            return plan;
        }

        private NoFitCache CacheFor(NestPlateStock stock)
        {
            var clearance = System.Math.Max(0, stock.PartSpacing) + ClearanceMargin;
            if (!caches.TryGetValue(clearance, out var cache))
                caches[clearance] = cache = new NoFitCache(clearance);
            return cache;
        }

        /// <summary>
        /// Greedy sheet-by-sheet decode. <paramref name="usedBefore"/> seeds finite-stock
        /// accounting, <paramref name="sheetCap"/> bounds the sheets this run may add, and
        /// <paramref name="first"/>, when set, forces the stock of the first sheet.
        /// </summary>
        private Run Decode(
            int[] demand,
            PackAxis axis,
            double beta,
            IReadOnlyDictionary<string, int> usedBefore,
            int? sheetCap,
            NestPlateStock? first
        )
        {
            var remaining = (int[])demand.Clone();
            var used = job.Plates.ToDictionary(s => s.Id, s => usedBefore.GetValueOrDefault(s.Id), StringComparer.Ordinal);
            var sheets = new List<SheetFill>();
            var net = 0.0;
            NestJobStopReason reason;

            while (true)
            {
                if (remaining.All(r => r == 0))
                {
                    reason = NestJobStopReason.Completed;
                    break;
                }
                if (sheetCap is int cap && sheets.Count >= cap)
                {
                    reason = NestJobStopReason.PlateLimitReached;
                    break;
                }

                var trials = new List<(SheetFill Fill, double Net)>();
                foreach (var stock in job.Plates)
                {
                    token.ThrowIfCancellationRequested();
                    if (sheets.Count == 0 && first != null && !ReferenceEquals(stock, first))
                        continue;
                    if (stock.Quantity is int available && used[stock.Id] >= available)
                        continue;
                    progress?.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, stock.Id, sheets.Count, 0, 0));
                    var packer = new FrontierPacker(types, CacheFor(stock), stock, axis, beta, Work);
                    var fill = packer.Fill(remaining, token);
                    if (fill.Parts.Count > 0)
                        trials.Add((fill, SheetEconomics.NetArea(job.Options, fill)));
                }

                if (trials.Count == 0)
                {
                    var exhausted = job.Plates.Any(s => s.Quantity is int q && used[s.Id] >= q);
                    reason = exhausted ? NestJobStopReason.StockExhausted : NestJobStopReason.NoPlacementFound;
                    break;
                }

                // Look-ahead: charge whatever a trial leaves behind at the best efficiency any trial
                // achieved, so a sheet that finishes the job competes fairly with a denser partial one.
                var remainingArea = types.Sum(t => remaining[t.Index] * t.Area);
                var bestRatio = trials.Min(t => t.Net / System.Math.Max(t.Fill.PartArea, 1e-12));
                var chosen = trials
                    .Select((t, order) => (t.Fill, t.Net, order, Estimate: t.Net + System.Math.Max(0, remainingArea - t.Fill.PartArea) * bestRatio))
                    .OrderBy(t => t.Estimate)
                    .ThenByDescending(t => t.Fill.Parts.Count)
                    .ThenBy(t => t.order)
                    .First();

                sheets.Add(chosen.Fill);
                net += chosen.Net;
                used[chosen.Fill.Stock.Id]++;
                foreach (var part in chosen.Fill.Parts)
                    remaining[part.Orientation.TypeIndex]--;
            }

            return new Run(sheets, net, reason);
        }
    }

    private sealed record Run(IReadOnlyList<SheetFill> Sheets, double Net, NestJobStopReason Reason);

    private static NestJobResult BuildResult(
        NestJob job,
        IReadOnlyList<PartType> types,
        Plan plan,
        IProgress<NestJobProgress>? progress
    )
    {
        var placed = new int[types.Count];
        var plates = new List<NestJobPlateResult>(plan.Sheets.Count);
        var committedParts = 0;
        foreach (var sheet in plan.Sheets)
        {
            var placements = sheet.Parts.Select(p =>
            {
                var type = types[p.Orientation.TypeIndex];
                return new NestJobPlacement(type.Part.Id, placed[type.Index]++, p.X, p.Y, p.Orientation.Rotation);
            });
            plates.Add(new NestJobPlateResult(plates.Count, sheet.Stock, placements.ToList()));
            committedParts += sheet.Parts.Count;
            progress?.Report(new NestJobProgress(NestJobStage.PlateCommitted, sheet.Stock.Id, plates.Count - 1, plates.Count, committedParts));
        }

        var fulfillment = types.Select(t => new PartFulfillment(t.Part.Id, t.Part.Quantity, placed[t.Index], t.Part.Quantity - placed[t.Index]));
        var usage = job.Plates.Select(stock =>
        {
            var count = plan.Sheets.Count(s => ReferenceEquals(s.Stock, stock));
            return new StockUsage(stock.Id, count, stock.Quantity - count);
        });
        var status = plan.Unplaced == 0 ? NestJobStatus.Complete : NestJobStatus.Incomplete;
        return new NestJobResult(status, plan.Reason, plates, fulfillment.ToList(), usage.ToList());
    }

    private sealed record Plan(IReadOnlyList<SheetFill> Sheets, double Cost, int Unplaced, NestJobStopReason Reason)
    {
        public bool IsBetterThan(Plan other)
        {
            if (Unplaced != other.Unplaced)
                return Unplaced < other.Unplaced;
            var scale = System.Math.Max(1, System.Math.Max(Cost, other.Cost));
            if (System.Math.Abs(Cost - other.Cost) > 1e-9 * scale)
                return Cost < other.Cost;
            return Sheets.Count < other.Sheets.Count;
        }
    }
}

/// <summary>Deterministic effort meter shared by all packers in one solve.</summary>
internal sealed class WorkCounter
{
    private long value;
    public long Value => Interlocked.Read(ref value);
    public void Add(long amount) => Interlocked.Add(ref value, amount);
}
