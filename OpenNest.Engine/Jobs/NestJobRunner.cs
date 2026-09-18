using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest;

/// <summary>
/// Physical-sheet allocation. Every available stock entry is tried independently and only the selected
/// candidate changes demand or inventory accounting.
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
        var remaining = job.Parts.ToDictionary(part => part.Id, part => part.Quantity, StringComparer.Ordinal);
        var placed = job.Parts.ToDictionary(part => part.Id, _ => 0, StringComparer.Ordinal);
        var parts = job.Parts.ToDictionary(part => part.Id, StringComparer.Ordinal);
        var used = job.Plates.ToDictionary(stock => stock.Id, _ => 0, StringComparer.Ordinal);
        var comparer = new NestJobCandidateComparer(job.Parts);
        var nester = job.Parts.Count == 0 ? null : plateNesterFactory(job.Options.PlacementStrategy) ??
            throw new NotSupportedException($"Unknown placement strategy: {job.Options.PlacementStrategy}.");
        var reason = NestJobStopReason.Completed;
        while (remaining.Values.Any(count => count > 0))
        {
            token.ThrowIfCancellationRequested();
            if (job.Options.MaxPlates <= plates.Count)
            {
                reason = NestJobStopReason.PlateLimitReached;
                break;
            }

            CandidateTrial winner = null;
            var hasAvailableStock = false;
            for (var index = 0; index < job.Plates.Count; index++)
            {
                var stock = job.Plates[index];
                if (stock.Quantity is int quantity && used[stock.Id] >= quantity) continue;
                hasAvailableStock = true;
                var request = new PlatePlacementRequest(stock, job.Parts.Where(part => remaining[part.Id] > 0)
                    .Select(part => new NestJobPart(part.Id, part.Geometry, remaining[part.Id], part.Priority, part.Rotation)));
                progress?.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, stock.Id,
                    plates.Count, plates.Count, placed.Values.Sum()));
                token.ThrowIfCancellationRequested();
                var candidateProgress = progress == null ? null : new CandidateProgress(progress, stock.Id,
                    plates.Count, plates.Count, placed.Values.Sum());
                var candidate = nester.Place(request, candidateProgress, token);
                token.ThrowIfCancellationRequested();
                NestJobValidator.ValidateCandidate(candidate, stock, remaining, parts);
                var trial = new CandidateTrial(candidate, stock, index);
                if (winner == null || comparer.Compare(trial.Candidate, trial.Stock, trial.StockIndex,
                    winner.Candidate, winner.Stock, winner.StockIndex) > 0)
                    winner = trial;
            }

            if (!hasAvailableStock)
            {
                reason = NestJobStopReason.StockExhausted;
                break;
            }
            if (winner.Candidate.Placements.Count == 0)
            {
                reason = NestJobStopReason.NoPlacementFound;
                break;
            }

            var committed = new List<NestJobPlacement>();
            foreach (var pose in winner.Candidate.Placements)
            {
                committed.Add(pose with { InstanceIndex = placed[pose.PartId]++ });
                remaining[pose.PartId]--;
            }
            used[winner.Stock.Id]++;
            plates.Add(new NestJobPlateResult(plates.Count, winner.Stock, committed));
            progress?.Report(new NestJobProgress(NestJobStage.PlateCommitted, winner.Stock.Id,
                plates.Count - 1, plates.Count, placed.Values.Sum()));
        }

        token.ThrowIfCancellationRequested();
        return new NestJobResult(reason == NestJobStopReason.Completed ? NestJobStatus.Complete : NestJobStatus.Incomplete,
            reason, plates, job.Parts.Select(part => new PartFulfillment(part.Id, part.Quantity, placed[part.Id], remaining[part.Id])),
            job.Plates.Select(stock => new StockUsage(stock.Id, used[stock.Id],
                stock.Quantity is int quantity ? quantity - used[stock.Id] : null)));
    }

    private sealed record CandidateTrial(PlateCandidate Candidate, NestPlateStock Stock, int StockIndex);

    private sealed class CandidateProgress(IProgress<NestJobProgress> progress, string stockId, int plateIndex,
        int committedPlates, int committedParts) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            progress.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, stockId, plateIndex,
                committedPlates, committedParts, value.LegacyProgress));
        }
    }
}
