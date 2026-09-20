using System;

namespace OpenNest;

/// <summary>
/// Bridges legacy <see cref="IProgress{NestProgress}"/> reporting into job progress while a candidate
/// trial is being evaluated. Used by both the legacy adapter and the migrated built-in nesters so the
/// stage/context mapping has one implementation.
/// </summary>
internal static class CandidateProgressBridge
{
    internal static IProgress<NestProgress> Create(
        IProgress<NestJobProgress> progress,
        string stockId
    )
    {
        if (progress == null)
            return null;
        return new LegacyToJob(progress, stockId);
    }

    private sealed class LegacyToJob(IProgress<NestJobProgress> progress, string stockId)
        : IProgress<NestProgress>
    {
        public void Report(NestProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            progress.Report(
                new NestJobProgress(NestJobStage.EvaluatingCandidate, stockId, -1, 0, 0, value)
            );
        }
    }
}
