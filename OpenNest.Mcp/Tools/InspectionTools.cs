using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using ModelContextProtocol.Server;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Mcp.Tools
{
    [McpServerToolType]
    public class InspectionTools
    {
        private readonly NestSession _session;

        public InspectionTools(NestSession session)
        {
            _session = session;
        }

        [McpServerTool(Name = "get_plate_info")]
        [Description("Get detailed information about a plate including dimensions, part count, utilization, remnants, and drawing breakdown.")]
        public string GetPlateInfo(
            [Description("Index of the plate")] int plateIndex)
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            var work = plate.WorkArea();
            var remnants = plate.GetRemnants();

            var sb = new StringBuilder();
            sb.AppendLine($"Plate {plateIndex}:");
            sb.AppendLine($"  Size: {plate.Size.Width:F1} x {plate.Size.Length:F1}");
            sb.AppendLine($"  Quadrant: {plate.Quadrant}");
            sb.AppendLine($"  Thickness: {plate.Thickness:F2}");
            sb.AppendLine($"  Material: {plate.Material.Name}");
            sb.AppendLine($"  Part spacing: {plate.PartSpacing:F2}");
            sb.AppendLine($"  Edge spacing: L={plate.EdgeSpacing.Left:F2} B={plate.EdgeSpacing.Bottom:F2} R={plate.EdgeSpacing.Right:F2} T={plate.EdgeSpacing.Top:F2}");
            sb.AppendLine($"  Work area: {work.X:F1},{work.Y:F1} {work.Width:F1}x{work.Length:F1}");
            sb.AppendLine($"  Parts: {plate.Parts.Count}");
            sb.AppendLine($"  Utilization: {plate.Utilization():P1}");
            sb.AppendLine($"  Quantity: {plate.Quantity}");

            // Drawing breakdown
            if (plate.Parts.Count > 0)
            {
                sb.AppendLine("  Drawings:");
                var groups = plate.Parts.GroupBy(p => p.BaseDrawing.Name);
                foreach (var group in groups)
                    sb.AppendLine($"    {group.Key}: {group.Count()}");
            }

            // Remnants
            sb.AppendLine($"  Remnants: {remnants.Count}");
            for (var i = 0; i < remnants.Count; i++)
            {
                var r = remnants[i];
                sb.AppendLine($"    Remnant {i}: ({r.X:F1},{r.Y:F1}) {r.Width:F1}x{r.Length:F1}, area={r.Area():F1}");
            }

            return sb.ToString();
        }

        [McpServerTool(Name = "get_parts")]
        [Description("List placed parts on a plate with index, drawing name, location, rotation, and bounding box.")]
        public string GetParts(
            [Description("Index of the plate")] int plateIndex,
            [Description("Maximum number of parts to list (default 50)")] int limit = 50)
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            if (plate.Parts.Count == 0)
                return $"Plate {plateIndex} has no parts";

            var sb = new StringBuilder();
            sb.AppendLine($"Plate {plateIndex}: {plate.Parts.Count} parts (showing up to {limit})");

            var count = System.Math.Min(plate.Parts.Count, limit);

            for (var i = 0; i < count; i++)
            {
                var part = plate.Parts[i];
                var bbox = part.BoundingBox;
                var rotDeg = Angle.ToDegrees(part.Rotation);

                sb.AppendLine($"  [{i}] {part.BaseDrawing.Name}: " +
                              $"loc=({part.Location.X:F2},{part.Location.Y:F2}), " +
                              $"rot={rotDeg:F1} deg, " +
                              $"bbox=({bbox.X:F2},{bbox.Y:F2} {bbox.Width:F2}x{bbox.Length:F2})");
            }

            if (plate.Parts.Count > limit)
                sb.AppendLine($"  ... and {plate.Parts.Count - limit} more");

            return sb.ToString();
        }

        [McpServerTool(Name = "check_overlaps")]
        [Description("Check a plate for overlapping parts. Reports collision points if any.")]
        public string CheckOverlaps(
            [Description("Index of the plate")] int plateIndex)
        {
            var plate = _session.GetPlate(plateIndex);
            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            if (plate.Parts.Count < 2)
                return $"Plate {plateIndex}: no overlaps possible (fewer than 2 parts)";

            var hasOverlaps = plate.HasOverlappingParts(out var pts);

            if (!hasOverlaps)
                return $"Plate {plateIndex}: no overlapping parts detected";

            var sb = new StringBuilder();
            sb.AppendLine($"Plate {plateIndex}: {pts.Count} collision point(s) detected!");

            var limit = System.Math.Min(pts.Count, 20);

            for (var i = 0; i < limit; i++)
            {
                var pt = pts[i];
                sb.AppendLine($"  Collision at ({pt.X:F2}, {pt.Y:F2})");
            }

            if (pts.Count > limit)
                sb.AppendLine($"  ... and {pts.Count - limit} more collision points");

            return sb.ToString();
        }
    }
}
