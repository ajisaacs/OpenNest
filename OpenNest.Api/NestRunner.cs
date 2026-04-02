using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Api;

public static class NestRunner
{
    public static Task<NestResponse> RunAsync(
        NestRequest request,
        IProgress<NestProgress> progress = null,
        CancellationToken token = default)
    {
        if (request.Parts.Count == 0)
            throw new ArgumentException("Request must contain at least one part.", nameof(request));

        var sw = Stopwatch.StartNew();

        // 1. Import DXFs → Drawings
        var drawings = new List<Drawing>();
        var importer = new DxfImporter();

        foreach (var part in request.Parts)
        {
            if (!File.Exists(part.DxfPath))
                throw new FileNotFoundException($"DXF file not found: {part.DxfPath}", part.DxfPath);

            if (!importer.GetGeometry(part.DxfPath, out var geometry) || geometry.Count == 0)
                throw new InvalidOperationException($"Failed to import DXF: {part.DxfPath}");

            var normalized = ShapeProfile.NormalizeEntities(geometry);
            var pgm = ConvertGeometry.ToProgram(normalized);
            var name = Path.GetFileNameWithoutExtension(part.DxfPath);
            var drawing = new Drawing(name);
            drawing.Program = pgm;
            drawings.Add(drawing);
        }

        // 2. Build NestItems
        var items = new List<NestItem>();
        for (var i = 0; i < request.Parts.Count; i++)
        {
            var part = request.Parts[i];
            items.Add(new NestItem
            {
                Drawing = drawings[i],
                Quantity = part.Quantity,
                Priority = part.Priority,
                StepAngle = part.AllowRotation ? 0 : OpenNest.Math.Angle.TwoPI,
            });
        }

        // 3. Multi-plate loop
        var nest = new Nest();
        nest.Thickness = request.Thickness;
        nest.Material = new Material(request.Material);
        var remaining = items.Select(item => item.Quantity).ToList();

        while (remaining.Any(q => q > 0))
        {
            token.ThrowIfCancellationRequested();

            var plate = new Plate(request.SheetSize)
            {
                PartSpacing = request.Spacing,
            };

            // Build items for this pass with remaining quantities
            var passItems = new List<NestItem>();
            for (var i = 0; i < items.Count; i++)
            {
                if (remaining[i] <= 0) continue;
                passItems.Add(new NestItem
                {
                    Drawing = items[i].Drawing,
                    Quantity = remaining[i],
                    Priority = items[i].Priority,
                    StepAngle = items[i].StepAngle,
                });
            }

            // Run engine
            var engine = NestEngineRegistry.Create(plate);
            var parts = engine.Nest(passItems, progress, token);

            if (parts.Count == 0)
                break; // No progress — part doesn't fit on fresh sheet

            // Add parts to plate and nest
            foreach (var p in parts)
                plate.Parts.Add(p);

            nest.Plates.Add(plate);

            // Deduct placed quantities
            foreach (var p in parts)
            {
                var idx = drawings.IndexOf(p.BaseDrawing);
                if (idx >= 0)
                    remaining[idx]--;
            }
        }

        // 4. Compute timing
        var timingInfo = Timing.GetTimingInfo(nest);
        var cutTime = Timing.CalculateTime(timingInfo, request.Cutting);

        sw.Stop();

        // 5. Build response
        var response = new NestResponse
        {
            SheetCount = nest.Plates.Count,
            Utilization = nest.Plates.Count > 0
                ? nest.Plates.Average(p => p.Utilization())
                : 0,
            CutTime = cutTime,
            Elapsed = sw.Elapsed,
            Nest = nest,
            Request = request
        };

        return Task.FromResult(response);
    }
}
