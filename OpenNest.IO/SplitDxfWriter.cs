using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using OpenNest.Bending;
using OpenNest.Converters;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.IO;

// Disambiguate Entity — both ACadSharp.Entities and OpenNest.Geometry define it
using GeoEntity = OpenNest.Geometry.Entity;

namespace OpenNest.IO
{
    public class SplitDxfWriter
    {
        private const double DefaultEtchLength = 1.0;

        public double EtchLength { get; set; } = DefaultEtchLength;

        public void Write(string path, Drawing drawing)
        {
            var doc = new CadDocument();

            var cutLayer = new ACadSharp.Tables.Layer("CUT") { Color = new Color(7) };
            var bendLayer = new ACadSharp.Tables.Layer("BEND") { Color = new Color(2) };
            var etchLayer = new ACadSharp.Tables.Layer("ETCH") { Color = new Color(3) };
            doc.Layers.Add(cutLayer);
            doc.Layers.Add(bendLayer);
            doc.Layers.Add(etchLayer);

            var centerLineType = new LineType("CENTERX2");
            doc.LineTypes.Add(centerLineType);

            WriteProgramEntities(doc, drawing.Program, cutLayer);

            if (drawing.Bends != null)
            {
                foreach (var bend in drawing.Bends)
                {
                    WriteBendLine(doc, bend, bendLayer, centerLineType);
                    WriteEtchLines(doc, bend, etchLayer);
                }
            }

            using var stream = File.Create(path);
            using var writer = new DxfWriter(stream, doc, false);
            writer.Write();
        }

        private static void WriteProgramEntities(CadDocument doc, CNC.Program program, ACadSharp.Tables.Layer layer)
        {
            var geometry = ConvertProgram.ToGeometry(program);
            WriteGeometryEntities(doc, geometry, layer);
        }

        private static void WriteGeometryEntities(CadDocument doc, List<GeoEntity> geometry, ACadSharp.Tables.Layer layer)
        {
            foreach (var entity in geometry)
            {
                // Skip rapid moves
                if (entity.Layer == SpecialLayers.Rapid)
                    continue;

                switch (entity)
                {
                    case OpenNest.Geometry.Line line:
                        doc.Entities.Add(new ACadSharp.Entities.Line
                        {
                            StartPoint = new XYZ(line.StartPoint.X, line.StartPoint.Y, 0),
                            EndPoint = new XYZ(line.EndPoint.X, line.EndPoint.Y, 0),
                            Layer = layer
                        });
                        break;

                    case OpenNest.Geometry.Arc arc:
                        var startAngle = arc.StartAngle;
                        var endAngle = arc.EndAngle;
                        if (arc.IsReversed)
                            OpenNest.Math.Generic.Swap(ref startAngle, ref endAngle);

                        doc.Entities.Add(new ACadSharp.Entities.Arc
                        {
                            Center = new XYZ(arc.Center.X, arc.Center.Y, 0),
                            Radius = arc.Radius,
                            StartAngle = startAngle,
                            EndAngle = endAngle,
                            Layer = layer
                        });
                        break;

                    case OpenNest.Geometry.Circle circle:
                        doc.Entities.Add(new ACadSharp.Entities.Circle
                        {
                            Center = new XYZ(circle.Center.X, circle.Center.Y, 0),
                            Radius = circle.Radius,
                            Layer = layer
                        });
                        break;

                    case OpenNest.Geometry.Shape shape:
                        WriteGeometryEntities(doc, shape.Entities, layer);
                        break;
                }
            }
        }

        private static void WriteBendLine(CadDocument doc, Bend bend, ACadSharp.Tables.Layer layer, LineType lineType)
        {
            var line = new ACadSharp.Entities.Line
            {
                StartPoint = new XYZ(bend.StartPoint.X, bend.StartPoint.Y, 0),
                EndPoint = new XYZ(bend.EndPoint.X, bend.EndPoint.Y, 0),
                Layer = layer,
                LineType = lineType
            };
            doc.Entities.Add(line);

            if (!string.IsNullOrEmpty(bend.NoteText))
            {
                var midX = (bend.StartPoint.X + bend.EndPoint.X) / 2;
                var midY = (bend.StartPoint.Y + bend.EndPoint.Y) / 2;

                var mtext = new MText
                {
                    InsertPoint = new XYZ(midX, midY + 0.5, 0),
                    Value = bend.NoteText,
                    Height = 0.1,
                    Layer = layer
                };
                doc.Entities.Add(mtext);
            }
        }

        private void WriteEtchLines(CadDocument doc, Bend bend, ACadSharp.Tables.Layer layer)
        {
            if (bend.Direction != BendDirection.Up)
                return;

            var start = bend.StartPoint;
            var end = bend.EndPoint;
            var length = bend.Length;

            if (length < EtchLength * 3.0)
            {
                doc.Entities.Add(new ACadSharp.Entities.Line
                {
                    StartPoint = new XYZ(start.X, start.Y, 0),
                    EndPoint = new XYZ(end.X, end.Y, 0),
                    Layer = layer
                });
            }
            else
            {
                var angle = start.AngleTo(end);
                var dx = System.Math.Cos(angle) * EtchLength;
                var dy = System.Math.Sin(angle) * EtchLength;

                doc.Entities.Add(new ACadSharp.Entities.Line
                {
                    StartPoint = new XYZ(start.X, start.Y, 0),
                    EndPoint = new XYZ(start.X + dx, start.Y + dy, 0),
                    Layer = layer
                });

                doc.Entities.Add(new ACadSharp.Entities.Line
                {
                    StartPoint = new XYZ(end.X, end.Y, 0),
                    EndPoint = new XYZ(end.X - dx, end.Y - dy, 0),
                    Layer = layer
                });
            }
        }
    }
}
