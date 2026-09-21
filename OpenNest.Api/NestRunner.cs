using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.IO;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Api;

public static class NestRunner
{
    private const string LegacyStockId = "legacy-sheet";

    public static Task<NestResponse> RunAsync(
        NestRequest request,
        IProgress<NestProgress> progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestParts =
            request.Parts
            ?? throw new ArgumentException("Request parts must not be null.", nameof(request));
        if (requestParts.Count == 0)
            throw new ArgumentException("Request must contain at least one part.", nameof(request));

        var sw = Stopwatch.StartNew();
        var parts = IdentifyParts(requestParts);
        var importedByPath = new Dictionary<string, Drawing>(StringComparer.Ordinal);
        var jobParts = new List<NestJobPart>(parts.Count);

        foreach (var part in parts)
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(part.Request.DxfPath))
                throw new FileNotFoundException(
                    $"DXF file not found: {part.Request.DxfPath}",
                    part.Request.DxfPath
                );

            if (!importedByPath.TryGetValue(part.Request.DxfPath, out var drawing))
            {
                try
                {
                    drawing = CadImporter.ImportDrawing(
                        part.Request.DxfPath,
                        new CadImportOptions { Quantity = part.Request.Quantity }
                    );
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Failed to import DXF: {part.Request.DxfPath}",
                        exception
                    );
                }

                if (drawing.Program == null || drawing.Program.Codes.Count == 0)
                    throw new InvalidOperationException(
                        $"Failed to import DXF: {part.Request.DxfPath}"
                    );

                importedByPath.Add(part.Request.DxfPath, drawing);
            }

            ConfigureDrawingForRequirement(drawing, part.Request);
            jobParts.Add(DrawingJobMapper.FromDrawing(part.Id, drawing, part.Request.Quantity));
        }

        var job = new NestJob(
            jobParts,
            CreateStock(request),
            new NestJobOptions(ResolvePlacementStrategy(request))
        );
        var jobProgress = progress == null ? null : new JobProgressBridge(progress);
        var result = new NestJobRunner(PlateNesterFactory.Create).Solve(job, jobProgress, token);

        // This is the sole translation from immutable result poses to mutable legacy output objects.
        var materialized = NestResultMaterializer.Materialize(job, result);
        var nest = materialized.Nest;
        nest.Thickness = request.Thickness;
        nest.Material = new Material(request.Material);

        var timingInfo = Timing.GetTimingInfo(nest);
        var cutTime = Timing.CalculateTime(timingInfo, request.Cutting);
        sw.Stop();

        return Task.FromResult(
            new NestResponse
            {
                SheetCount = nest.Plates.Count,
                Utilization = CalculateUtilization(nest),
                CutTime = cutTime,
                Elapsed = sw.Elapsed,
                Status = result.Status,
                StopReason = result.StopReason,
                Fulfillment = result
                    .Fulfillment.Select(value => new NestPartFulfillment(
                        value.PartId,
                        value.Requested,
                        value.Placed,
                        value.Unplaced
                    ))
                    .ToArray(),
                StockUsage = result
                    .StockUsage.Select(value => new NestStockUsage(
                        value.StockId,
                        value.Used,
                        value.Remaining
                    ))
                    .ToArray(),
                PlateStockMappings = result
                    .Plates.Select(value => new NestPlateStockMapping(
                        value.PlateIndex,
                        value.StockId
                    ))
                    .ToArray(),
                Nest = nest,
                Request = request,
            }
        );
    }

    private static IReadOnlyList<IdentifiedRequestPart> IdentifyParts(
        IReadOnlyList<NestRequestPart> requestParts
    )
    {
        var identified = new List<IdentifiedRequestPart>(requestParts.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < requestParts.Count; index++)
        {
            var part =
                requestParts[index]
                ?? throw new ArgumentException(
                    "Request parts must not contain null entries.",
                    nameof(requestParts)
                );
            var id = part.Id ?? $"part-{index}";
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Part IDs must not be blank.", nameof(requestParts));
            if (!ids.Add(id))
                throw new ArgumentException("Part IDs must be unique.", nameof(requestParts));
            identified.Add(new IdentifiedRequestPart(id, part));
        }

        return identified;
    }

    private static IReadOnlyList<NestPlateStock> CreateStock(NestRequest request)
    {
        if (request.Plates is null)
        {
            return
            [
                new NestPlateStock(
                    LegacyStockId,
                    request.SheetSize,
                    quantity: null,
                    partSpacing: request.Spacing
                ),
            ];
        }

        var stock = new List<NestPlateStock>(request.Plates.Count);
        foreach (var plate in request.Plates)
        {
            if (plate is null)
                throw new ArgumentException(
                    "Request plates must not contain null entries.",
                    nameof(request)
                );
            stock.Add(
                new NestPlateStock(
                    plate.Id,
                    plate.Size,
                    plate.Quantity,
                    plate.PartSpacing,
                    plate.EdgeSpacing,
                    plate.Quadrant
                )
            );
        }

        return stock;
    }

    private static void ConfigureDrawingForRequirement(Drawing drawing, NestRequestPart part)
    {
        drawing.Priority = part.Priority;
        drawing.Constraints ??= new NestConstraints();
        if (!part.AllowRotation)
        {
            // A zero legacy step means automatic rotation to DrawingJobMapper, so lock it explicitly.
            drawing.Constraints.StepAngle = OpenNest.Math.Angle.TwoPI;
            drawing.Constraints.StartAngle = 0;
            drawing.Constraints.EndAngle = 0;
        }
    }

    private static string ResolvePlacementStrategy(NestRequest request) =>
        request.PlacementStrategy
        ?? request.Strategy switch
        {
            NestStrategy.Auto => "Default",
            _ => throw new NotSupportedException(
                $"Unknown legacy nesting strategy: {request.Strategy}."
            ),
        };

    private static double CalculateUtilization(Nest nest)
    {
        var sheetArea = nest.Plates.Sum(plate => plate.Area());
        if (sheetArea == 0)
            return 0;
        var placedArea = nest.Plates.Sum(plate =>
            plate.Parts.Where(part => !part.BaseDrawing.IsCutOff).Sum(part => part.BaseDrawing.Area)
        );
        return placedArea / sheetArea;
    }

    private sealed record IdentifiedRequestPart(string Id, NestRequestPart Request);

    private sealed class JobProgressBridge(IProgress<NestProgress> progress)
        : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.LegacyProgress is not null)
                progress.Report(value.LegacyProgress);
        }
    }
}
