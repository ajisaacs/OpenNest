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

            var remnants = plate.GetRemnants();

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

            var fillItems = items
                .Where(i => i.Quantity > 1)
                .OrderBy(i => i.Priority)
                .ThenByDescending(i => i.Drawing.Area)
                .ToList();

            var packItems = items
                .Where(i => i.Quantity == 1)
                .ToList();

            var workArea = plate.WorkArea();
            var totalPlaced = 0;

            // Phase 1: Fill multi-quantity drawings with NestEngine.
            foreach (var item in fillItems)
            {
                if (item.Quantity <= 0 || workArea.Width <= 0 || workArea.Length <= 0)
                    continue;

                var engine = NestEngineRegistry.Create(plate);
                var parts = engine.FillExact(item, workArea, null, CancellationToken.None);

                if (parts.Count > 0)
                {
                    plate.Parts.AddRange(parts);
                    // TODO: Compactor.Compact(parts, plate);
                    item.Quantity = System.Math.Max(0, item.Quantity - parts.Count);
                    totalPlaced += parts.Count;
                    var placedBox = parts.Cast<IBoundable>().GetBoundingBox();
                    workArea = ComputeRemainderWithin(workArea, placedBox, plate.PartSpacing);
                }
            }

            // Phase 2: Pack single-quantity items into remaining space.
            packItems = packItems.Where(i => i.Quantity > 0).ToList();

            if (packItems.Count > 0 && workArea.Width > 0 && workArea.Length > 0)
            {
                var engine = NestEngineRegistry.Create(plate);
                var packParts = engine.PackArea(workArea, packItems, null, CancellationToken.None);
                if (packParts.Count > 0)
                {
                    plate.Parts.AddRange(packParts);
                    totalPlaced += packParts.Count;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"AutoNest plate {plateIndex}: {(totalPlaced > 0 ? "success" : "no parts placed")}");
            sb.AppendLine($"  Parts placed: {totalPlaced}");
            sb.AppendLine($"  Total parts: {plate.Parts.Count}");
            sb.AppendLine($"  Utilization: {plate.Utilization():P1}");

            var groups = plate.Parts.GroupBy(p => p.BaseDrawing.Name);
            foreach (var group in groups)
                sb.AppendLine($"  {group.Key}: {group.Count()}");

            return sb.ToString();
        }

        private static Box ComputeRemainderWithin(Box workArea, Box usedBox, double spacing)
        {
            var hWidth = workArea.Right - usedBox.Right - spacing;
            var hStrip = hWidth > 0
                ? new Box(usedBox.Right + spacing, workArea.Y, hWidth, workArea.Length)
                : Box.Empty;

            var vHeight = workArea.Top - usedBox.Top - spacing;
            var vStrip = vHeight > 0
                ? new Box(workArea.X, usedBox.Top + spacing, workArea.Width, vHeight)
                : Box.Empty;

            return hStrip.Area() >= vStrip.Area() ? hStrip : vStrip;
        }
    }
}
