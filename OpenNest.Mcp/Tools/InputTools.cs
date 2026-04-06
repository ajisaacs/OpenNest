using ModelContextProtocol.Server;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Shapes;
using System.ComponentModel;
using System.IO;
using System.Text;
using CncProgram = OpenNest.CNC.Program;

namespace OpenNest.Mcp.Tools
{
    [McpServerToolType]
    public class InputTools
    {
        private readonly NestSession _session;

        public InputTools(NestSession session)
        {
            _session = session;
        }

        [McpServerTool(Name = "load_nest")]
        [Description("Load a .nest file into the session. Returns a summary of plates, parts, and drawings.")]
        public string LoadNest([Description("Absolute path to the .nest file")] string path)
        {
            if (!File.Exists(path))
                return $"Error: file not found: {path}";

            var reader = new NestReader(path);
            var nest = reader.Read();
            _session.Nest = nest;

            var sb = new StringBuilder();
            sb.AppendLine($"Loaded nest: {nest.Name}");
            sb.AppendLine($"Units: {nest.Units}");
            sb.AppendLine($"Plates: {nest.Plates.Count}");

            for (var i = 0; i < nest.Plates.Count; i++)
            {
                var plate = nest.Plates[i];
                var work = plate.WorkArea();
                sb.AppendLine($"  Plate {i}: {plate.Size.Width:F1} x {plate.Size.Length:F1}, " +
                              $"parts={plate.Parts.Count}, " +
                              $"utilization={plate.Utilization():P1}, " +
                              $"work area={work.Width:F1} x {work.Length:F1}");
            }

            sb.AppendLine($"Drawings: {nest.Drawings.Count}");

            foreach (var dwg in nest.Drawings)
            {
                var bbox = dwg.Program.BoundingBox();
                sb.AppendLine($"  {dwg.Name}: bbox={bbox.Width:F2} x {bbox.Length:F2}, " +
                              $"required={dwg.Quantity.Required}, nested={dwg.Quantity.Nested}");
            }

            return sb.ToString();
        }

        [McpServerTool(Name = "save_nest")]
        [Description("Save the current session (all drawings and plates) to a .nest file.")]
        public string SaveNest(
            [Description("Absolute path for the output .nest file")] string path,
            [Description("Name for the nest (optional)")] string name = null)
        {
            var nest = new Nest();
            nest.Name = name ?? Path.GetFileNameWithoutExtension(path);

            foreach (var drawing in _session.AllDrawings())
                nest.Drawings.Add(drawing);

            foreach (var plate in _session.AllPlates())
                nest.Plates.Add(plate);

            if (nest.Drawings.Count == 0)
                return "Error: no drawings in session to save";

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var writer = new NestWriter(nest);
            if (!writer.Write(path))
                return "Error: failed to write nest file";

            return $"Saved nest to {path}\n  Drawings: {nest.Drawings.Count}\n  Plates: {nest.Plates.Count}";
        }

        [McpServerTool(Name = "import_dxf")]
        [Description("Import a DXF file as a new drawing. Returns drawing name and bounding box.")]
        public string ImportDxf(
            [Description("Absolute path to the DXF file")] string path,
            [Description("Name for the drawing (defaults to filename without extension)")] string name = null)
        {
            if (!File.Exists(path))
                return $"Error: file not found: {path}";

            var geometry = Dxf.GetGeometry(path);

            if (geometry.Count == 0)
                return "Error: failed to read DXF file or no geometry found";

            var normalized = ShapeProfile.NormalizeEntities(geometry);
            var pgm = ConvertGeometry.ToProgram(normalized);

            if (pgm == null)
                return "Error: failed to convert geometry to program";

            var drawingName = name ?? Path.GetFileNameWithoutExtension(path);
            var drawing = new Drawing(drawingName, pgm);
            drawing.Color = Drawing.GetNextColor();
            _session.Drawings.Add(drawing);

            var bbox = pgm.BoundingBox();
            return $"Imported drawing '{drawingName}': bbox={bbox.Width:F2} x {bbox.Length:F2}";
        }

        [McpServerTool(Name = "create_drawing")]
        [Description("Create a drawing from a built-in shape or G-code string. Shape can be: rectangle, circle, l_shape, t_shape, gcode.")]
        public string CreateDrawing(
            [Description("Name for the drawing")] string name,
            [Description("Shape type: rectangle, circle, l_shape, t_shape, gcode")] string shape,
            [Description("Width of the shape (not used for circle or gcode)")] double width = 10,
            [Description("Length of the shape (not used for circle or gcode)")] double length = 10,
            [Description("Radius for circle shape")] double radius = 5,
            [Description("G-code string (only used when shape is 'gcode')")] string gcode = null)
        {
            ShapeDefinition shapeDef;

            switch (shape.ToLower())
            {
                case "rectangle":
                    shapeDef = new RectangleShape { Name = name, Width = width, Length = length };
                    break;

                case "circle":
                    shapeDef = new CircleShape { Name = name, Diameter = radius * 2 };
                    break;

                case "l_shape":
                    shapeDef = new LShape { Name = name, Width = width, Height = length };
                    break;

                case "t_shape":
                    shapeDef = new TShape { Name = name, Width = width, Height = length };
                    break;

                case "gcode":
                    if (string.IsNullOrWhiteSpace(gcode))
                        return "Error: gcode parameter is required when shape is 'gcode'";
                    var pgm = ParseGcode(gcode);
                    if (pgm == null)
                        return "Error: failed to parse G-code";
                    var gcodeDrawing = new Drawing(name, pgm);
                    gcodeDrawing.Color = Drawing.GetNextColor();
                    _session.Drawings.Add(gcodeDrawing);
                    var gcodeBbox = pgm.BoundingBox();
                    return $"Created drawing '{name}': bbox={gcodeBbox.Width:F2} x {gcodeBbox.Length:F2}";

                default:
                    return $"Error: unknown shape '{shape}'. Use: rectangle, circle, l_shape, t_shape, gcode";
            }

            var drawing = shapeDef.GetDrawing();
            drawing.Color = Drawing.GetNextColor();
            _session.Drawings.Add(drawing);

            var bbox = drawing.Program.BoundingBox();
            return $"Created drawing '{name}': bbox={bbox.Width:F2} x {bbox.Length:F2}";
        }

        private static CncProgram ParseGcode(string gcode)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(gcode));
            var reader = new ProgramReader(stream);
            var pgm = reader.Read();
            reader.Close();
            return pgm;
        }
    }
}
