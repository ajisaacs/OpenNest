#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Engine.NestingEngines.Rectangles;

namespace OpenNest.Engine.NestingEngines.Default;

/// <summary>
/// The engine used when the caller names none. It runs each candidate engine on the whole job,
/// checks every result with <see cref="NestLayoutCheck"/> and returns the best one: a valid
/// layout before an invalid one, then the fewest unplaced parts, then the lowest
/// <see cref="NestJobCost"/>; remaining ties keep candidate order.
///
/// Candidates are Irregular, then Rectangles. Neither wins every job: Rectangles packs each part
/// as its box, which is often best for plain plates and is a cheap, valid fallback for the rest.
/// A candidate that throws or returns no result is skipped; when every candidate throws, the
/// first exception is rethrown. Cancellation stops the search. Candidates' plate commits are not forwarded; the chosen
/// result's commits are reported once it is selected.
///
/// Deterministic: candidates run one after another and the choice uses no clock.
/// </summary>
public sealed class DefaultNestingEngine : INestingEngine
{
    private readonly IReadOnlyList<Func<INestingEngine>> candidates;

    public DefaultNestingEngine()
        : this(new Func<INestingEngine>[] { () => new IrregularNestingEngine(), () => new RectanglesNestingEngine() })
    {
    }

    /// <summary>Test seam: candidate engines in tie-break order.</summary>
    internal DefaultNestingEngine(IReadOnlyList<Func<INestingEngine>> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
            throw new ArgumentException("At least one candidate engine is required.", nameof(candidates));
        this.candidates = candidates;
    }

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();
        var forward = progress == null ? null : new CandidateProgress(progress);
        Scored? best = null;
        ExceptionDispatchInfo? firstFailure = null;

        for (var order = 0; order < candidates.Count; order++)
        {
            token.ThrowIfCancellationRequested();
            NestJobResult? result;
            try
            {
                result = candidates[order]().Solve(job, forward, token);
            }
            catch (Exception ex)
            {
                // A cancelled solve is rethrown by the token checks around each candidate.
                firstFailure ??= ExceptionDispatchInfo.Capture(ex);
                continue;
            }
            if (result == null)
                continue;

            var scored = Scored.Of(job, result, order);
            if (best == null || scored.IsBetterThan(best))
                best = scored;
        }

        token.ThrowIfCancellationRequested();
        if (best == null)
        {
            firstFailure?.Throw();
            throw new InvalidOperationException("No candidate engine returned a result.");
        }

        ReportCommits(best.Result, progress);
        return best.Result;
    }

    private static void ReportCommits(NestJobResult result, IProgress<NestJobProgress>? progress)
    {
        if (progress == null)
            return;
        var parts = 0;
        for (var i = 0; i < result.Plates.Count; i++)
        {
            var plate = result.Plates[i];
            parts += plate.Placements.Count;
            progress.Report(new NestJobProgress(NestJobStage.PlateCommitted, plate.StockId, plate.PlateIndex, i + 1, parts));
        }
    }

    private sealed record Scored(NestJobResult Result, bool Valid, int Unplaced, double Cost, int Order)
    {
        public static Scored Of(NestJob job, NestJobResult result, int order)
        {
            var valid = NestLayoutCheck.Violations(job, result).Count == 0;
            var placed = result.Plates.Sum(p => p.Placements.Count);
            var unplaced = System.Math.Max(0, job.Parts.Sum(p => p.Quantity) - placed);
            // Cost needs every placement to name a known part, which only a valid result guarantees.
            var cost = valid ? NestJobCost.Evaluate(job, result) : double.PositiveInfinity;
            return new Scored(result, valid, unplaced, cost, order);
        }

        public bool IsBetterThan(Scored other)
        {
            if (Valid != other.Valid)
                return Valid;
            if (Unplaced != other.Unplaced)
                return Unplaced < other.Unplaced;
            var scale = System.Math.Max(1, System.Math.Max(System.Math.Abs(Cost), System.Math.Abs(other.Cost)));
            if (double.IsFinite(Cost) && double.IsFinite(other.Cost) && System.Math.Abs(Cost - other.Cost) > 1e-9 * scale)
                return Cost < other.Cost;
            return Order < other.Order;
        }
    }

    /// <summary>Forwards a candidate's progress except plate commits, which belong to the chosen result.</summary>
    private sealed class CandidateProgress(IProgress<NestJobProgress> inner) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value)
        {
            if (value != null && value.Stage != NestJobStage.PlateCommitted)
                inner.Report(value);
        }
    }
}
