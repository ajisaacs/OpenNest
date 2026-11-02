using System;
using System.Threading;

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

    public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null, CancellationToken token = default)
    {
        var result = inner.Solve(job, progress, token);
        if (result != null)
        {
            lock (Transcript)
                Transcript.Add(job, result);
        }
        return result;
    }
}
