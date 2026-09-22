using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using ModelContextProtocol.Server;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Engine;

namespace OpenNest.Mcp.Tools
{
    [McpServerToolType]
    public class NestingTools
    {
        private readonly NestSession _session;

        public NestingTools(NestSession session)
        {
            _session = session;
        }

        [McpServerTool(Name = "fill_plate")]
        [Description(
            "Fill an entire plate with a single drawing. Returns parts added and utilization."
        )]
        public string FillPlate(
            [Description("Index of the plate to fill")] int plateIndex,
            [Description("Name of the drawing to fill with")] string drawingName,
            [Description("Maximum quantity to place (0 = unlimited)")] int quantity = 0,
            [Description("Placement strategy: Default, Strip, Vertical Remnant, Horizontal Remnant")]
                string engine = null
        )
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            var drawing = _session.GetDrawing(drawingName);
            if (drawing == null)
                return $"Error: drawing '{drawingName}' not found";

            var strategy = ResolveStrategy(engine);
            if (strategy == null)
                return EngineError(engine);

            var countBefore = plate.Parts.Count;
            var item = new NestItem { Drawing = drawing, Quantity = quantity };
            var parts = PlateFillService.FillItem(
                strategy,
                plate,
                item,
                plate.WorkArea(),
                null,
                CancellationToken.None
            );
            plate.Parts.AddRange(parts);
            var success = parts.Count > 0;

            var countAfter = plate.Parts.Count;
            var added = countAfter - countBefore;

            var sb = new StringBuilder();
            sb.AppendLine(
                $"Fill plate {plateIndex} with '{drawingName}' ({strategy}): {(success ? "success" : "failed")}"
            );
            sb.AppendLine($"  Parts added: {added}");
            sb.AppendLine($"  Total parts: {countAfter}");
            sb.AppendLine($"  Utilization: {plate.Utilization():P1}");

            return sb.ToString();
        }

        [McpServerTool(Name = "fill_area")]
        [Description("Fill a specific rectangular area on a plate with a single drawing.")]
        public string FillArea(
            [Description("Index of the plate")] int plateIndex,
            [Description("Name of the drawing to fill with")] string drawingName,
            [Description("X origin of the area")] double x,
            [Description("Y origin of the area")] double y,
            [Description("Width of the area")] double width,
            [Description("Length of the area")] double length,
            [Description("Maximum quantity to place (0 = unlimited)")] int quantity = 0,
            [Description("Placement strategy: Default, Strip, Vertical Remnant, Horizontal Remnant")]
                string engine = null
        )
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            var drawing = _session.GetDrawing(drawingName);
            if (drawing == null)
                return $"Error: drawing '{drawingName}' not found";

            var strategy = ResolveStrategy(engine);
            if (strategy == null)
                return EngineError(engine);

            var countBefore = plate.Parts.Count;
            var item = new NestItem { Drawing = drawing, Quantity = quantity };
            var area = new Box(x, y, width, length);
            var parts = PlateFillService.FillItem(
                strategy,
                plate,
                item,
                area,
                null,
                CancellationToken.None
            );
            plate.Parts.AddRange(parts);
            var success = parts.Count > 0;

            var countAfter = plate.Parts.Count;
            var added = countAfter - countBefore;

            var sb = new StringBuilder();
            sb.AppendLine(
                $"Fill area ({x:F1},{y:F1} {width:F1}x{length:F1}) on plate {plateIndex} with '{drawingName}' ({strategy}): {(success ? "success" : "failed")}"
            );
            sb.AppendLine($"  Parts added: {added}");
            sb.AppendLine($"  Total parts: {countAfter}");
            sb.AppendLine($"  Utilization: {plate.Utilization():P1}");

            return sb.ToString();
        }

        [McpServerTool(Name = "fill_remnants")]
        [Description("Find empty remnant regions on a plate and fill each with a drawing.")]
        public string FillRemnants(
            [Description("Index of the plate")] int plateIndex,
            [Description("Name of the drawing to fill with")] string drawingName,
            [Description("Maximum quantity per remnant (0 = unlimited)")] int quantity = 0,
            [Description("Placement strategy: Default, Strip, Vertical Remnant, Horizontal Remnant")]
                string engine = null
        )
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            var drawing = _session.GetDrawing(drawingName);
            if (drawing == null)
                return $"Error: drawing '{drawingName}' not found";

            var strategy = ResolveStrategy(engine);
            if (strategy == null)
                return EngineError(engine);

            var finder = RemnantFinder.FromPlate(plate);
            var remnants = finder.FindRemnants();

            if (remnants.Count == 0)
                return $"No remnant areas found on plate {plateIndex}";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {remnants.Count} remnant area(s) on plate {plateIndex}");

            var totalAdded = 0;

            for (var i = 0; i < remnants.Count; i++)
            {
                var remnant = remnants[i];
                var countBefore = plate.Parts.Count;
                var item = new NestItem { Drawing = drawing, Quantity = quantity };
                var parts = PlateFillService.FillItem(
                    strategy,
                    plate,
                    item,
                    remnant,
                    null,
                    CancellationToken.None
                );
                plate.Parts.AddRange(parts);
                var added = plate.Parts.Count - countBefore;
                totalAdded += added;

                sb.AppendLine(
                    $"  Remnant {i}: ({remnant.X:F1},{remnant.Y:F1} {remnant.Width:F1}x{remnant.Length:F1}) -> {added} parts {(added > 0 ? "" : "(no fit)")}"
                );
            }

            sb.AppendLine($"Total parts added: {totalAdded}");
            sb.AppendLine($"Utilization: {plate.Utilization():P1}");

            return sb.ToString();
        }

        [McpServerTool(Name = "pack_plate")]
        [Description(
            "Pack multiple drawings onto a plate using bin-packing. Specify drawings and quantities as comma-separated lists."
        )]
        public string PackPlate(
            [Description("Index of the plate")] int plateIndex,
            [Description("Comma-separated drawing names")] string drawingNames,
            [Description("Comma-separated quantities for each drawing")] string quantities,
            [Description("Placement strategy: Default, Strip, Vertical Remnant, Horizontal Remnant")]
                string engine = null
        )
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            if (string.IsNullOrWhiteSpace(drawingNames))
                return "Error: drawingNames is required";

            if (string.IsNullOrWhiteSpace(quantities))
                return "Error: quantities is required";

            var strategy = ResolveStrategy(engine);
            if (strategy == null)
                return EngineError(engine);

            var parsed = ParseItems(drawingNames, quantities);
            if (parsed.error != null)
                return parsed.error;

            var items = parsed.items;

            var countBefore = plate.Parts.Count;
            var parts = PlateFillService.PackArea(
                strategy,
                plate,
                plate.WorkArea(),
                items,
                null,
                CancellationToken.None
            );
            plate.Parts.AddRange(parts);
            var success = parts.Count > 0;
            var countAfter = plate.Parts.Count;
            var added = countAfter - countBefore;

            var sb = new StringBuilder();
            sb.AppendLine($"Pack plate {plateIndex} ({strategy}): {(success ? "success" : "failed")}");
            sb.AppendLine($"  Parts added: {added}");
            sb.AppendLine($"  Total parts: {countAfter}");
            sb.AppendLine($"  Utilization: {plate.Utilization():P1}");

            // Breakdown by drawing
            var groups = plate.Parts.GroupBy(p => p.BaseDrawing.Name);
            foreach (var group in groups)
                sb.AppendLine($"  {group.Key}: {group.Count()}");

            return sb.ToString();
        }

        [McpServerTool(Name = "autonest_plate")]
        [Description(
            "Mixed-part autonesting. Solves the drawings as one whole job against the plate using the named jobs engine and commits the resulting placements onto the plate."
        )]
        public string AutoNestPlate(
            [Description("Index of the plate")] int plateIndex,
            [Description("Comma-separated drawing names")] string drawingNames,
            [Description("Comma-separated quantities for each drawing")] string quantities,
            [Description("Jobs engine name (Default, Strip, Vertical Remnant, Horizontal Remnant, StockLadder)")]
                string engine = null
        )
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            if (string.IsNullOrWhiteSpace(drawingNames))
                return "Error: drawingNames is required";

            if (string.IsNullOrWhiteSpace(quantities))
                return "Error: quantities is required";

            var engineName = string.IsNullOrWhiteSpace(engine)
                ? _session.DefaultEngineName
                : engine.Trim();

            var parsed = ParseItems(drawingNames, quantities);
            if (parsed.error != null)
                return parsed.error;

            if (parsed.items.Any(item => item.Quantity <= 0))
                return "Error: autonest quantities must be positive";

            INestingEngine nestingEngine;
            try
            {
                nestingEngine = NestingEngineRegistry.Create(engineName);
            }
            catch (NotSupportedException)
            {
                return UnknownEngineMessage(engineName);
            }

            var jobParts = new List<NestJobPart>(parsed.items.Count);
            var drawingsByPartId = new Dictionary<string, Drawing>(StringComparer.Ordinal);
            for (var i = 0; i < parsed.items.Count; i++)
            {
                var partId = $"part-{i}";
                jobParts.Add(DrawingJobMapper.FromItem(partId, parsed.items[i]));
                drawingsByPartId[partId] = parsed.items[i].Drawing;
            }

            // One physical sheet: this plate, this solve — the runner owns stock accounting.
            var stock = DrawingJobMapper.FromPlate("plate-0", plate, 1);
            var job = new NestJob(jobParts, [stock]);

            var result = nestingEngine.Solve(job, null, CancellationToken.None);

            var totalPlaced = 0;
            foreach (var plateResult in result.Plates)
            {
                foreach (var pose in plateResult.Placements)
                {
                    if (!drawingsByPartId.TryGetValue(pose.PartId, out var drawing))
                        continue;
                    var part = new Part(drawing);
                    part.Rotate(pose.Rotation);
                    part.Location = new Vector(pose.X, pose.Y);
                    part.UpdateBounds();
                    plate.Parts.Add(part);
                    totalPlaced++;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(
                $"AutoNest plate {plateIndex} ({engineName} engine): {(totalPlaced > 0 ? "success" : "no parts placed")}"
            );
            sb.AppendLine($"  Parts placed: {totalPlaced}");
            sb.AppendLine($"  Total parts: {plate.Parts.Count}");
            sb.AppendLine($"  Utilization: {plate.Utilization():P1}");

            var groups = plate.Parts.GroupBy(p => p.BaseDrawing.Name);
            foreach (var group in groups)
                sb.AppendLine($"  {group.Key}: {group.Count()}");

            return sb.ToString();
        }

        /// <summary>
        /// Resolves the requested fill strategy, falling back to the session default. Returns null
        /// when the name is not a single-plate placement strategy.
        /// </summary>
        private string ResolveStrategy(string engine)
        {
            var requested = string.IsNullOrWhiteSpace(engine)
                ? _session.DefaultEngineName
                : engine.Trim();

            try
            {
                return PlateFillService.ResolveStrategy(requested);
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private string EngineError(string engine) =>
            UnknownEngineMessage(
                string.IsNullOrWhiteSpace(engine) ? _session.DefaultEngineName : engine.Trim()
            );

        private static string UnknownEngineMessage(string engineName)
        {
            var isJobEngine = NestingEngineRegistry.AvailableEngines.Any(e =>
                e.Name.Equals(engineName, StringComparison.OrdinalIgnoreCase)
            );
            return isJobEngine
                ? $"Error: engine '{engineName}' is a whole-job engine; this tool supports: {string.Join(", ", PlateFillService.BuiltInStrategies)}. Use autonest_plate for whole-job engines."
                : $"Error: unknown engine '{engineName}'. Fill strategies: {string.Join(", ", PlateFillService.BuiltInStrategies)}; jobs engines: {string.Join(", ", NestingEngineRegistry.AvailableEngines.Select(e => e.Name))}";
        }

        private (List<NestItem> items, string error) ParseItems(string drawingNames, string quantities)
        {
            var names = drawingNames.Split(',').Select(n => n.Trim()).ToArray();
            var qtyStrings = quantities.Split(',').Select(q => q.Trim()).ToArray();
            var qtys = new int[qtyStrings.Length];

            for (var i = 0; i < qtyStrings.Length; i++)
            {
                if (!int.TryParse(qtyStrings[i], out qtys[i]))
                    return (null, $"Error: '{qtyStrings[i]}' is not a valid quantity");
            }

            if (names.Length != qtys.Length)
                return (
                    null,
                    $"Error: drawing names count ({names.Length}) does not match quantities count ({qtys.Length})"
                );

            var items = new List<NestItem>();

            for (var i = 0; i < names.Length; i++)
            {
                var drawing = _session.GetDrawing(names[i]);
                if (drawing == null)
                    return (null, $"Error: drawing '{names[i]}' not found");

                items.Add(new NestItem { Drawing = drawing, Quantity = qtys[i] });
            }

            return (items, null);
        }
    }
}
