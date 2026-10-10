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

/// <summary>A proposed sheet: new parts bound by reference to the caller's drawings.</summary>
public sealed record ProposedPlate(int PlateIndex, NestPlateStock Stock, IReadOnlyList<Part> Parts);

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
        IReadOnlyDictionary<string, Drawing> drawingsByPartId
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

    /// <summary>Runs a caller-supplied engine instance through the same validation and binding.</summary>
    public static NestPipelineResult Run(
        INestingEngine engine,
        string engineName,
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    ) => RunCore(engine, engineName, request, progress, token, cutoutPreview: false);

    /// <summary>Internal test-only integration trial. Normal Run never prepares cutouts;
    /// this entry point must not be called by production front ends before cutting/post gates.</summary>
    internal static NestPipelineResult RunCutoutPreview(
        INestingEngine engine,
        string engineName,
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    ) => RunCore(engine, engineName, request, progress, token, cutoutPreview: true);

    internal static NestPipelineResult RunCutoutPreview(
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    ) => RunCutoutPreview(NestingEngineRegistry.Create(request.EngineName),
        request.EngineName, request, progress, token);

    private static NestPipelineResult RunCore(
        INestingEngine engine,
        string engineName,
        NestPipelineRequest request,
        IProgress<NestJobProgress> progress,
        CancellationToken token,
        bool cutoutPreview
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
        var prepass = cutoutPreview ? CutoutPipelinePrepass.Prepare(job, token) : null;
        var engineJob = prepass?.EngineJob ?? job;
        if (prepass != null)
            NestJobValidator.Validate(engineJob);

        var clock = Stopwatch.StartNew();
        var raw =
            engine.Solve(engineJob, prepass == null || progress == null
                ? progress : new PreviewProgress(progress), token)
            ?? throw new InvalidOperationException($"Engine '{engineName}' returned no result.");
        var solveTime = clock.Elapsed;
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
                raw = expanded;
                violations.AddRange(NestLayoutCheck.Violations(job, raw, names, out var physicalKeep));
                if (raw.Status != NestJobStatus.Complete)
                    violations.Add("Cutout engine did not complete the transformed job; unplaced frames' reserved inserts remain unplaced in the original job");
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

        var plates = canKeep
            ? raw.Plates.Select(sheet => new ProposedPlate(
                sheet.PlateIndex,
                sheet.Stock,
                NestResultBinder.Bind(sheet, drawingsByPartId)
            ))
            .ToList()
            : new List<ProposedPlate>();

        // Binding clones the caller's current Program; a concurrent edit during binding
        // must not return parts whose bytes differ from the already validated snapshot.
        if (canKeep)
        {
            var changes = NestPipelineDrawingFreshness.Changes(job, drawingsByPartId).ToList();
            changes.AddRange(NestPipelineDrawingFreshness.BoundChanges(job, raw, plates, drawingsByPartId));
            if (changes.Count > 0)
            {
                violations.AddRange(changes);
                plates.Clear();
                canKeep = false;
            }
        }

        token.ThrowIfCancellationRequested();
        if (prepass != null && canKeep)
        {
            var count = 0;
            for (var i = 0; i < raw.Plates.Count; i++)
            {
                count += raw.Plates[i].Placements.Count;
                progress?.Report(new NestJobProgress(NestJobStage.PlateCommitted,
                    raw.Plates[i].StockId, i, i + 1, count));
            }
        }
        token.ThrowIfCancellationRequested();
        return new NestPipelineResult(
            engineName,
            job,
            raw,
            plates,
            violations,
            canKeep,
            solveTime,
            validationTime,
            drawingsByPartId
        );
    }
}
