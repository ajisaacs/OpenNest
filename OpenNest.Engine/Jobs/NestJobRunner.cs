using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest;

/// <summary>
/// Single-stock physical-sheet allocation. Candidate accounting is validated before commit;
/// full geometry, clearance, and rotation-policy validation is not implemented yet.
/// </summary>
public sealed class NestJobRunner : INestingEngine
{
    private readonly Func<string, IPlateNester> plateNesterFactory;

    /// <summary>Runner-local strategy resolution. A factory must reject unknown keys or return null.</summary>
    public NestJobRunner(Func<string, IPlateNester> plateNesterFactory)
    {
        ArgumentNullException.ThrowIfNull(plateNesterFactory);
        this.plateNesterFactory = plateNesterFactory;
    }

    public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();
        NestJobValidator.Validate(job);
        var plates = new List<NestJobPlateResult>();
        var remaining = job.Parts.ToDictionary(p => p.Id, p => p.Quantity, StringComparer.Ordinal);
        var placed = job.Parts.ToDictionary(p => p.Id, _ => 0, StringComparer.Ordinal);
        var reason = NestJobStopReason.Completed;
        if (job.Parts.Count != 0)
        {
            var nester = plateNesterFactory(job.Options.PlacementStrategy) ??
                throw new NotSupportedException($"Unknown placement strategy: {job.Options.PlacementStrategy}.");
            var stock = job.Plates.SingleOrDefault();
            while (remaining.Values.Any(count => count > 0))
            {
                token.ThrowIfCancellationRequested();
                if (stock == null || stock.Quantity <= plates.Count)
                {
                    reason = NestJobStopReason.StockExhausted;
                    break;
                }
                if (job.Options.MaxPlates <= plates.Count)
                {
                    reason = NestJobStopReason.PlateLimitReached;
                    break;
                }
                var request = new PlatePlacementRequest(stock, job.Parts.Where(p => remaining[p.Id] > 0)
                    .Select(p => new NestJobPart(p.Id, p.Geometry, remaining[p.Id], p.Priority, p.Rotation)));
                progress?.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, stock.Id,
                    plates.Count, plates.Count, placed.Values.Sum()));
                token.ThrowIfCancellationRequested();
                // Do not forward legacy/candidate progress as committed production.
                var candidate = nester.Place(request, token: token);
                token.ThrowIfCancellationRequested();
                NestJobValidator.ValidateCandidate(candidate, remaining);
                if (candidate.Placements.Count == 0)
                {
                    reason = NestJobStopReason.NoPlacementFound;
                    break;
                }
                var committed = new List<NestJobPlacement>();
                foreach (var pose in candidate.Placements)
                {
                    committed.Add(pose with { InstanceIndex = placed[pose.PartId]++ });
                    remaining[pose.PartId]--;
                }
                plates.Add(new NestJobPlateResult(plates.Count, stock, committed));
                progress?.Report(new NestJobProgress(NestJobStage.PlateCommitted, stock.Id,
                    plates.Count - 1, plates.Count, placed.Values.Sum()));
            }
        }
        token.ThrowIfCancellationRequested();
        return new NestJobResult(reason == NestJobStopReason.Completed ? NestJobStatus.Complete : NestJobStatus.Incomplete,
            reason, plates, job.Parts.Select(p => new PartFulfillment(p.Id, p.Quantity, placed[p.Id], remaining[p.Id])),
            job.Plates.Select(stock => new StockUsage(stock.Id, plates.Count(p => p.StockId == stock.Id),
                stock.Quantity - plates.Count(p => p.StockId == stock.Id))));
    }
}
