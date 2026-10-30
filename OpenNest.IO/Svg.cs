using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.IO
{
    /// <summary>
    /// SVG import and export. Unlike a DXF, an SVG's numbers are not drawing units, so both
    /// directions take the <see cref="Units"/> of the drawing; see docs/svg-import-export.md.
    /// </summary>
    public static class Svg
    {
        internal const string PlateLayerName = "PLATE";

        private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

        #region Import

        public static bool IsSvg(string path) =>
            Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Imports an SVG in <see cref="CadImportOptions.DrawingUnits"/>. Throws
        /// <see cref="InvalidDataException"/> when the file does not state its real-world size
        /// and <see cref="CadImportOptions.SvgUserUnitsPerInch"/> is not set: a guessed scale
        /// would silently produce wrong-size parts.
        /// </summary>
        public static DxfImportResult Import(string path, CadImportOptions options) =>
            SvgReader.Read(path, options);

        /// <summary>
        /// The file's width and height in its own numbers when it does not state its
        /// real-world size, or null when it does and no scale is needed.
        /// </summary>
        public static (double Width, double Height)? UndeclaredSize(string path) =>
            SvgReader.UndeclaredSize(path);

        #endregion

        #region Export

        public static void ExportProgram(Program program, string path, Units units)
        {
            using var stream = File.Create(path);
            ExportProgram(program, stream, units);
        }

        public static void ExportProgram(Program program, Stream stream, Units units) =>
            ExportEntities(ConvertProgram.ToGeometry(program), stream, units);

        public static void ExportPlate(Plate plate, string path, Units units)
        {
            using var stream = File.Create(path);
            ExportPlate(plate, stream, units);
        }

        public static void ExportPlate(Plate plate, Stream stream, Units units)
        {
            var entities = PlateOutline(plate);

            foreach (var part in plate.Parts)
            {
                var geometry = ConvertProgram.ToGeometry(part.Program);
                foreach (var entity in geometry)
                    entity.Offset(part.Location);
                entities.AddRange(geometry);
            }

            ExportEntities(entities, stream, units);
        }

        /// <summary>
        /// Writes entities as one group per layer, with the document's physical size stated in
        /// <paramref name="units"/> so one user unit is exactly one drawing unit.
        /// </summary>
        public static void ExportEntities(IEnumerable<Entity> entities, string path, Units units)
        {
            using var stream = File.Create(path);
            ExportEntities(entities, stream, units);
        }

        public static void ExportEntities(IEnumerable<Entity> entities, Stream stream, Units units)
        {
            var list = entities.ToList();
            var bounds = list.Count > 0 ? list.GetBoundingBox() : new Box(0, 0, 1, 1);
            // Half of each stroke falls outside the outermost geometry; keep it on the page.
            var margin = System.Math.Max(System.Math.Max(bounds.Length, bounds.Width), 1) * 0.01;

            var left = bounds.Left - margin;
            var top = -bounds.Top - margin;
            var width = bounds.Length + margin * 2;
            var height = bounds.Width + margin * 2;
            var unit = UnitsHelper.GetShortString(units);

            var root = new XElement(
                Ns + "svg",
                new XAttribute("version", "1.1"),
                new XAttribute("width", Number(width) + unit),
                new XAttribute("height", Number(height) + unit),
                new XAttribute(
                    "viewBox",
                    $"{Number(left)} {Number(top)} {Number(width)} {Number(height)}"
                ),
                new XAttribute("fill", "none")
            );

            foreach (var layer in list.GroupBy(e => e.Layer?.Name ?? Layer.Default.Name))
                root.Add(LayerGroup(layer.Key, layer.ToList()));

            new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(stream);
        }

        private static XElement LayerGroup(string name, List<Entity> entities)
        {
            var group = new XElement(
                Ns + "g",
                new XAttribute("data-layer", name),
                new XAttribute("stroke", StrokeOf(name)),
                new XAttribute("stroke-width", "1"),
                new XAttribute("vector-effect", "non-scaling-stroke")
            );

            var data = new StringBuilder();
            var position = Vector.Invalid;

            foreach (var entity in entities)
            {
                switch (entity)
                {
                    case Line line:
                        MoveTo(data, line.StartPoint, ref position);
                        data.Append($"L{Point(line.EndPoint)}");
                        position = line.EndPoint;
                        break;

                    case Arc arc when arc.StartPoint().DistanceTo(arc.EndPoint()) < Tolerance.Epsilon:
                        group.Add(CircleElement(arc.Center, arc.Radius));
                        break;

                    case Arc arc:
                        MoveTo(data, arc.StartPoint(), ref position);
                        var large = arc.SweepAngle() > System.Math.PI ? 1 : 0;
                        // The Y flip keeps how an arc looks, and SVG's positive sweep is clockwise.
                        var sweep = arc.IsReversed ? 1 : 0;
                        var radius = Number(arc.Radius);
                        data.Append($"A{radius} {radius} 0 {large} {sweep} {Point(arc.EndPoint())}");
                        position = arc.EndPoint();
                        break;

                    case Circle circle:
                        group.Add(CircleElement(circle.Center, circle.Radius));
                        break;
                }
            }

            if (data.Length > 0)
                group.Add(new XElement(Ns + "path", new XAttribute("d", data.ToString())));

            return group;
        }

        private static void MoveTo(StringBuilder data, Vector point, ref Vector position)
        {
            if (!position.IsValid() || position.DistanceTo(point) >= Tolerance.Epsilon)
                data.Append($"M{Point(point)}");
            position = point;
        }

        private static XElement CircleElement(Vector center, double radius) =>
            new XElement(
                Ns + "circle",
                new XAttribute("cx", Number(center.X)),
                new XAttribute("cy", Number(-center.Y)),
                new XAttribute("r", Number(radius))
            );

        private static string StrokeOf(string layer)
        {
            if (string.Equals(layer, SpecialLayers.Rapid.Name, StringComparison.OrdinalIgnoreCase))
                return "#808080";
            if (string.Equals(layer, SpecialLayers.Scribe.Name, StringComparison.OrdinalIgnoreCase))
                return "#c000c0";
            if (string.Equals(layer, PlateLayerName, StringComparison.OrdinalIgnoreCase))
                return "#0080c0";
            return "#000000";
        }

        private static string Point(Vector point) => $"{Number(point.X)} {Number(-point.Y)}";

        private static string Number(double value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

        private static List<Entity> PlateOutline(Plate plate)
        {
            var entities = new List<Entity>();
            if (plate.Quadrant < 1 || plate.Quadrant > 4)
                return entities;

            var signX = plate.Quadrant is 1 or 4 ? 1 : -1;
            var signY = plate.Quadrant is 1 or 2 ? 1 : -1;
            var length = plate.Size.Length * signX;
            var width = plate.Size.Width * signY;
            var layer = new Layer(PlateLayerName) { IsVisible = true };

            void Rectangle(double x1, double y1, double x2, double y2)
            {
                var corners = new[]
                {
                    new Vector(x1, y1),
                    new Vector(x1, y2),
                    new Vector(x2, y2),
                    new Vector(x2, y1),
                };
                for (var i = 0; i < corners.Length; i++)
                    entities.Add(
                        new Line(corners[i], corners[(i + 1) % corners.Length]) { Layer = layer }
                    );
            }

            Rectangle(0, 0, length, width);
            Rectangle(
                plate.EdgeSpacing.Left,
                plate.EdgeSpacing.Bottom,
                length - plate.EdgeSpacing.Right,
                width - plate.EdgeSpacing.Top
            );

            return entities;
        }

        #endregion
    }
}
