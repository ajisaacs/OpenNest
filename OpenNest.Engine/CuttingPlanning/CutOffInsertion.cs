using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Places cut-off segments into a plan made without them. A segment is a fixed open cut that
/// is cut the same way wherever it goes and is never a rapid obstacle, so all that changes with
/// its place in the order are the rapid into its pierce and the rapid from its end to what
/// follows. Each segment goes as late as it can before the first part in its span of the line,
/// where both rapids cross nothing cut before that point; the part after it keeps the program
/// it was planned with. Segments due in the same gap are chained in the order placed.
/// </summary>
internal static class CutOffInsertion
{
    /// <summary>
    /// The non-separating cut-offs that must precede some part, by source ordinal. A separator
    /// or a cut-off through open scrap precedes nothing and is planned like a part.
    /// </summary>
    internal static int[] Attached(CuttingPlanSnapshot snapshot)
    {
        var count = snapshot.Placements.Count;
        var attached = new SortedSet<int>();
        for (var part = 0; part < count; part++)
        {
            if (snapshot.Placements[part].IsCutOff)
                continue;
            foreach (var prerequisite in snapshot.Dependencies.PrerequisitesOf(part))
                if (snapshot.Placements[prerequisite] is { IsCutOff: true, SeparatesSheet: false })
                    attached.Add(prerequisite);
        }
        return attached.ToArray();
    }

    /// <summary>
    /// The planned order with every attached cut-off inserted, or null when one fits in no gap
    /// before its first part. <paramref name="countExpansion"/> is charged once per gap tried.
    /// </summary>
    internal static IReadOnlyList<FixedProgramPlacement> Insert(CuttingPlanSnapshot snapshot,
        IReadOnlyList<FixedProgramPlacement> planned, IReadOnlyList<int> cutOffs,
        Action<FixedProgramPlacement> countExpansion, CancellationToken token)
    {
        if (cutOffs.Count == 0)
            return planned;
        var count = planned.Count;
        // The contours finished before each gap, and where the tool is when it reaches the gap.
        var states = new ReleasedContourState[count + 1];
        var arrivals = new Vector[count + 1];
        states[0] = new ReleasedContourState(reportMissingLeadIns: false);
        arrivals[0] = snapshot.StartPoint;
        for (var gap = 0; gap < count; gap++)
        {
            token.ThrowIfCancellationRequested();
            var placement = planned[gap];
            states[gap + 1] = states[gap].Copy();
            states[gap + 1].Check(placement.Execution, arrivals[gap], placement.SourceOrdinal + 1,
                placement.IsCutOff, placement.FixedMotions(placement.Execution), token);
            arrivals[gap + 1] = placement.Execution.DeparturePoint;
        }
        var position = new Dictionary<int, int>();
        for (var index = 0; index < count; index++)
            position[planned[index].SourceOrdinal] = index;
        var latest = cutOffs.ToDictionary(cutOff => cutOff, cutOff => Enumerable.Range(0, snapshot.Placements.Count)
            .Where(part => position.ContainsKey(part) && snapshot.Dependencies.PrerequisitesOf(part).Contains(cutOff))
            .Select(part => position[part]).DefaultIfEmpty(count).Min());

        var chains = new List<FixedProgramPlacement>[count + 1];
        // Latest gap first: a segment due late never takes a gap an earlier one needs.
        foreach (var cutOff in cutOffs.OrderByDescending(c => latest[c]).ThenBy(c => c))
        {
            var source = snapshot.Placements[cutOff];
            var placed = false;
            for (var gap = latest[cutOff]; gap >= 0 && !placed; gap--)
            {
                token.ThrowIfCancellationRequested();
                var chain = chains[gap];
                var arrival = chain is { Count: > 0 } ? chain[^1].Execution.DeparturePoint : arrivals[gap];
                // A head-locked far-edge segment may be cut from either end: the nearer first.
                var variants = source.Reversed == null ? [source]
                    : new[] { source, source.Reversed }.OrderBy(v => v.Execution.RapidDistanceFrom(arrival)).ToArray();
                foreach (var cut in variants)
                {
                    countExpansion(source);
                    if (states[gap].Copy().Check(cut.Execution, arrival, cut.SourceOrdinal + 1, true, null, token).Strict.Count != 0)
                        continue;
                    if (gap < count)
                    {
                        var next = planned[gap];
                        var findings = states[gap].Copy().Check(next.Execution, cut.Execution.DeparturePoint,
                            next.SourceOrdinal + 1, next.IsCutOff, next.FixedMotions(next.Execution), token).Strict;
                        if (findings.Count != 0)
                            continue;
                    }
                    (chains[gap] ??= []).Add(cut);
                    placed = true;
                    break;
                }
            }
            if (!placed)
                return null;
        }
        var order = new List<FixedProgramPlacement>(snapshot.Placements.Count);
        for (var gap = 0; gap <= count; gap++)
        {
            if (chains[gap] != null)
                order.AddRange(chains[gap]);
            if (gap < count)
                order.Add(planned[gap]);
        }
        return order;
    }
}
