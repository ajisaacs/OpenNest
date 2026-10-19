#nullable enable
using System;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.NestingEngines;

/// <summary>Forwards a candidate run's progress except plate commits, which belong to the result
/// finally chosen; <see cref="ReportCommits"/> reports those once it is.</summary>
internal sealed class CandidateProgress(IProgress<NestJobProgress> inner) : IProgress<NestJobProgress>
{
    public void Report(NestJobProgress value)
    {
        if (value != null && value.Stage != NestJobStage.PlateCommitted)
            inner.Report(value);
    }

    /// <summary>Reports the plate commits of <paramref name="result"/> in order. The token is
    /// observed before each commit and after the last, as while building a result.</summary>
    public static void ReportCommits(NestJobResult result, IProgress<NestJobProgress>? progress,
        CancellationToken token = default)
    {
        if (progress == null)
            return;
        var parts = 0;
        for (var i = 0; i < result.Plates.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var plate = result.Plates[i];
            parts += plate.Placements.Count;
            progress.Report(new NestJobProgress(NestJobStage.PlateCommitted, plate.StockId, plate.PlateIndex, i + 1, parts));
        }
        token.ThrowIfCancellationRequested();
    }

    /// <summary>Reports a whole-job plan as <see cref="NestJobStage.PlanImproved"/>: a proposal
    /// with no committed counts. Reporting never changes the search.</summary>
    public static void ReportPlan(NestJobResult plan, IProgress<NestJobProgress>? progress) =>
        progress?.Report(new NestJobProgress(NestJobStage.PlanImproved, string.Empty, -1, 0, 0, Plan: plan));

    /// <summary>True when both results place the same parts at the same poses on the same stock,
    /// sheet by sheet.</summary>
    public static bool SameLayout(NestJobResult a, NestJobResult b) =>
        ReferenceEquals(a, b)
        || (a.Plates.Count == b.Plates.Count
            && a.Plates.Zip(b.Plates).All(p => p.First.StockId == p.Second.StockId
                && p.First.Placements.SequenceEqual(p.Second.Placements)));
}
