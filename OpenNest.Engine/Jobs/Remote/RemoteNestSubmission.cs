using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Jobs.Remote;

/// <summary>
/// The client's side of a nest solved elsewhere. Created when the job is sent, it snapshots
/// each item's geometry; <see cref="Complete"/> then replays the pipeline on this machine with
/// the engine answers recorded remotely (<see cref="NestSolveTranscript"/>), so the proposal is
/// validated against, and bound to, the caller's own drawings exactly as a local run would be.
/// Nothing is committed: the caller reviews and keeps the result as it would a local one.
/// </summary>
public sealed class RemoteNestSubmission
{
    private readonly List<(NestItem Item, PartGeometrySnapshot Geometry)> sent = new();

    public RemoteNestSubmission(NestPipelineRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        ArgumentNullException.ThrowIfNull(request.Items);
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (item != null && item.Quantity > 0)
                sent.Add((item, DrawingJobMapper.FromItem($"part-{i}", item).Geometry));
        }
    }

    public NestPipelineRequest Request { get; }

    /// <summary>
    /// Replays the remote run. Throws <see cref="NestReplayMismatchException"/> when a drawing
    /// changed after the job was sent or the run does not replay here; the caller should run
    /// the nest again rather than keep anything.
    /// </summary>
    public NestPipelineResult Complete(NestSolveTranscript transcript, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        foreach (var (item, geometry) in sent)
        {
            if (item.Drawing?.Program == null
                || !NestPipelineDrawingFreshness.Same(geometry, PartGeometrySnapshot.FromProgram(item.Drawing.Program)))
                throw new NestReplayMismatchException(
                    $"Drawing '{item.Drawing?.Name}' changed after the nest was sent to the server.");
        }

        var replay = new ReplayNestingEngine(transcript);
        var result = NestPipeline.Run(replay, Request.EngineName, Request, progress: null, token);
        if (!replay.Finished)
            throw new NestReplayMismatchException("The nest needed fewer engine runs here than it did on the server.");
        return result;
    }
}
