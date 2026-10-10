using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Jobs;

/// <summary>Ranks independent plate trials: priority fulfillment, sheet cost, placement envelope, then input order.</summary>
public sealed class NestJobCandidateComparer
{
    private readonly IReadOnlyList<NestJobPart> parts;
    private readonly NestJob job;

    public NestJobCandidateComparer(NestJob job) : this(job.Parts) => this.job = job;

    public NestJobCandidateComparer(IReadOnlyList<NestJobPart> parts)
    {
        this.parts = parts ?? throw new ArgumentNullException(nameof(parts));
    }

    /// <summary>Returns positive when the left trial is preferred.</summary>
    public int Compare(
        PlateCandidate left,
        NestPlateStock leftStock,
        int leftIndex,
        PlateCandidate right,
        NestPlateStock rightStock,
        int rightIndex
    )
    {
        var priorities = parts
            .Select(part => part.Priority)
            .Distinct()
            .OrderBy(priority => priority);
        foreach (var priority in priorities)
        {
            var leftCount = Count(left, priority);
            var rightCount = Count(right, priority);
            if (leftCount != rightCount)
                return leftCount.CompareTo(rightCount);
        }

        var area = Score(right, rightStock).CompareTo(Score(left, leftStock));
        if (area != 0)
            return area;

        var envelope = Envelope(right).CompareTo(Envelope(left));
        if (envelope != 0)
            return envelope;

        return rightIndex.CompareTo(leftIndex);
    }

    private int Count(PlateCandidate candidate, int priority) =>
        candidate.Placements.Count(placement =>
            parts.First(part => part.Id == placement.PartId).Priority == priority
        );

    private double Score(PlateCandidate candidate, NestPlateStock stock) =>
        job != null && stock.Cost.HasValue
            ? NestJobCost.NetSheetCost(job, new NestJobPlateResult(0, stock, candidate.Placements))
            : NestJobCost.GrossSheetCost(stock);

    private static double Envelope(PlateCandidate candidate)
    {
        if (candidate.Placements.Count == 0)
            return 0;
        var xs = candidate.Placements.Select(placement => placement.X);
        var ys = candidate.Placements.Select(placement => placement.Y);
        return (xs.Max() - xs.Min()) * (ys.Max() - ys.Min());
    }
}
