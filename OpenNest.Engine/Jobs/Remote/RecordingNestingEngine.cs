using System;
using System.Threading;
using OpenNest.Engine.Jobs.Cutouts;

namespace OpenNest.Engine.Jobs.Remote;

/// <summary>
/// Passes every solve to another engine and records what it was asked and what it returned
/// in <see cref="Transcript"/>. A solve that throws records nothing. The nest server's worker
/// wraps the selected engine in one, so the client can replay the run (<see cref="RemoteNestSubmission"/>).
/// </summary>
public sealed class RecordingNestingEngine : INestingEngine
{
    private readonly INestingEngine inner;

    public RecordingNestingEngine(INestingEngine inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public NestSolveTranscript Transcript { get; } = new();

    /// <summary>
    /// Also receives each solve's own progress, before the pipeline turns plans into sheets:
    /// the worker's live reports. A bundled cutout trial's commits count proxies, so it passes
    /// only candidates and plans, as the pipeline's own progress does.
    /// </summary>
    public IProgress<NestJobProgress> Progress { get; init; }

    public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null, CancellationToken token = default)
    {
        if (Progress != null)
        {
            var tap = CutoutPipelinePrepass.HasProxies(job) ? new NestPipeline.PreviewProgress(Progress) : Progress;
            progress = progress == null ? tap : new BothProgress(progress, tap);
        }
        var result = inner.Solve(job, progress, token);
        if (result != null)
        {
            lock (Transcript)
                Transcript.Add(job, result);
        }
        return result;
    }

    private sealed class BothProgress(IProgress<NestJobProgress> first, IProgress<NestJobProgress> second)
        : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value)
        {
            first.Report(value);
            second.Report(value);
        }
    }
}
