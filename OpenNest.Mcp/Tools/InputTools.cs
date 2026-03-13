using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using ModelContextProtocol.Server;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
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
        [Description("Load a .nest zip file into the session. Returns a summary of plates, parts, and drawings.")]
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

        [McpServerTool(Name = "import_dxf")]
        [Description("Import a DXF file as a new drawing. Returns drawing name and bounding box.")]
        public string ImportDxf(
            [Description("Absolute path to the DXF file")] string path,
            [Description("Name for the drawing (defaults to filename without extension)")] string name = null)
        {
            if (!File.Exists(path))
                return $"Error: file not found: {path}";

            var importer = new DxfImporter();

            if (!importer.GetGeometry(path, out var geometry))
                return "Error: failed to read DXF file";

            if (geometry.Count == 0)
                return "Error: no geometry found in DXF file";

            var pgm = ConvertGeometry.ToProgram(geometry);

            if (pgm == null)
                return "Error: failed to convert geometry to program";

            var drawingName = name ?? Path.GetFileNameWithoutExtension(path);
            var drawing = new Drawing(drawingName, pgm);
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
            [Description("Height of the shape (not used for circle or gcode)")] double height = 10,
            [Description("Radius for circle shape")] double radius = 5,
            [Description("G-code string (only used when shape is 'gcode')")] string gcode = null)
        {
            CncProgram pgm;

            switch (shape.ToLower())
            {
                case "rectangle":
                    pgm = CreateRectangle(width, height);
                    break;

                case "circle":
                    pgm = CreateCircle(radius);
                    break;

                case "l_shape":
                    pgm = CreateLShape(width, height);
                    break;

                case "t_shape":
                    pgm = CreateTShape(width, height);
                    break;

                case "gcode":
                    if (string.IsNullOrWhiteSpace(gcode))
                        return "Error: gcode parameter is required when shape is 'gcode'";
                    pgm = ParseGcode(gcode);
                    if (pgm == null)
                        return "Error: failed to parse G-code";
                    break;

                default:
                    return $"Error: unknown shape '{shape}'. Use: rectangle, circle, l_shape, t_shape, gcode";
            }

            var drawing = new Drawing(name, pgm);
            _session.Drawings.Add(drawing);

            var bbox = pgm.BoundingBox();
            return $"Created drawing '{name}': bbox={bbox.Width:F2} x {bbox.Length:F2}";
        }

        private static CncProgram CreateRectangle(double width, double height)
        {
            var entities = new List<Entity>
            {
                new Line(0, 0, width, 0),
                new Line(width, 0, width, height),
                new Line(width, height, 0, height),
                new Line(0, height, 0, 0)
            };

            return ConvertGeometry.ToProgram(entities);
        }

        private static CncProgram CreateCircle(double radius)
        {
            var entities = new List<Entity>
            {
                new Circle(0, 0, radius)
            };

            return ConvertGeometry.ToProgram(entities);
        }

        private static CncProgram CreateLShape(double width, double height)
        {
            var hw = width / 2;
            var hh = height / 2;

            var entities = new List<Entity>
            {
                new Line(0, 0, width, 0),
                new Line(width, 0, width, hh),
                new Line(width, hh, hw, hh),
                new Line(hw, hh, hw, height),
                new Line(hw, height, 0, height),
                new Line(0, height, 0, 0)
            };

            return ConvertGeometry.ToProgram(entities);
        }

        private static CncProgram CreateTShape(double width, double height)
        {
            var stemWidth = width / 3;
            var topHeight = height / 3;
            var stemLeft = (width - stemWidth) / 2;
            var stemRight = stemLeft + stemWidth;
            var stemBottom = 0.0;
            var stemTop = height - topHeight;

            var entities = new List<Entity>
            {
                new Line(stemLeft, stemBottom, stemRight, stemBottom),
                new Line(stemRight, stemBottom, stemRight, stemTop),
                new Line(stemRight, stemTop, width, stemTop),
                new Line(width, stemTop, width, height),
                new Line(width, height, 0, height),
                new Line(0, height, 0, stemTop),
                new Line(0, stemTop, stemLeft, stemTop),
                new Line(stemLeft, stemTop, stemLeft, stemBottom)
            };

            return ConvertGeometry.ToProgram(entities);
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
