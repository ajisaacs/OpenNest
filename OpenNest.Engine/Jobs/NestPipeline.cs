using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs.Adapters;

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
        TimeSpan solveTime,
        TimeSpan validationTime
    )
    {
        EngineName = engineName;
        Job = job;
        Raw = raw;
        Plates = plates;
        Violations = violations;
        SolveTime = solveTime;
        ValidationTime = validationTime;
    }

    public string EngineName { get; }
    public NestJob Job { get; }
    public NestJobResult Raw { get; }
    public IReadOnlyList<ProposedPlate> Plates { get; }
    public IReadOnlyList<string> Violations { get; }
    public bool IsValid => Violations.Count == 0;
    public NestJobStatus Status => Raw.Status;
    public NestJobStopReason StopReason => Raw.StopReason;
    public TimeSpan SolveTime { get; }
    public TimeSpan ValidationTime { get; }
}

/// <summary>
/// The single automatic-nesting path shared by every front end: build the job, solve it
/// with the named engine, validate the result with the benchmark's rules, then bind
/// placements to the caller's drawings. The pipeline never mutates caller items, drawings,
/// or plates, and it does not care which engine is selected.
/// </summary>
public static class NestPipeline
{
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

        var clock = Stopwatch.StartNew();
        var raw =
            engine.Solve(job, progress, token)
            ?? throw new InvalidOperationException($"Engine '{engineName}' returned no result.");
        var solveTime = clock.Elapsed;
        token.ThrowIfCancellationRequested();

        clock.Restart();
        var violations = Validate(job, raw, drawingsByPartId);
        var validationTime = clock.Elapsed;

        var plates = raw
            .Plates.Select(sheet => new ProposedPlate(
                sheet.PlateIndex,
                sheet.Stock,
                NestResultBinder.Bind(sheet, drawingsByPartId)
            ))
            .ToList();

        token.ThrowIfCancellationRequested();
        return new NestPipelineResult(
            engineName,
            job,
            raw,
            plates,
            violations,
            solveTime,
            validationTime
        );
    }

    /// <summary>Benchmark validation, with messages naming the caller's drawings rather than
    /// internal requirement IDs. Placements for unknown requirements are reported and then
    /// excluded so the remaining layout is still checked.</summary>
    private static IReadOnlyList<string> Validate(
        NestJob job,
        NestJobResult raw,
        IReadOnlyDictionary<string, Drawing> drawingsByPartId
    )
    {
        var violations = new List<string>();
        var known = raw;
        var unknown = raw
            .Plates.SelectMany(sheet =>
                sheet.Placements.Where(p => !drawingsByPartId.ContainsKey(p.PartId))
                    .Select(p => (sheet.PlateIndex, p.PartId))
            )
            .ToList();

        if (unknown.Count > 0)
        {
            foreach (var group in unknown.GroupBy(u => (u.PlateIndex, u.PartId)))
                violations.Add(
                    $"Plate {group.Key.PlateIndex} has {group.Count()} placement(s) for '{group.Key.PartId}', which is not part of this job"
                );

            known = new NestJobResult(
                raw.Status,
                raw.StopReason,
                raw.Plates.Select(sheet => new NestJobPlateResult(
                    sheet.PlateIndex,
                    sheet.Stock,
                    sheet.Placements.Where(p => drawingsByPartId.ContainsKey(p.PartId))
                )),
                raw.Fulfillment,
                raw.StockUsage
            );
        }

        var names = drawingsByPartId.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Name ?? kv.Key,
            StringComparer.Ordinal
        );
        violations.AddRange(NestLayoutCheck.Violations(job, known, names));
        return violations;
    }
}
