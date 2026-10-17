using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Cutouts;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// One automatic-nesting request: the named engine, the caller's items (drawing, quantity,
/// priority, rotation), the stock it may use, and optional job options. Items with a
/// nonpositive quantity are skipped.
/// </summary>
public sealed record NestPipelineRequest(
    string EngineName,
    IReadOnlyList<NestItem> Items,
    IReadOnlyList<NestPlateStock> Stock,
    NestJobOptions Options = null
);

/// <summary>A proposed layout: new parts bound to caller drawings. PlateIndex is the first
/// raw physical occurrence; Quantity is the total physical copies, including the first.</summary>
public sealed record ProposedPlate(int PlateIndex, NestPlateStock Stock, IReadOnlyList<Part> Parts)
{
    private int quantity = 1;
    public int Quantity
    {
        get => quantity;
        init => quantity = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(Quantity));
    }
}

/// <summary>
/// The engine's raw result, its independent validation, and the proposed plates. Nothing
/// has been committed; the caller decides what to do with an invalid result.
/// </summary>
public sealed class NestPipelineResult
{
    internal NestPipelineResult(
        string engineName,
        NestJob job,
        NestJobResult raw,
        IReadOnlyList<ProposedPlate> plates,
        IReadOnlyList<string> violations,
        bool canKeep,
        TimeSpan solveTime,
        TimeSpan validationTime,
        IReadOnlyDictionary<string, Drawing> drawingsByPartId,
        IReadOnlyList<NestUnplacedPart> unplaced
    )
    {
        EngineName = engineName;
        Job = job;
        Raw = raw;
        Plates = plates;
        Violations = violations;
        CanKeep = canKeep;
        SolveTime = solveTime;
        ValidationTime = validationTime;
        DrawingsByPartId = drawingsByPartId;
        Unplaced = unplaced;
    }

    public string EngineName { get; }
    public NestJob Job { get; }
    public NestJobResult Raw { get; }
    public IReadOnlyList<ProposedPlate> Plates { get; }
    public IReadOnlyList<string> Violations { get; }
    public bool IsValid => Violations.Count == 0;
    /// <summary>True when every placement can be represented, even if layout rules fail.
    /// False for unknown/null requirement IDs or nonfinite poses; Plates is then empty.</summary>
    public bool CanKeep { get; }
    public NestJobStatus Status => Raw.Status;
    public NestJobStopReason StopReason => Raw.StopReason;
    public TimeSpan SolveTime { get; }
    public TimeSpan ValidationTime { get; }
    internal IReadOnlyDictionary<string, Drawing> DrawingsByPartId { get; }

    /// <summary>Every requested copy this proposal leaves unplaced, by requirement and reason,
    /// counted from the returned placements. Empty when the proposal places every copy; when it
    /// cannot be kept, all demand is listed as <see cref="NestUnplacedReason.ProposalNotKeepable"/>.</summary>
    public IReadOnlyList<NestUnplacedPart> Unplaced { get; }

    /// <summary>Why a bundled cutout trial was refused before this ordinary solve replaced it;
    /// empty when no trial ran or the trial was not refused. Never part of <see cref="Violations"/>:
    /// the refused trial has no bindable plates.</summary>
    public IReadOnlyList<string> RefusedCutoutTrial { get; private init; } = Array.Empty<string>();

    internal NestPipelineResult WithRefusedCutoutTrial(IReadOnlyList<string> refusals) =>
        new(EngineName, Job, Raw, Plates, Violations, CanKeep, SolveTime, ValidationTime,
            DrawingsByPartId, Unplaced)
        { RefusedCutoutTrial = refusals };
}

/// <summary>
/// The single automatic-nesting path shared by every front end: build the job, solve it
/// with the named engine, validate the result with the benchmark's rules, then bind
/// placements to the caller's drawings. The pipeline never mutates caller items, drawings,
/// or plates, and it does not care which engine is selected.
/// </summary>
public static class NestPipeline
{
    private sealed class PreviewProgress(IProgress<NestJobProgress> inner) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value)
        {
            if (value != null && value.Stage == NestJobStage.EvaluatingCandidate)
                inner.Report(value with { CommittedPlates = 0, CommittedParts = 0 });
        }
    }

    /// <summary>Resolves <see cref="NestPipelineRequest.EngineName"/> through
    /// <see cref="NestingEngineRegistry"/>; unknown names throw <see cref="NotSupportedException"/>.
    /// Engine cancellation propagates unchanged.</summary>
    public static NestPipelineResult Run(
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var engine = NestingEngineRegistry.Create(request.EngineName);
        return Run(engine, request.EngineName, request, progress, token);
    }

    /// <summary>Runs a caller-supplied engine instance through the same validation and binding.
    /// Parts that fit other parts' enclosed cutouts are first bundled into their frames
    /// (engine-agnostic); see <see cref="CutoutPipelinePrepass"/>. The engine then solves once.
    /// A bundled trial that fails physical validation, or leaves demand unplaced, is compared
    /// with an ordinary solve of the same job and the better valid proposal is returned.</summary>
    public static NestPipelineResult Run(
        INestingEngine engine,
        string engineName,
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    ) => Run(engine, engineName, request, progress, token, cutouts: true);

    /// <summary>The ordinary solve alone, with no cutout pre-pass: a characterization baseline
    /// for tests and benchmarks, not a front-end setting.</summary>
    internal static NestPipelineResult RunWithoutCutouts(
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    ) => Run(NestingEngineRegistry.Create(request.EngineName), request.EngineName, request,
        progress, token, cutouts: false);

    /// <summary>The bundled cutout trial alone, without the ordinary-solve comparison, so tests
    /// can exercise its validation boundary. Jobs without bundles run the ordinary solve.</summary>
    internal static NestPipelineResult RunCutoutTrial(
        INestingEngine engine,
        string engineName,
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    )
    {
        var (job, drawingsByPartId) = CreateJob(engine, request, token);
        var prepass = CutoutPipelinePrepass.Prepare(job, token);
        var result = Evaluate(engine, engineName, job, prepass, drawingsByPartId, progress, token);
        return prepass != null && result.CanKeep && result.IsValid
            ? Committed(result, progress, token) : result;
    }

    internal static NestPipelineResult RunCutoutTrial(
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    ) => RunCutoutTrial(NestingEngineRegistry.Create(request.EngineName),
        request.EngineName, request, progress, token);

    private static NestPipelineResult Run(
        INestingEngine engine,
        string engineName,
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress,
        CancellationToken token,
        bool cutouts
    )
    {
        var (job, drawingsByPartId) = CreateJob(engine, request, token);
        var prepass = cutouts ? CutoutPipelinePrepass.Prepare(job, token) : null;
        if (prepass == null)
            return Evaluate(engine, engineName, job, null, drawingsByPartId, progress, token);

        var trial = Evaluate(engine, engineName, job, prepass, drawingsByPartId, progress, token);
        var trialKeepable = trial.CanKeep && trial.IsValid;
        if (trialKeepable && trial.Status == NestJobStatus.Complete)
            return Committed(trial, progress, token);

        // Unplaced frames return their reserved inserts to unplaced demand, and an untrusted
        // composite is never bindable; the ordinary solve is the established baseline.
        var plain = Evaluate(engine, engineName, job, null, drawingsByPartId, progress, token);
        if (!trialKeepable)
            return plain.WithRefusedCutoutTrial(trial.Violations);
        if (plain.CanKeep && plain.IsValid && Placed(plain) > Placed(trial))
            return plain;
        return Committed(trial, progress, token);
    }

    private static (NestJob Job, Dictionary<string, Drawing> Drawings) CreateJob(
        INestingEngine engine,
        NestPipelineRequest request,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Items);
        ArgumentNullException.ThrowIfNull(request.Stock);
        token.ThrowIfCancellationRequested();

        var drawingsByPartId = new Dictionary<string, Drawing>(StringComparer.Ordinal);
        var parts = new List<NestJobPart>();
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (item == null || item.Quantity <= 0)
                continue;
            var partId = $"part-{i}";
            parts.Add(DrawingJobMapper.FromItem(partId, item));
            drawingsByPartId[partId] = item.Drawing;
        }

        var job = new NestJob(parts, request.Stock, request.Options);
        NestJobValidator.Validate(job);
        return (job, drawingsByPartId);
    }

    private static int Placed(NestPipelineResult result) =>
        result.Raw.Plates.Sum(p => p.Placements.Count);

    // Trial progress hid the engine's proxy commit counts; report validated physical ones.
    private static NestPipelineResult Committed(NestPipelineResult result,
        IProgress<NestJobProgress> progress, CancellationToken token)
    {
        var count = 0;
        for (var i = 0; i < result.Raw.Plates.Count; i++)
        {
            count += result.Raw.Plates[i].Placements.Count;
            progress?.Report(new NestJobProgress(NestJobStage.PlateCommitted,
                result.Raw.Plates[i].StockId, i, i + 1, count));
        }
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static NestPipelineResult Evaluate(
        INestingEngine engine,
        string engineName,
        NestJob job,
        CutoutPipelinePrepass prepass,
        IReadOnlyDictionary<string, Drawing> drawingsByPartId,
        IProgress<NestJobProgress> progress,
        CancellationToken token
    )
    {
        var engineJob = prepass?.EngineJob ?? job;
        IReadOnlyDictionary<string, int> released = new Dictionary<string, int>();
        if (prepass != null)
            NestJobValidator.Validate(engineJob);

        var clock = Stopwatch.StartNew();
        var raw =
            engine.Solve(engineJob, prepass == null || progress == null
                ? progress : new PreviewProgress(progress), token)
            ?? throw new InvalidOperationException($"Engine '{engineName}' returned no result.");
        var solveTime = clock.Elapsed;
        token.ThrowIfCancellationRequested();

        // D8/C10: only the ordinary solve offers last-resort clamp removal; an incomplete
        // cutout trial is compared with that solve instead. No-op whenever nothing is blocked.
        if (prepass == null)
        {
            var recovered = ClampRemovalRecovery.TryRecover(job, raw, engine, token);
            if (recovered != null)
            {
                job = recovered.Job;
                engineJob = recovered.Job;
                raw = recovered.Result;
            }
        }
        token.ThrowIfCancellationRequested();

        clock.Restart();
        var names = drawingsByPartId.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Name ?? kv.Key,
            StringComparer.Ordinal
        );
        var checkedNames = prepass == null ? names : engineJob.Parts.ToDictionary(p => p.Id,
            p => names.GetValueOrDefault(p.Id, p.Id), StringComparer.Ordinal);
        var violations = NestLayoutCheck.Violations(engineJob, raw, checkedNames, out var canKeep).ToList();
        if (prepass != null)
        {
            // A compound may be physically invalid even when its envelope is legal.
            // Refuse every unverified trial, including with explicit invalid-result consent.
            var failure = "Cutout engine output failed reconciliation";
            if (violations.Count == 0 && prepass.TryExpand(job, raw, token, out var expanded, out failure))
            {
                released = prepass.ReleasedInserts(raw);
                raw = expanded;
                violations.AddRange(NestLayoutCheck.Violations(job, raw, names, out var physicalKeep));
                canKeep = physicalKeep && violations.Count == 0;
            }
            else
            {
                if (violations.Count == 0)
                    violations.Add(failure);
                canKeep = false;
            }
        }
        var freshness = NestPipelineDrawingFreshness.Changes(job, drawingsByPartId);
        violations.AddRange(freshness);
        if (freshness.Count > 0)
            canKeep = false;
        var validationTime = clock.Elapsed;
        if (canKeep && violations.Count == 0)
            _ = NestJobCost.Evaluate(job, raw);

        // Invalid-but-representable output retains its physical review correspondence.
        var consolidate = violations.Count == 0;
        var plates = new List<ProposedPlate>();
        if (canKeep)
            foreach (var group in RepeatedPlateGrouping.Create(raw.Plates, consolidate, token))
            {
                token.ThrowIfCancellationRequested();
                plates.Add(new ProposedPlate(group.Sheet.PlateIndex, group.Sheet.Stock,
                    NestResultBinder.Bind(group.Sheet, drawingsByPartId))
                { Quantity = group.Quantity });
            }

        // Binding clones the caller's current Program; a concurrent edit during binding
        // must not return parts whose bytes differ from the already validated snapshot.
        if (canKeep)
        {
            var changes = NestPipelineDrawingFreshness.Changes(job, drawingsByPartId).ToList();
            changes.AddRange(NestPipelineDrawingFreshness.BoundChanges(job, raw, plates, drawingsByPartId, consolidate, token));
            if (changes.Count > 0)
            {
                violations.AddRange(changes);
                plates.Clear();
                canKeep = false;
            }
        }

        token.ThrowIfCancellationRequested();
        return new NestPipelineResult(
            engineName,
            job,
            raw,
            plates.AsReadOnly(),
            violations,
            canKeep,
            solveTime,
            validationTime,
            drawingsByPartId,
            NestUnplacedParts.Classify(job, raw, canKeep, drawingsByPartId, released)
        );
    }
}
