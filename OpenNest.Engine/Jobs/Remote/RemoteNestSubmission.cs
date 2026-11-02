using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs.Remote;

/// <summary>
/// The client's side of a nest solved elsewhere. Created when the job is sent, it fingerprints
/// each item's geometry; <see cref="Complete"/> then replays the pipeline on this machine with
/// the engine answers recorded remotely (<see cref="NestSolveTranscript"/>), so the proposal is
/// validated against, and bound to, the caller's own drawings exactly as a local run would be.
/// A job reviewed in a later session is rebuilt from what was sent (<see cref="Reopen"/>).
/// Nothing is committed: the caller reviews and keeps the result as it would a local one.
/// </summary>
public sealed class RemoteNestSubmission
{
    private readonly List<(NestItem Item, string Fingerprint)> sent = new();

    public RemoteNestSubmission(NestPipelineRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        ArgumentNullException.ThrowIfNull(request.Items);
        foreach (var item in request.Items)
        {
            if (item != null && item.Quantity > 0)
                sent.Add((item, Fingerprint(item.Drawing)));
        }
    }

    private RemoteNestSubmission(NestPipelineRequest request, IEnumerable<(NestItem Item, string Fingerprint)> sent)
    {
        Request = request;
        this.sent.AddRange(sent);
    }

    public NestPipelineRequest Request { get; }

    /// <summary>
    /// The geometry fingerprint the freshness check compares (<see cref="PartGeometrySnapshot.Fingerprint"/>);
    /// empty for a drawing without supported geometry, which never matches.
    /// </summary>
    public static string Fingerprint(Drawing drawing)
    {
        if (drawing?.Program == null)
            return "";
        try
        {
            return PartGeometrySnapshot.FromProgram(drawing.Program).Fingerprint();
        }
        catch (NotSupportedException)
        {
            return "";
        }
    }

    /// <summary>
    /// Rebuilds a job sent earlier (its <c>job.json</c>) against <paramref name="drawings"/>, the
    /// drawings of the nest it is reviewed in: each sent drawing is the one with its name, the one
    /// whose geometry is unchanged when several share it. <see cref="Complete"/> then refuses the
    /// result if any of them changed since it was sent, as it does in the session that sent it.
    /// Throws <see cref="NestReplayMismatchException"/> when a drawing is missing or the job
    /// predates the fingerprints.
    /// </summary>
    public static RemoteNestSubmission Reopen(NestJobDocument document, IEnumerable<Drawing> drawings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(drawings);
        var names = document.DrawingNames ?? new List<string>();
        var fingerprints = document.DrawingFingerprints;
        if (fingerprints == null || fingerprints.Count != names.Count)
            throw new NestReplayMismatchException(
                "This job was sent by an older OpenNest, so it can only be reviewed in the session that sent it.");

        var available = drawings.Where(d => d != null).ToList();
        var chosen = new List<Drawing>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var named = available.Where(d => string.Equals(d.Name ?? "", names[i], StringComparison.Ordinal)).ToList();
            if (named.Count == 0)
                throw new NestReplayMismatchException($"The nest has no drawing named '{names[i]}'.");
            chosen.Add(named.FirstOrDefault(d => fingerprints[i].Length > 0 && Fingerprint(d) == fingerprints[i]) ?? named[0]);
        }

        NestPipelineRequest request;
        try
        {
            request = document.ToRequest(chosen);
        }
        catch (FormatException ex)
        {
            throw new NestReplayMismatchException($"The job sent to the server cannot be read here: {ex.Message}");
        }
        var items = document.Items ?? new List<NestJobDocument.ItemJson>();
        var sent = new List<(NestItem, string)>();
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (item != null && item.Quantity > 0)
                sent.Add((item, fingerprints[items[i].Drawing]));
        }
        return new RemoteNestSubmission(request, sent);
    }

    /// <summary>
    /// Replays the remote run. Throws <see cref="NestReplayMismatchException"/> when a drawing
    /// changed after the job was sent or the run does not replay here; the caller should run
    /// the nest again rather than keep anything.
    /// </summary>
    public NestPipelineResult Complete(NestSolveTranscript transcript, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        foreach (var (item, fingerprint) in sent)
        {
            if (fingerprint.Length == 0 || Fingerprint(item.Drawing) != fingerprint)
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
