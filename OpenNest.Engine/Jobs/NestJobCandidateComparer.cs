using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest;

/// <summary>Ranks independent plate trials: priority fulfillment, sheet area, placement envelope, then input order.</summary>
public sealed class NestJobCandidateComparer
{
    private readonly IReadOnlyList<NestJobPart> parts;

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

        var area = Area(rightStock).CompareTo(Area(leftStock));
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

    private static double Area(NestPlateStock stock) => stock.Size.Width * stock.Size.Length;

    private static double Envelope(PlateCandidate candidate)
    {
        if (candidate.Placements.Count == 0)
            return 0;
        var xs = candidate.Placements.Select(placement => placement.X);
        var ys = candidate.Placements.Select(placement => placement.Y);
        return (xs.Max() - xs.Min()) * (ys.Max() - ys.Min());
    }
}
