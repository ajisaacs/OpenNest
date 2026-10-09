using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using ModelContextProtocol.Server;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.Mcp.Tools
{
    [McpServerToolType]
    public class NestingTools
    {
        private readonly NestSession _session;

        private const string FillStrategies =
            "Placement strategy: Fill (default; lattice fill of repeated copies), Strip, "
            + "Vertical Remnant (keeps a right-side drop), Horizontal Remnant (keeps a top-side drop)";

        /// <summary>Built-in engines with what each suits; a test keeps this in step with the registry.</summary>
        private const string JobEngines =
            "Whole-job engine. Default (used when omitted): suits any job; runs Irregular and Rectangles "
            + "and keeps the cheapest valid layout. "
            + "Irregular: irregular profiles; no-fit-polygon packing into notches and gaps, best-fit pairs. "
            + "Rectangles: plain and near-rectangular parts; packs each part as its bounding box, fastest. "
            + "Fill: many copies of few drawings; lattice fill with pairs and rectangle best-fit. "
            + "Strip: Fill variant laying mixed drawings in strips. "
            + "Vertical Remnant, Horizontal Remnant: Fill variants keeping a right-side or top-side drop. "
            + "StockLadder: constrained parts first, then area repacking; meant for several stock sizes. "
            + "Plug-ins loaded from Engines/ use their class name.";

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
            [Description(FillStrategies)]
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
            [Description(FillStrategies)]
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
            [Description(FillStrategies)]
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
            [Description(FillStrategies)]
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

            AppendMixSummary(sb, items, parts);

            return sb.ToString();
        }

        [McpServerTool(Name = "autonest_plate")]
        [Description("Validated whole-job nesting onto an empty single sheet. Invalid results are discarded unless allow_invalid is explicitly true; unrepresentable or multiple-sheet results are always rejected.")]
        public string AutoNestPlate(
            [Description("Index of the empty plate")] int plateIndex,
            [Description("Comma-separated drawing names")] string drawingNames,
            [Description("Comma-separated positive quantities")] string quantities,
            [Description(JobEngines)] string engine = null,
            [Description("Explicitly keep representable layouts despite validation violations")] bool allow_invalid = false,
            CancellationToken cancellationToken = default
        )
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";
            if (plate.Parts.Count > 0)
                return "Error: autonest cannot use an occupied plate. Use fill_area or fill_remnants for existing obstacles.";
            if (string.IsNullOrWhiteSpace(drawingNames))
                return "Error: drawingNames is required";
            if (string.IsNullOrWhiteSpace(quantities))
                return "Error: quantities is required";

            var engineName = string.IsNullOrWhiteSpace(engine) ? _session.DefaultEngineName : engine.Trim();
            var parsed = ParseItems(drawingNames, quantities);
            if (parsed.error != null)
                return parsed.error;
            if (parsed.items.Any(item => item.Quantity <= 0))
                return "Error: autonest quantities must be positive";

            NestPipelineResult result;
            try
            {
                result = NestPipeline.Run(new NestPipelineRequest(
                    engineName, parsed.items, NestStockBuilder.SinglePlate(plate)), token: cancellationToken);
            }
            catch (NotSupportedException)
            {
                return UnknownEngineMessage(engineName);
            }

            var sb = new StringBuilder();
            foreach (var violation in result.Violations)
                sb.AppendLine($"Violation: {violation}");
            if (!result.CanKeep || result.Plates.Count > 1 || (!result.IsValid && !allow_invalid))
            {
                sb.AppendLine(result.Plates.Count > 1
                    ? "Error: multiple result sheets cannot be merged onto one target, even with allow_invalid. Nothing committed."
                    : !result.CanKeep
                        ? "Error: result cannot be represented faithfully, even with allow_invalid. Nothing committed."
                        : "Error: invalid result discarded. Set allow_invalid to explicitly keep its violations. Nothing committed.");
                return sb.ToString();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var proposed = result.Plates.SingleOrDefault();
            var totalPlaced = proposed?.Parts.Count ?? 0;
            if (totalPlaced > 0)
            {
                plate.Size = proposed.Stock.Size;
                plate.PartSpacing = proposed.Stock.PartSpacing;
                plate.EdgeSpacing = proposed.Stock.EdgeSpacing;
                plate.Quadrant = proposed.Stock.Quadrant;
                plate.Quantity = 1;
                plate.Parts.AddRange(proposed.Parts);
            }
            sb.AppendLine($"AutoNest plate {plateIndex} ({engineName} engine): {(totalPlaced > 0 ? "success" : "no parts placed")}");
            sb.AppendLine($"  Parts placed: {totalPlaced}");
            sb.AppendLine($"  Total parts: {plate.Parts.Count}");
            sb.AppendLine($"  Utilization: {plate.Utilization():P1}");
            AppendMixSummary(sb, parsed.items, proposed == null ? Enumerable.Empty<Part>() : proposed.Parts);
            return sb.ToString();
        }

        private static void AppendMixSummary(StringBuilder sb, IReadOnlyList<NestItem> items, IEnumerable<Part> newlyPlaced)
        {
            var counts = new Dictionary<Drawing, int>(ReferenceEqualityComparer.Instance);
            foreach (var part in newlyPlaced)
                counts[part.BaseDrawing] = counts.TryGetValue(part.BaseDrawing, out var n) ? n + 1 : 1;

            var placed = new int[items.Count];
            sb.AppendLine("  Requested mix (new placements only):");
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                placed[i] = counts.GetValueOrDefault(item.Drawing);
                var remaining = item.Quantity > 0 ? (item.Quantity - placed[i]).ToString() : "unlimited";
                sb.AppendLine($"    [{i}] {item.Drawing.Name}: requested={item.Quantity}, placed={placed[i]}, remaining={remaining}");
            }

            var total = placed.Sum(count => (long)count);
            if (total == 0)
                sb.AppendLine("  Mix: zero progress for all requested drawings.");
            else if (items.Count > 1 && items.All(item => item.Quantity > 0))
            {
                var balanced = true;
                for (var i = 1; i < items.Count; i++)
                    if ((long)placed[i] * items[0].Quantity != (long)placed[0] * items[i].Quantity)
                        balanced = false;
                sb.AppendLine(balanced
                    ? "  Mix: ratio satisfied for new placements; remaining demand may still exist."
                    : "  Warning: ratio not met by new placements; quantities are ceilings, not a balance constraint.");
            }
            else if (items.Count > 1)
                sb.AppendLine("  Mix: ratio not evaluated for unlimited or nonpositive requested quantities.");
        }

        /// <summary>
        /// Resolves the requested fill strategy; an omitted name means Fill. Returns null when the
        /// name is not a single-plate placement strategy.
        /// </summary>
        private static string ResolveStrategy(string engine)
        {
            try
            {
                return PlateFillService.ResolveStrategy(engine?.Trim());
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private static string EngineError(string engine) => UnknownEngineMessage(engine.Trim());

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
            var seen = new HashSet<Drawing>(ReferenceEqualityComparer.Instance);

            for (var i = 0; i < names.Length; i++)
            {
                var matches = _session.AllDrawings().Where(d => d.Name == names[i]).ToArray();
                if (matches.Length == 0)
                    return (null, $"Error: drawing '{names[i]}' not found");
                if (matches.Length > 1)
                    return (null, $"Error: ambiguous drawing name '{names[i]}' matches multiple drawings");
                var drawing = matches[0];
                if (!seen.Add(drawing))
                    return (null, $"Error: duplicate drawing '{names[i]}' in request");

                items.Add(new NestItem { Drawing = drawing, Quantity = qtys[i] });
            }

            return (items, null);
        }
    }
}
