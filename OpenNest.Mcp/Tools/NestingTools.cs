using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using ModelContextProtocol.Server;
using OpenNest.Geometry;

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
        [Description("Fill an entire plate with a single drawing. Returns parts added and utilization.")]
        public string FillPlate(
            [Description("Index of the plate to fill")] int plateIndex,
            [Description("Name of the drawing to fill with")] string drawingName,
            [Description("Maximum quantity to place (0 = unlimited)")] int quantity = 0)
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            var drawing = _session.GetDrawing(drawingName);
            if (drawing == null)
                return $"Error: drawing '{drawingName}' not found";

            var countBefore = plate.Parts.Count;
            var engine = NestEngineRegistry.Create(plate);
            var item = new NestItem { Drawing = drawing, Quantity = quantity };
            var success = engine.Fill(item);

            var countAfter = plate.Parts.Count;
            var added = countAfter - countBefore;

            var sb = new StringBuilder();
            sb.AppendLine($"Fill plate {plateIndex} with '{drawingName}': {(success ? "success" : "failed")}");
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
            [Description("Height of the area")] double height,
            [Description("Maximum quantity to place (0 = unlimited)")] int quantity = 0)
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            var drawing = _session.GetDrawing(drawingName);
            if (drawing == null)
                return $"Error: drawing '{drawingName}' not found";

            var countBefore = plate.Parts.Count;
            var engine = NestEngineRegistry.Create(plate);
            var item = new NestItem { Drawing = drawing, Quantity = quantity };
            var area = new Box(x, y, width, height);
            var success = engine.Fill(item, area);

            var countAfter = plate.Parts.Count;
            var added = countAfter - countBefore;

            var sb = new StringBuilder();
            sb.AppendLine($"Fill area ({x:F1},{y:F1} {width:F1}x{height:F1}) on plate {plateIndex} with '{drawingName}': {(success ? "success" : "failed")}");
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
            [Description("Maximum quantity per remnant (0 = unlimited)")] int quantity = 0)
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            var drawing = _session.GetDrawing(drawingName);
            if (drawing == null)
                return $"Error: drawing '{drawingName}' not found";

            var finder = RemnantFinder.FromPlate(plate);
            var remnants = finder.FindRemnants();

            if (remnants.Count == 0)
                return $"No remnant areas found on plate {plateIndex}";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {remnants.Count} remnant area(s) on plate {plateIndex}");

            var totalAdded = 0;
            var engine = NestEngineRegistry.Create(plate);

            for (var i = 0; i < remnants.Count; i++)
            {
                var remnant = remnants[i];
                var countBefore = plate.Parts.Count;
                var item = new NestItem { Drawing = drawing, Quantity = quantity };
                var success = engine.Fill(item, remnant);
                var added = plate.Parts.Count - countBefore;
                totalAdded += added;

                sb.AppendLine($"  Remnant {i}: ({remnant.X:F1},{remnant.Y:F1} {remnant.Width:F1}x{remnant.Length:F1}) -> {added} parts {(success ? "" : "(no fit)")}");
            }

            sb.AppendLine($"Total parts added: {totalAdded}");
            sb.AppendLine($"Utilization: {plate.Utilization():P1}");

            return sb.ToString();
        }

        [McpServerTool(Name = "pack_plate")]
        [Description("Pack multiple drawings onto a plate using bin-packing. Specify drawings and quantities as comma-separated lists.")]
        public string PackPlate(
            [Description("Index of the plate")] int plateIndex,
            [Description("Comma-separated drawing names")] string drawingNames,
            [Description("Comma-separated quantities for each drawing")] string quantities)
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            if (string.IsNullOrWhiteSpace(drawingNames))
                return "Error: drawingNames is required";

            if (string.IsNullOrWhiteSpace(quantities))
                return "Error: quantities is required";

            var names = drawingNames.Split(',').Select(n => n.Trim()).ToArray();
            var qtyStrings = quantities.Split(',').Select(q => q.Trim()).ToArray();
            var qtys = new int[qtyStrings.Length];

            for (var i = 0; i < qtyStrings.Length; i++)
            {
                if (!int.TryParse(qtyStrings[i], out qtys[i]))
                    return $"Error: '{qtyStrings[i]}' is not a valid quantity";
            }

            if (names.Length != qtys.Length)
                return $"Error: drawing names count ({names.Length}) does not match quantities count ({qtys.Length})";

            var items = new List<NestItem>();

            for (var i = 0; i < names.Length; i++)
            {
                var drawing = _session.GetDrawing(names[i]);
                if (drawing == null)
                    return $"Error: drawing '{names[i]}' not found";

                items.Add(new NestItem { Drawing = drawing, Quantity = qtys[i] });
            }

            var countBefore = plate.Parts.Count;
            var engine = NestEngineRegistry.Create(plate);
            var success = engine.Pack(items);
            var countAfter = plate.Parts.Count;
            var added = countAfter - countBefore;

            var sb = new StringBuilder();
            sb.AppendLine($"Pack plate {plateIndex}: {(success ? "success" : "failed")}");
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
        [Description("Mixed-part autonesting. Fills the plate with multiple different drawings using iterative per-drawing fills with remainder-strip packing.")]
        public string AutoNestPlate(
            [Description("Index of the plate")] int plateIndex,
            [Description("Comma-separated drawing names")] string drawingNames,
            [Description("Comma-separated quantities for each drawing")] string quantities)
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            if (string.IsNullOrWhiteSpace(drawingNames))
                return "Error: drawingNames is required";

            if (string.IsNullOrWhiteSpace(quantities))
                return "Error: quantities is required";

            var names = drawingNames.Split(',').Select(n => n.Trim()).ToArray();
            var qtyStrings = quantities.Split(',').Select(q => q.Trim()).ToArray();
            var qtys = new int[qtyStrings.Length];

            for (var i = 0; i < qtyStrings.Length; i++)
            {
                if (!int.TryParse(qtyStrings[i], out qtys[i]))
                    return $"Error: '{qtyStrings[i]}' is not a valid quantity";
            }

            if (names.Length != qtys.Length)
                return $"Error: drawing names count ({names.Length}) does not match quantities count ({qtys.Length})";

            var items = new List<NestItem>();

            for (var i = 0; i < names.Length; i++)
            {
                var drawing = _session.GetDrawing(names[i]);
                if (drawing == null)
                    return $"Error: drawing '{names[i]}' not found";

                items.Add(new NestItem { Drawing = drawing, Quantity = qtys[i] });
            }

            var engine = NestEngineRegistry.Create(plate);
            var nestParts = engine.Nest(items, null, CancellationToken.None);
            plate.Parts.AddRange(nestParts);
            var totalPlaced = nestParts.Count;

            var sb = new StringBuilder();
            sb.AppendLine($"AutoNest plate {plateIndex} ({engine.Name} engine): {(totalPlaced > 0 ? "success" : "no parts placed")}");
            sb.AppendLine($"  Parts placed: {totalPlaced}");
            sb.AppendLine($"  Total parts: {plate.Parts.Count}");
            sb.AppendLine($"  Utilization: {plate.Utilization():P1}");

            var groups = plate.Parts.GroupBy(p => p.BaseDrawing.Name);
            foreach (var group in groups)
                sb.AppendLine($"  {group.Key}: {group.Count()}");

            return sb.ToString();
        }
    }
}
