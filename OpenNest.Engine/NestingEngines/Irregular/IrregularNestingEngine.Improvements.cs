#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines.Irregular;

public sealed partial class IrregularNestingEngine
{
    // The passes that improve a plan already made: each returns the plan it was given unless it
    // found one that beats it.
    private sealed partial class Solver
    {
        /// <summary>Most sheets, counted from the last, a tail re-plan takes up.</summary>
        private const int MaxTail = 3;

        private readonly Dictionary<int, IReadOnlyList<Orientation>> squeezeOrientations = new();

        /// <summary>Next private orientation index: far above the catalog's, pair and block poses.</summary>
        private int solvedOrientations = 1_000_000;

        /// <summary>
        /// Takes the parts off the last k sheets (k = 1..3) and re-plans just that demand with
        /// every stock forced as the first sheet, under every variant; the cheapest complete
        /// re-plan that beats the current tail replaces it. Tails are small and effort is metered.
        /// </summary>
        public Plan ImproveTail(Plan plan, long budget)
        {
            engine.TailReplanning?.Invoke(budget - Work.Value);
            var sheets = plan.Sheets.ToList();
            for (var k = 1; k <= System.Math.Min(MaxTail, sheets.Count); k++)
            {
                if (Work.Value >= budget)
                    break;
                var prefix = sheets.Take(sheets.Count - k).ToList();
                var tail = sheets.Skip(sheets.Count - k).ToList();
                var tailNet = tail.Sum(s => NetCost(job.Options, s));
                var replan = ReplanTail(prefix, tail, tailNet);
                if (replan == null)
                    continue;
                sheets = prefix.Concat(replan.Sheets).ToList();
                plan = plan with { Sheets = sheets.ToList(), Cost = plan.Cost - (tailNet - replan.Net) };
                engine.TailReplaced?.Invoke(k);
            }
            return plan;
        }

        /// <summary>
        /// The cheapest decode of the tail's parts that places them all for less than
        /// <paramref name="tailNet"/>, the stock the sheets before it used counted as spent; null
        /// when none does.
        /// </summary>
        private Run? ReplanTail(List<SheetFill> prefix, List<SheetFill> tail, double tailNet)
        {
            var tailParts = tail.Sum(s => s.Parts.Count);
            var tailDemand = new int[types.Count];
            foreach (var part in tail.SelectMany(s => s.Parts))
                tailDemand[part.Orientation.TypeIndex]++;
            var used = prefix
                .GroupBy(s => s.Stock.Id)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            int? cap = job.Options.MaxPlates is int max ? max - prefix.Count : null;

            Run? best = null;
            var bestNet = tailNet - (NestJobCost.UsesExplicitCosts(job) ? 0 : CostTie * System.Math.Max(1, tailNet));
            foreach (var (axis, beta) in Variants)
                foreach (var first in job.Plates)
                {
                    token.ThrowIfCancellationRequested();
                    var run = Decode(tailDemand, new Strategy(axis, beta), used, cap, first);
                    if (run.Sheets.Sum(s => s.Parts.Count) != tailParts || run.Net >= bestNet)
                        continue;
                    best = run;
                    bestNet = run.Net;
                }
            return best;
        }

        /// <summary>
        /// Drops sheets while it can: the parts of a complete plan on one stock are split over
        /// one sheet fewer (by area, largest first onto the lightest sheet, then dealt in a snake)
        /// and each sheet packed by <see cref="EdgeFirstFill"/>. A split that packs every sheet
        /// replaces the plan when it beats it.
        /// </summary>
        public Plan ReduceSheets(Plan plan)
        {
            while (plan.Unplaced == 0 && plan.Sheets.Count >= 2)
            {
                token.ThrowIfCancellationRequested();
                var fewer = OnOneSheetFewer(plan);
                if (fewer == null)
                    break;
                plan = fewer;
            }
            return plan;
        }

        /// <summary>
        /// The plan's parts packed on one sheet fewer, when the first split that packs beats the
        /// plan; null otherwise, and for a plan on outlined or mixed stock, with too many parts or
        /// with more part area than the fewer sheets hold.
        /// </summary>
        private Plan? OnOneSheetFewer(Plan plan)
        {
            var stock = plan.Sheets[0].Stock;
            if (stock.Outline != null || plan.Sheets.Any(s => !ReferenceEquals(s.Stock, stock)))
                return null;
            var instances = plan.Sheets.SelectMany(s => s.Parts).Select(p => p.Orientation.TypeIndex)
                .OrderByDescending(t => types[t].Area).ThenBy(t => t).ToList();
            var target = plan.Sheets.Count - 1;
            var room = stock.WorkArea.Length * stock.WorkArea.Width;
            if (instances.Count > MaxReduceParts || instances.Sum(t => types[t].Area) > target * room)
                return null;

            foreach (var bins in Splits(instances, target))
            {
                var sheets = PackEdgeFirst(stock, bins);
                if (sheets == null)
                    continue;
                var candidate = PlanOf(job, sheets, NestJobCost.RequireFinite(sheets.Sum(s => NetCost(job.Options, s))), 0,
                    NestJobStopReason.Completed);
                return candidate.IsBetterThan(plan) ? candidate : null;
            }
            return null;
        }

        /// <summary>Each bin's parts packed edge first on a sheet of its own; null when a bin does not pack.</summary>
        private List<SheetFill>? PackEdgeFirst(NestPlateStock stock, int[][] bins)
        {
            var sheets = new List<SheetFill>();
            foreach (var bin in bins)
            {
                var fill = new EdgeFirstFill(types, CacheFor(stock), stock, ClampsFor(stock), Work,
                    () => new FrontierPacker(types, CacheFor(stock), PairsFor(stock), stock, PackAxis.X, 1.0, Work,
                        clamps: ClampsFor(stock)),
                    Orient).Fill(bin, token);
                if (fill == null)
                    return null;
                sheets.Add(fill);
            }
            return sheets;
        }

        /// <summary>Per-sheet demands for <paramref name="count"/> sheets: largest part first onto
        /// the lightest sheet, then the same parts dealt out in a snake.</summary>
        private IEnumerable<int[][]> Splits(IReadOnlyList<int> instances, int count)
        {
            var greedy = Enumerable.Range(0, count).Select(_ => new int[types.Count]).ToArray();
            var load = new double[count];
            foreach (var t in instances)
            {
                var lightest = 0;
                for (var i = 1; i < count; i++)
                    if (load[i] < load[lightest] - 1e-9)
                        lightest = i;
                greedy[lightest][t]++;
                load[lightest] += types[t].Area;
            }
            yield return greedy;
            if (count < 2)
                yield break;
            var snake = Enumerable.Range(0, count).Select(_ => new int[types.Count]).ToArray();
            for (var i = 0; i < instances.Count; i++)
            {
                var round = i / count;
                var slot = i % count;
                snake[round % 2 == 0 ? slot : count - 1 - slot][instances[i]]++;
            }
            if (!snake.Zip(greedy).All(p => p.First.SequenceEqual(p.Second)))
                yield return snake;
        }

        /// <summary>
        /// Packs the last sheet of a complete plan again inside ever shorter strips
        /// (<see cref="OrderedSqueeze"/>), along each of <see cref="SqueezeAxes"/>; a shorter
        /// sheet replaces it when the plan then beats the one given.
        /// </summary>
        public Plan Squeeze(Plan plan)
        {
            if (plan.Unplaced != 0 || plan.Sheets.Count == 0)
                return plan;
            var last = plan.Sheets[^1];
            var stock = last.Stock;
            if (stock.Outline != null || last.Parts.Count < 2 || last.Parts.Count > OrderedSqueeze.MaxParts)
                return plan;
            var best = plan;
            foreach (var axis in SqueezeAxes(job, stock))
            {
                token.ThrowIfCancellationRequested();
                var fill = new OrderedSqueeze(types, CacheFor(stock), stock, ClampsFor(stock), Work, SqueezeOrientations)
                    .Fill(last.Parts, axis, token);
                if (fill == null)
                    continue;
                var sheets = plan.Sheets.Take(plan.Sheets.Count - 1).Append(fill).ToList();
                var candidate = plan with
                {
                    Sheets = sheets,
                    Cost = NestJobCost.RequireFinite(plan.Cost - NetCost(job.Options, last) + NetCost(job.Options, fill)),
                };
                if (candidate.IsBetterThan(best))
                    best = candidate;
            }
            return best;
        }

        /// <summary>
        /// The orientations the squeeze offers <paramref name="type"/>: the catalog's, and for a
        /// part that may turn freely also every <see cref="SqueezeAngleStep"/> that gives another
        /// outline. Built once per type per solve, as each owns no-fit polygons.
        /// </summary>
        private IReadOnlyList<Orientation> SqueezeOrientations(PartType type)
        {
            if (squeezeOrientations.TryGetValue(type.Index, out var found))
                return found;
            var all = type.Orientations.ToList();
            if (type.Perimeter != null && all.Count > 0 && type.Part.Rotation.Kind == RotationPolicyKind.Automatic)
            {
                var angles = all.Select(o => o.Rotation).ToList();
                for (var turn = 0; turn * SqueezeAngleStep < 2 * System.Math.PI - 1e-9; turn++)
                    angles.Add(turn * SqueezeAngleStep);
                foreach (var angle in RotationCandidates.DistinctOutlines(type.Perimeter, angles).Skip(all.Count))
                    if (Orient(type, angle) is { } extra)
                        all.Add(extra);
            }
            return squeezeOrientations[type.Index] = all;
        }

        /// <summary>A pose of <paramref name="type"/> at a solved angle, with its own NFP cache key.</summary>
        private Orientation? Orient(PartType type, double angle) =>
            PartCatalog.CreateOrientation(type, solvedOrientations++, angle);
    }
}
