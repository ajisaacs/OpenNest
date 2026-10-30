#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Circles;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Engine.NestingEngines.Rectangles;

namespace OpenNest.Engine.NestingEngines.Default;

/// <summary>
/// The engine used when the caller names none. It runs each candidate engine on the whole job,
/// checks every result with <see cref="NestLayoutCheck"/> and returns the best one: a valid
/// layout before an invalid one, then the fewest unplaced parts, then the lowest
/// <see cref="NestJobCost"/>; remaining ties keep candidate order.
///
/// Candidates are Irregular, then Rectangles, then Circles. Neither of the first two wins every
/// job: Rectangles packs each part as its box, which is often best for plain plates and is a
/// cheap, valid fallback for the rest. Circles bids only when every part is round, and otherwise
/// returns everything unplaced at once.
/// A candidate that throws or returns no result is skipped; when every candidate throws, the
/// first exception is rethrown. Cancellation stops the search. Candidates' plate commits are not forwarded; the chosen
/// result's commits are reported once it is selected.
///
/// Candidates' whole-job plans (<see cref="NestJobStage.PlanImproved"/>) are scored the same way
/// and reported only when one beats every plan reported so far, so a later candidate never shows a
/// worse plan after a better one. A candidate result that beats them is reported too, and the
/// chosen result is always the last plan reported.
///
/// Deterministic: candidates run one after another and the choice uses no clock.
/// </summary>
public sealed class DefaultNestingEngine : INestingEngine
{
    private readonly IReadOnlyList<Func<INestingEngine>> candidates;

    public DefaultNestingEngine()
        : this(new Func<INestingEngine>[]
        {
            () => new IrregularNestingEngine(), () => new RectanglesNestingEngine(), () => new CirclesNestingEngine(),
        })
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
        var forward = progress == null ? null : new PlanFilter(job, progress, token);
        CandidateScore? best = null;
        ExceptionDispatchInfo? firstFailure = null;

        for (var order = 0; order < candidates.Count; order++)
        {
            token.ThrowIfCancellationRequested();
            NestJobResult? result;
            if (forward != null)
                forward.Order = order;
            try
            {
                result = candidates[order]().Solve(job, forward, token);
            }
            catch (Exception ex)
            {
                // A canceled solve is rethrown by the token checks around each candidate.
                firstFailure ??= ExceptionDispatchInfo.Capture(ex);
                continue;
            }
            if (result == null)
                continue;

            var scored = CandidateScore.Of(job, result, order);
            if (best == null || scored.IsBetterThan(best))
                best = scored;
            forward?.Offer(scored);
        }

        token.ThrowIfCancellationRequested();
        if (best == null)
        {
            firstFailure?.Throw();
            throw new InvalidOperationException("No candidate engine returned a result.");
        }

        forward?.Finish(best);
        CandidateProgress.ReportCommits(best.Result, progress);
        return best.Result;
    }

    /// <summary>
    /// Forwards candidate progress except plate commits, and replaces candidates' raw plan reports
    /// with Default's own best plan so far by <see cref="CandidateScore"/>.
    /// </summary>
    private sealed class PlanFilter(NestJob job, IProgress<NestJobProgress> inner, CancellationToken token)
        : IProgress<NestJobProgress>
    {
        private CandidateScore? shown;

        /// <summary>Candidate order of the plans now arriving, for score ties.</summary>
        public int Order { get; set; }

        public void Report(NestJobProgress value)
        {
            if (value == null || value.Stage == NestJobStage.PlateCommitted)
                return;
            if (value.Stage != NestJobStage.PlanImproved)
            {
                inner.Report(value);
                return;
            }
            if (value.Plan == null)
                return;
            CandidateScore plan;
            try
            {
                plan = CandidateScore.Of(job, value.Plan, Order);
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                // A plan that cannot be scored is not shown; throwing would fail the candidate.
                return;
            }
            Offer(plan);
        }

        /// <summary>Reports the plan when it beats every plan reported so far.</summary>
        public void Offer(CandidateScore plan)
        {
            if (shown != null && !plan.IsBetterThan(shown))
                return;
            shown = plan;
            CandidateProgress.ReportPlan(plan.Result, inner);
        }

        /// <summary>Makes the chosen result the last plan reported. A candidate's plan can outscore
        /// every returned result (its engine judged a later plan better), so this may follow it.</summary>
        public void Finish(CandidateScore chosen)
        {
            if (shown != null && CandidateProgress.SameLayout(shown.Result, chosen.Result))
                return;
            shown = chosen;
            CandidateProgress.ReportPlan(chosen.Result, inner);
        }
    }
}
