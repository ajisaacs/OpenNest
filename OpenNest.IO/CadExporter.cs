using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using CSMath;
using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.IO.Bending;
using OpenNest.Math;
using AcadLayer = ACadSharp.Tables.Layer;
using GeoEntity = OpenNest.Geometry.Entity;

namespace OpenNest.IO
{
    /// <summary>
    /// Writes a drawing's source entities and bends as DXF, DWG or SVG, so that
    /// <see cref="CadImporter"/> reads the same geometry and bends back. Each entity keeps its
    /// layer; bends go on <c>BEND</c> as center lines with a note in the form the bend detector
    /// reads. Bend-source lines are written only as their bends.
    /// </summary>
    public static class CadExporter
    {
        public const string BendLayerName = "BEND";

        /// <summary>
        /// The DWG versions that read back as written. ACadSharp cannot write DWG 2007 (AC1021),
        /// and its DWG 2000 (AC1015) and R14 (AC1014) output loses entities and their layers.
        /// </summary>
        public static readonly IReadOnlyList<ACadVersion> DwgVersions = new[]
        {
            ACadVersion.AC1032,
            ACadVersion.AC1027,
            ACadVersion.AC1024,
            ACadVersion.AC1018,
        };

        // Characters AutoCAD does not allow in a layer name.
        private static readonly char[] InvalidLayerCharacters = "<>/\\\":;?*|=,`".ToCharArray();

        /// <summary>
        /// Writes <paramref name="path"/> in the format its extension names: .svg, .dwg or
        /// otherwise DXF. <paramref name="version"/> applies to DXF and DWG.
        /// </summary>
        public static void Export(
            string path,
            IEnumerable<GeoEntity> entities,
            IEnumerable<Bend> bends,
            Units units,
            ACadVersion version = ACadVersion.AC1032
        )
        {
            var bendList = bends?.ToList() ?? new List<Bend>();
            var written = Written(entities, bendList);

            if (Svg.IsSvg(path))
            {
                Svg.ExportEntities(written, bendList, path, units);
                return;
            }

            var dwg = Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase);
            if (dwg && !DwgVersions.Contains(version))
                throw new NotSupportedException($"DWG {version} cannot be written.");

            var document = BuildDocument(written, bendList, units, version);
            using var stream = File.Create(path);
            if (dwg)
            {
                using var writer = new DwgWriter(stream, document);
                writer.Write();
            }
            else
            {
                using var writer = new DxfWriter(stream, document, false);
                writer.Write();
            }
        }

        /// <summary>Builds the CAD document <see cref="Export"/> writes as DXF or DWG.</summary>
        public static CadDocument BuildDocument(
            IEnumerable<GeoEntity> entities,
            IEnumerable<Bend> bends,
            Units units,
            ACadVersion version = ACadVersion.AC1032
        )
        {
            var bendList = bends?.ToList() ?? new List<Bend>();
            var document = new CadDocument(version);
            document.Header.InsUnits =
                units == Units.Millimeters ? UnitsType.Millimeters : UnitsType.Inches;

            var layers = new Dictionary<string, AcadLayer>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in Written(entities, bendList))
                Add(document, entity, LayerOf(document, layers, entity.Layer?.Name));

            if (bendList.Count == 0)
                return document;

            var bendLayer = LayerOf(document, layers, BendLayerName);
            var center = CenterLineType(document, units);
            var height = BendNote.Height(units);

            foreach (var bend in bendList)
            {
                document.Entities.Add(
                    new ACadSharp.Entities.Line
                    {
                        StartPoint = Xyz(bend.StartPoint),
                        EndPoint = Xyz(bend.EndPoint),
                        Layer = bendLayer,
                        LineType = center,
                    }
                );

                var note = BendNote.Text(bend);
                if (note == null)
                    continue;

                var (position, angle) = BendNote.Placement(bend, height);
                document.Entities.Add(
                    new MText
                    {
                        InsertPoint = Xyz(position),
                        Value = IsUnicode(version) ? note : EscapeNonAscii(note),
                        Height = height,
                        // MText takes its rotation as the direction of its X axis.
                        AlignmentPoint = new XYZ(PortableMath.Cos(angle), PortableMath.Sin(angle), 0),
                        AttachmentPoint = AttachmentPointType.BottomCenter,
                        Layer = bendLayer,
                    }
                );
            }

            return document;
        }

        // Rapids are not drawing geometry, and a line picked as a bend is written as that bend.
        private static List<GeoEntity> Written(IEnumerable<GeoEntity> entities, List<Bend> bends)
        {
            var bendSources = new HashSet<GeoEntity>(
                bends.Where(b => b.SourceEntity != null).Select(b => b.SourceEntity)
            );
            return entities
                .Where(e => e.Layer != SpecialLayers.Rapid && !bendSources.Contains(e))
                .ToList();
        }

        private static void Add(CadDocument document, GeoEntity entity, AcadLayer layer)
        {
            switch (entity)
            {
                case Geometry.Line line:
                    document.Entities.Add(
                        new ACadSharp.Entities.Line
                        {
                            StartPoint = Xyz(line.StartPoint),
                            EndPoint = Xyz(line.EndPoint),
                            Layer = layer,
                        }
                    );
                    break;

                case Geometry.Arc arc:
                    var startAngle = arc.StartAngle;
                    var endAngle = arc.EndAngle;
                    if (arc.IsReversed)
                        OpenNest.Math.Generic.Swap(ref startAngle, ref endAngle);
                    document.Entities.Add(
                        new ACadSharp.Entities.Arc
                        {
                            Center = Xyz(arc.Center),
                            Radius = arc.Radius,
                            StartAngle = startAngle,
                            EndAngle = endAngle,
                            Layer = layer,
                        }
                    );
                    break;

                case Geometry.Circle circle:
                    document.Entities.Add(
                        new ACadSharp.Entities.Circle
                        {
                            Center = Xyz(circle.Center),
                            Radius = circle.Radius,
                            Layer = layer,
                        }
                    );
                    break;

                case Geometry.Shape shape:
                    foreach (var child in shape.Entities)
                        Add(document, child, layer);
                    break;
            }
        }

        private static AcadLayer LayerOf(
            CadDocument document,
            Dictionary<string, AcadLayer> layers,
            string name
        )
        {
            name = LayerName(name);
            if (layers.TryGetValue(name, out var layer))
                return layer;

            if (!document.Layers.TryGetValue(name, out layer))
            {
                layer = new AcadLayer(name) { Color = LayerColor(name) };
                document.Layers.Add(layer);
            }

            layers.Add(name, layer);
            return layer;
        }

        private static string LayerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return AcadLayer.DefaultName;

            var characters = name.Trim().ToCharArray();
            for (var i = 0; i < characters.Length; i++)
            {
                if (Array.IndexOf(InvalidLayerCharacters, characters[i]) >= 0 || char.IsControl(characters[i]))
                    characters[i] = '_';
            }
            return new string(characters);
        }

        private static Color LayerColor(string name)
        {
            if (name.Equals(BendLayerName, StringComparison.OrdinalIgnoreCase))
                return new Color(2);
            if (name.Equals(Bend.EtchLayer.Name, StringComparison.OrdinalIgnoreCase))
                return new Color(3);
            if (name.Equals(SpecialLayers.Scribe.Name, StringComparison.OrdinalIgnoreCase))
                return new Color(6);
            return new Color(7);
        }

        // AutoCAD's CENTER pattern, scaled for millimeter drawings like its ISO linetypes.
        private static LineType CenterLineType(CadDocument document, Units units)
        {
            const string name = "CENTER";
            if (document.LineTypes.TryGetValue(name, out var existing))
                return existing;

            var scale = units == Units.Millimeters ? 25.4 : 1.0;
            var lineType = new LineType(name) { Description = "Center ____ _ ____ _ ____" };
            foreach (var length in new[] { 1.25, -0.25, 0.25, -0.25 })
                lineType.AddSegment(new LineType.Segment { Length = length * scale });
            document.LineTypes.Add(lineType);
            return lineType;
        }

        // Files before 2007 are not Unicode, and ACadSharp reads their non-ASCII text back
        // wrongly, so notes there use MText's \U+XXXX escapes, which the bend detector decodes.
        private static bool IsUnicode(ACadVersion version) =>
            version is ACadVersion.AC1021 or ACadVersion.AC1024 or ACadVersion.AC1027 or ACadVersion.AC1032;

        private static string EscapeNonAscii(string text)
        {
            var escaped = new System.Text.StringBuilder(text.Length);
            foreach (var character in text)
            {
                if (character < 128)
                    escaped.Append(character);
                else
                    escaped.Append($"\\U+{(int)character:X4}");
            }
            return escaped.ToString();
        }

        private static XYZ Xyz(Vector point) => new XYZ(point.X, point.Y, 0);
    }
}
