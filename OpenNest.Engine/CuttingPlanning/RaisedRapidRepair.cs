using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Tries to take head raises out of a safest route by local changes to its part order. The
/// greedy walk never goes back to a part it has cut, so a rapid that passes over one is raised
/// even where cutting two parts the other way round would keep the head down. For each rapid
/// that crosses parts already cut, it tries cutting the part the rapid goes to before the
/// earliest part crossed, and each crossed part just before or just after it, planning only the
/// parts whose place changed (and the ones beside them) again. A change is kept only when the
/// replayed route breaks no more checks and raises the head fewer times, so it can never make a
/// route worse. A kept part order is never changed.
/// </summary>
internal static class RaisedRapidRepair
{
    /// <summary>Changed orders replayed per plate at most: each costs a full replay.</summary>
    internal const int MaximumTrials = 200;

    /// <param name="order">The unmarked route <paramref name="result"/> was replayed from.</param>
    /// <param name="replay">Marks and replays a route; null when it is refused.</param>
    internal static CuttingPlanResult Repair(CuttingPlanSnapshot snapshot, IReadOnlyList<FixedProgramPlacement> order,
        CuttingPlanResult result, Func<IReadOnlyList<FixedProgramPlacement>, CuttingPlanResult> replay,
        CancellationToken token)
    {
        if (snapshot.PreservePartOrder || result.HeadRaises == 0)
            return result;
        var resequencer = new JointCuttingPlanSearch.Resequencer(snapshot, token);
        var tried = new HashSet<string>();
        var trials = 0;
        while (result.HeadRaises > 0)
        {
            var sequence = order.Select(p => p.SourceOrdinal).ToArray();
            IReadOnlyList<FixedProgramPlacement> repaired = null;
            CuttingPlanResult repairedResult = null;
            foreach (var (index, crossed) in RaisedRapids.Crossed(snapshot, order, token))
            {
                foreach (var candidate in Candidates(sequence, index, crossed))
                {
                    if (trials == MaximumTrials || resequencer.Exhausted)
                        return result;
                    if (!tried.Add(string.Join(",", candidate)) || snapshot.Dependencies.FirstViolation(candidate) != null)
                        continue;
                    trials++;
                    var (from, to) = Window(sequence, candidate);
                    if (resequencer.Cut(order, candidate, from, to) is not { } changed
                        || replay(changed) is not { } replayed || !Better(replayed, result))
                        continue;
                    (repaired, repairedResult) = (changed, replayed);
                    break;
                }
                if (repaired != null)
                    break;
            }
            if (repaired == null)
                return result;
            (order, result) = (repaired, repairedResult);
        }
        return result;
    }

    // The orders worth trying for the part at 'index', whose rapids crossed the parts 'crossed'
    // (all cut before it): that part before the earliest of them, then each of them, latest
    // first, cut just before it and just after it.
    internal static IEnumerable<int[]> Candidates(int[] sequence, int index, int[] crossed)
    {
        var positions = crossed.Select(part => Array.IndexOf(sequence, part))
            .Where(position => position >= 0 && position < index).OrderDescending().ToArray();
        if (positions.Length == 0)
            yield break;
        yield return Move(sequence, index, positions[^1]);
        foreach (var position in positions)
        {
            // Removing the crossed part moves the target one place earlier.
            if (position != index - 1)
                yield return Move(sequence, position, index - 1);
            yield return Move(sequence, position, index);
        }
    }

    // The sequence with the element at 'from' taken out and put back at 'to' (an index after removal).
    private static int[] Move(int[] sequence, int from, int to)
    {
        var moved = sequence.ToList();
        var part = moved[from];
        moved.RemoveAt(from);
        moved.Insert(to, part);
        return [.. moved];
    }

    // The parts to plan again: every one whose place changed, the one before them (it was planned
    // facing a different next part) and the one after them (it is reached from somewhere else).
    private static (int From, int To) Window(int[] sequence, int[] candidate)
    {
        var first = 0;
        while (sequence[first] == candidate[first])
            first++;
        var last = sequence.Length - 1;
        while (sequence[last] == candidate[last])
            last--;
        return (System.Math.Max(0, first - 1), System.Math.Min(sequence.Length - 1, last + 1));
    }

    // Breaks no more checks (most severe first) and raises the head fewer times.
    private static bool Better(CuttingPlanResult candidate, CuttingPlanResult known)
    {
        var severity = (candidate.SafestRoute ?? default).CompareTo(known.SafestRoute ?? default);
        return severity < 0 || severity == 0 && candidate.HeadRaises < known.HeadRaises;
    }
}
