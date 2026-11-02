using System;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs.Remote;

/// <summary>
/// Answers each solve with the next recorded result of a <see cref="NestSolveTranscript"/>,
/// after checking it is asked the same job the recording engine was. A different question
/// (the drawings no longer bundle or recover the same way) or one too many fails the replay
/// with <see cref="NestReplayMismatchException"/>; nothing is ever solved here.
/// </summary>
internal sealed class ReplayNestingEngine : INestingEngine
{
    private readonly NestSolveTranscript transcript;
    private int next;

    public ReplayNestingEngine(NestSolveTranscript transcript)
    {
        this.transcript = transcript ?? throw new ArgumentNullException(nameof(transcript));
    }

    /// <summary>True once every recorded solve has been replayed.</summary>
    public bool Finished => next == transcript.Solves.Count;

    public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();
        if (next >= transcript.Solves.Count)
            throw new NestReplayMismatchException("The nest needed more engine runs here than it did on the server.");
        var solve = transcript.Solves[next++];
        if (solve.Request == null
            || !solve.Request.SequenceEqual(NestSolveTranscript.Fingerprint(job), StringComparer.Ordinal))
            throw new NestReplayMismatchException("The nest asked the engine a different question here than it did on the server.");
        return solve.Result.ToResult(job);
    }
}

/// <summary>The server's result cannot be replayed against the drawings and stock here.</summary>
public sealed class NestReplayMismatchException : InvalidOperationException
{
    public NestReplayMismatchException(string message) : base(message) { }
}
