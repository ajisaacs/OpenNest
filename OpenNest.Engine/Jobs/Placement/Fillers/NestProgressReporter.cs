using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpenNest.Engine.Jobs.Placement.Fillers;

internal static class NestProgressReporter
{
    internal static void Report(IProgress<NestProgress> progress, ProgressReport report)
    {
        if (progress == null || report.Parts == null || report.Parts.Count == 0)
            return;

        var clonedParts = new List<Part>(report.Parts.Count);
        foreach (var part in report.Parts)
            clonedParts.Add((Part)part.Clone());

        Debug.WriteLine(
            $"[Progress] Phase={report.Phase}, Plate={report.PlateNumber}, "
                + $"Parts={clonedParts.Count} | {report.Description}"
        );

        progress.Report(
            new NestProgress
            {
                Phase = report.Phase,
                PlateNumber = report.PlateNumber,
                BestParts = clonedParts,
                Description = report.Description,
                ActiveWorkArea = report.WorkArea,
                IsOverallBest = report.IsOverallBest,
            }
        );
    }
}
