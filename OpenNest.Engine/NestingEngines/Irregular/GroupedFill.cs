#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Fills one sheet with like parts grouped: each type with three or more copies packs as one
/// group, the groups in turn (<see cref="FrontierPacker"/> group ranks), and then the copies held
/// back from the groups and the types too few to group fill the gaps the groups left.
///
/// Holding copies back is what keeps the groups dense: the last copies of a group often start a
/// new row or column on their own, where they block room a later group could use whole; placed
/// last, they drop into whatever gaps remain. The search tries group orders with nothing held
/// back, then, from the best orders, holds back copies type by type while that improves the sheet.
/// When the sheet then holds everything, the best candidates and every order are packed again
/// inside a strip ending just short of the front, while that still holds everything and improves the sheet:
/// Fill blocks are sized to the free room, so a shorter strip yields denser groups.
///
/// Every fill is metered by the shared <see cref="WorkCounter"/>; the search starts no fill once
/// the budget is spent and keeps the best sheet so far.
/// </summary>
internal sealed class GroupedFill(
    IReadOnlyList<PartType> types,
    Func<IReadOnlyList<int>?, double?, FrontierPacker> packer,
    PackAxis axis,
    bool shrink,
    Comparison<SheetFill> compare,
    WorkCounter counter,
    long budget)
{
    /// <summary>Fewest copies of a type that pack as a group.</summary>
    internal const int MinGroup = 3;

    /// <summary>Group counts up to this are searched in every order; more use ranked orders.</summary>
    private const int MaxPermuted = 4;

    /// <summary>Best candidates carried into hold-back search and shorter strips.</summary>
    private const int Refined = 3;

    /// <summary>Most copies held back from one group.</summary>
    private const int MaxHeldBack = 4;

    /// <summary>Most shorter strips tried once everything fits the sheet.</summary>
    private const int MaxShrinks = 4;

    /// <summary>How much shorter than the best front each shorter strip ends.</summary>
    private const double Shrink = 0.25;

    /// <summary>Whether grouping can change anything: two or more types of which one groups.</summary>
    public static bool Applies(IReadOnlyList<PartType> types, IReadOnlyList<int> remaining) =>
        types.Count(t => remaining[t.Index] > 0) > 1 && types.Any(t => remaining[t.Index] >= MinGroup);

    private sealed record Candidate(int[] Order, int[] Held, SheetFill Fill);

    public SheetFill Fill(IReadOnlyList<int> remaining, CancellationToken token)
    {
        var grouped = types.Where(t => remaining[t.Index] >= MinGroup && t.Orientations.Count > 0)
            .Select(t => t.Index).ToArray();
        var none = new int[types.Count];
        var orders = Orders(grouped, remaining).ToList();
        var ranked = Evaluate(orders.Select(o => (o, none)), remaining, null, token);
        ranked = Refine(ranked.Take(Refined), remaining, null, token);
        var best = ranked[0].Fill;

        var total = remaining.Sum();
        for (var i = 0; shrink && i < MaxShrinks && best.Parts.Count == total && counter.Value < budget; i++)
        {
            var reach = Front(best) - Shrink;
            var seeds = ranked.Take(Refined).Select(c => (c.Order, c.Held)).Concat(orders.Select(o => (o, none)));
            var tried = Evaluate(seeds, remaining, reach, token);
            tried = Refine(tried.Take(1), remaining, reach, token).Concat(tried.Skip(1)).ToList();
            if (tried[0].Fill.Parts.Count < total || compare(tried[0].Fill, best) >= 0)
                break;
            best = tried[0].Fill;
            ranked = tried;
        }
        return best;
    }

    private double Front(SheetFill fill) =>
        axis == PackAxis.X ? fill.Parts.Max(p => p.Right) : fill.Parts.Max(p => p.Top);

    /// <summary>Packs each (order, held) in turn, best first; ties keep the earlier one. At least
    /// one is packed whatever the budget.</summary>
    private List<Candidate> Evaluate(IEnumerable<(int[] Order, int[] Held)> seeds, IReadOnlyList<int> remaining,
        double? reach, CancellationToken token)
    {
        var results = new List<Candidate>();
        foreach (var (order, held) in seeds)
        {
            if (results.Count > 0 && counter.Value >= budget)
                break;
            results.Add(new Candidate(order, held, Run(order, held, remaining, reach, token)));
        }
        return Sort(results);
    }

    /// <summary>Holds back copies of one type at a time, keeping each count that improves the sheet.</summary>
    private List<Candidate> Refine(IEnumerable<Candidate> starts, IReadOnlyList<int> remaining, double? reach,
        CancellationToken token)
    {
        var results = new List<Candidate>();
        foreach (var start in starts)
        {
            var incumbent = start;
            foreach (var type in start.Order)
            {
                var limit = System.Math.Min(MaxHeldBack, remaining[type] - MinGroup);
                for (var count = 0; count <= limit && counter.Value < budget; count++)
                {
                    if (count == incumbent.Held[type])
                        continue;
                    var held = incumbent.Held.ToArray();
                    held[type] = count;
                    var fill = Run(start.Order, held, remaining, reach, token);
                    if (compare(fill, incumbent.Fill) < 0)
                        incumbent = new Candidate(start.Order, held, fill);
                }
            }
            results.Add(incumbent);
        }
        return Sort(results);
    }

    private List<Candidate> Sort(List<Candidate> candidates) =>
        candidates.Select((c, i) => (c, i)).OrderBy(x => x.c.Fill, Comparer<SheetFill>.Create(compare))
            .ThenBy(x => x.i).Select(x => x.c).ToList();

    /// <summary>The groups packed in <paramref name="order"/>, less the held-back copies, then
    /// everything else filling around them.</summary>
    private SheetFill Run(int[] order, int[] held, IReadOnlyList<int> remaining, double? reach, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var ranks = new int[types.Count];
        var groups = new int[types.Count];
        for (var i = 0; i < order.Length; i++)
        {
            ranks[order[i]] = i;
            groups[order[i]] = remaining[order[i]] - held[order[i]];
        }
        var first = packer(ranks, reach).Fill(groups, token);
        var rest = remaining.ToArray();
        foreach (var part in first.Parts)
            rest[part.Orientation.TypeIndex]--;
        if (rest.All(r => r == 0))
            return first;
        var second = packer(null, reach).Fill(rest, token, first.Parts);
        return first with
        {
            Parts = first.Parts.Concat(second.Parts).ToList(),
            PartArea = first.PartArea + second.PartArea,
        };
    }

    /// <summary>
    /// Every order of up to <see cref="MaxPermuted"/> groups; otherwise the groups ranked by total
    /// area, part area and part size, largest first and last.
    /// </summary>
    private IEnumerable<int[]> Orders(int[] grouped, IReadOnlyList<int> remaining)
    {
        if (grouped.Length <= MaxPermuted)
            return Permutations(grouped);
        var size = new Func<int, double>(t => types[t].Orientations.Max(o => System.Math.Max(o.Width, o.Height)));
        var keys = new Func<int, double>[] { t => types[t].Area * remaining[t], t => types[t].Area, size };
        var orders = new List<int[]>();
        foreach (var key in keys)
        {
            var descending = grouped.OrderByDescending(key).ThenBy(t => t).ToArray();
            foreach (var order in new[] { descending, descending.Reverse().ToArray() })
                if (!orders.Any(o => o.SequenceEqual(order)))
                    orders.Add(order);
        }
        return orders;
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items.ToArray();
            yield break;
        }
        for (var i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, j) => j != i).ToArray();
            foreach (var tail in Permutations(rest))
                yield return new[] { items[i] }.Concat(tail).ToArray();
        }
    }
}
