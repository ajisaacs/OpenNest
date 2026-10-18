#nullable enable
using System.Linq;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines;

/// <summary>Ranks whole-job candidate results: a valid layout before an invalid one, then the
/// fewest unplaced parts, then the lowest <see cref="NestJobCost"/>; remaining ties keep
/// candidate order.</summary>
internal sealed record CandidateScore(NestJobResult Result, bool Valid, int Unplaced, double Cost, int Order, bool Priced)
{
    public static CandidateScore Of(NestJob job, NestJobResult result, int order)
    {
        var valid = NestLayoutCheck.Violations(job, result).Count == 0;
        var placed = result.Plates.Sum(p => p.Placements.Count);
        var unplaced = System.Math.Max(0, job.Parts.Sum(p => p.Quantity) - placed);
        // Cost needs every placement to name a known part, which only a valid result guarantees.
        var cost = valid ? NestJobCost.Evaluate(job, result) : double.PositiveInfinity;
        return new CandidateScore(result, valid, unplaced, cost, order, NestJobCost.UsesExplicitCosts(job));
    }

    public bool IsBetterThan(CandidateScore other)
    {
        if (Valid != other.Valid)
            return Valid;
        if (Unplaced != other.Unplaced)
            return Unplaced < other.Unplaced;
        if (Priced && Cost != other.Cost)
            return Cost < other.Cost;
        var scale = System.Math.Max(1, System.Math.Max(System.Math.Abs(Cost), System.Math.Abs(other.Cost)));
        if (double.IsFinite(Cost) && double.IsFinite(other.Cost) && System.Math.Abs(Cost - other.Cost) > 1e-9 * scale)
            return Cost < other.Cost;
        return Order < other.Order;
    }
}
