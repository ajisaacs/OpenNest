using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using OpenNest.Engine.Jobs.CommonLines;
using OpenNest.Engine.Jobs.Cutouts;

namespace OpenNest.Engine.Jobs.Remote;

/// <summary>
/// The nest server worker's latest progress report for a running job: the stage of the solve
/// in progress, without its plan (plans travel as <see cref="RemoteNestPlan"/>). Committed
/// counts follow <see cref="NestJobProgress"/>: authoritative only after a commit.
/// </summary>
public sealed class RemoteNestProgress
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public NestJobStage Stage { get; set; }
    public string StockId { get; set; } = "";
    public int PlateIndex { get; set; }
    public int CommittedPlates { get; set; }
    public int CommittedParts { get; set; }

    public static RemoteNestProgress From(NestJobProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return new RemoteNestProgress
        {
            Stage = progress.Stage,
            StockId = progress.StockId ?? "",
            PlateIndex = progress.PlateIndex,
            CommittedPlates = progress.CommittedPlates,
            CommittedParts = progress.CommittedParts,
        };
    }

    public NestJobProgress ToProgress() => new(Stage, StockId, PlateIndex, CommittedPlates, CommittedParts);

    public string ToJson() => JsonSerializer.Serialize(this, NestSolveTranscript.Options);

    /// <summary>The report, or null when <paramref name="json"/> is empty or not one this build reads.</summary>
    public static RemoteNestProgress FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var progress = JsonSerializer.Deserialize<RemoteNestProgress>(json, NestSolveTranscript.Options);
            return progress?.Version == CurrentVersion ? progress : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The engine's best whole-job plan so far on the worker (<see cref="NestJobStage.PlanImproved"/>),
/// as raw placements. The desktop draws it with <see cref="RemoteNestPlanPreview"/>; like a local
/// plan it is unvalidated and never kept.
/// </summary>
public sealed class RemoteNestPlan
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public NestSolveTranscript.ResultJson Plan { get; set; }

    public static RemoteNestPlan From(NestJobResult plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new RemoteNestPlan { Plan = NestSolveTranscript.ResultJson.From(plan) };
    }

    public string ToJson() => JsonSerializer.Serialize(this, NestSolveTranscript.Options);

    /// <summary>The plan, or null when <paramref name="json"/> is not one this build reads.</summary>
    public static RemoteNestPlan FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var plan = JsonSerializer.Deserialize<RemoteNestPlan>(json, NestSolveTranscript.Options);
            return plan?.Version == CurrentVersion && plan.Plan?.Plates != null ? plan : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Draws the worker's best plan so far on the desktop, the way a local run's
/// <see cref="NestJobProgress.PlanSheets"/> are drawn: the request's job is rebuilt here (the
/// same part ids the worker solved), and parts go on detached drawings built from its geometry,
/// so nothing touches the caller's drawings or quantities. A bundled cutout trial's frame proxies
/// are bundled here too, once, the first time a plan names one. Not thread-safe: use one
/// preview from one thread at a time.
/// </summary>
public sealed class RemoteNestPlanPreview
{
    private readonly NestJob job;
    private readonly IReadOnlyDictionary<string, Drawing> callerDrawings;
    private readonly Dictionary<string, NestJobPart> parts;
    private readonly Dictionary<string, Drawing> drawings = new(StringComparer.Ordinal);
    private bool bundled;

    public RemoteNestPlanPreview(NestPipelineRequest request, CancellationToken token = default)
    {
        (job, var byPart) = NestPipeline.CreateJob(request, token);
        callerDrawings = byPart;
        parts = job.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    /// <summary>The plan as sheets, or null when <paramref name="planJson"/> is unreadable or
    /// names parts this request does not have.</summary>
    public IReadOnlyList<ProposedPlate> Sheets(string planJson, CancellationToken token = default)
    {
        if (RemoteNestPlan.FromJson(planJson)?.Plan is not { } plan)
            return null;
        if (!Known(plan) && !bundled)
        {
            bundled = true;
            if (CutoutPipelinePrepass.Prepare(job, token)?.EngineJob is { } engineJob)
            {
                foreach (var part in engineJob.Parts)
                    parts.TryAdd(part.Id, part);
            }
            // A common-line trial's pair proxies, from either share (same geometry per part).
            foreach (var share in new[] { CommonLinePipelinePrepass.Share.All, CommonLinePipelinePrepass.Share.Half })
                if (CommonLinePipelinePrepass.Prepare(job, share, token)?.EngineJob is { } pairJob)
                    foreach (var part in pairJob.Parts)
                        parts.TryAdd(part.Id, part);
        }
        if (!Known(plan))
            return null;
        try
        {
            return NestPipeline.PlanSheets(plan.ToResult(job), parts, callerDrawings, drawings, token);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private bool Known(NestSolveTranscript.ResultJson plan) =>
        plan.Plates.All(s => s?.Placements != null && s.Placements.All(p => p.PartId != null && parts.ContainsKey(p.PartId)));
}
