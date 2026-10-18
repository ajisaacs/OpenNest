#nullable enable
using System;
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

    public static void ReportCommits(NestJobResult result, IProgress<NestJobProgress>? progress)
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
}
