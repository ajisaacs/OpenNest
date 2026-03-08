using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using OpenNest.Geometry;

namespace OpenNest.Mcp.Tools
{
    public class SetupTools
    {
        private readonly NestSession _session;

        public SetupTools(NestSession session)
        {
            _session = session;
        }

        [McpServerTool(Name = "create_plate")]
        [Description("Create a new plate with the given dimensions and spacing. Returns plate index and work area.")]
        public string CreatePlate(
            [Description("Plate width")] double width,
            [Description("Plate height")] double height,
            [Description("Spacing between parts (default 0)")] double partSpacing = 0,
            [Description("Edge spacing on all sides (default 0)")] double edgeSpacing = 0,
            [Description("Quadrant 1-4 (default 1). 1=TopRight, 2=TopLeft, 3=BottomLeft, 4=BottomRight")] int quadrant = 1,
            [Description("Material name (optional)")] string material = null)
        {
            var plate = new Plate(width, height);
            plate.PartSpacing = partSpacing;
            plate.EdgeSpacing = new Spacing(edgeSpacing, edgeSpacing);
            plate.Quadrant = quadrant;
            plate.Quantity = 1;

            if (!string.IsNullOrEmpty(material))
                plate.Material.Name = material;

            _session.Plates.Add(plate);

            var allPlates = _session.AllPlates();
            var index = allPlates.Count - 1;
            var work = plate.WorkArea();

            var sb = new StringBuilder();
            sb.AppendLine($"Created plate {index}: {plate.Size.Width:F1} x {plate.Size.Height:F1}");
            sb.AppendLine($"  Quadrant: {plate.Quadrant}");
            sb.AppendLine($"  Part spacing: {plate.PartSpacing:F2}");
            sb.AppendLine($"  Edge spacing: L={plate.EdgeSpacing.Left:F2} B={plate.EdgeSpacing.Bottom:F2} R={plate.EdgeSpacing.Right:F2} T={plate.EdgeSpacing.Top:F2}");
            sb.AppendLine($"  Work area: {work.Width:F1} x {work.Height:F1}");

            return sb.ToString();
        }

        [McpServerTool(Name = "clear_plate")]
        [Description("Remove all parts from a plate. Returns how many parts were removed.")]
        public string ClearPlate(
            [Description("Index of the plate to clear")] int plateIndex)
        {
            var plate = _session.GetPlate(plateIndex);

            if (plate == null)
                return $"Error: plate {plateIndex} not found";

            var count = plate.Parts.Count;
            plate.Parts.Clear();

            return $"Cleared plate {plateIndex}: removed {count} parts";
        }
    }
}
