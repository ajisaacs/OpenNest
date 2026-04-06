using ACadSharp;
using ACadSharp.IO;
using CSMath;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace OpenNest.IO
{
    using AcadArc = ACadSharp.Entities.Arc;
    using AcadCircle = ACadSharp.Entities.Circle;
    using AcadLine = ACadSharp.Entities.Line;
    using Layer = ACadSharp.Tables.Layer;

    public static class Dxf
    {
        #region Import

        /// <summary>
        /// Imports a DXF file, returning both converted entities and the raw CadDocument
        /// for bend detection. The CadDocument is NOT disposed — caller can use it for
        /// additional analysis (e.g., MText extraction for bend notes).
        /// </summary>
        public static DxfImportResult Import(string path)
        {
            using var reader = new DxfReader(path);
            var doc = reader.Read();

            return new DxfImportResult
            {
                Entities = ConvertEntities(doc),
                Document = doc
            };
        }

        public static List<Entity> GetGeometry(string path)
        {
            try
            {
                using var reader = new DxfReader(path);
                var doc = reader.Read();
                return ConvertEntities(doc);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return new List<Entity>();
            }
        }

        public static List<Entity> GetGeometry(Stream stream)
        {
            try
            {
                using var reader = new DxfReader(stream);
                var doc = reader.Read();
                return ConvertEntities(doc);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return new List<Entity>();
            }
        }

        #endregion

        #region Export

        public static void ExportProgram(Program program, string path)
        {
            using var stream = File.Create(path);
            ExportProgram(program, stream);
        }

        public static void ExportProgram(Program program, Stream stream)
        {
            var ctx = new ExportContext();
            ctx.AddProgram(program);

            using var writer = new DxfWriter(stream, ctx.Document, false);
            writer.Write();
        }

        public static void ExportPlate(Plate plate, string path)
        {
            using var stream = File.Create(path);
            ExportPlate(plate, stream);
        }

        public static void ExportPlate(Plate plate, Stream stream)
        {
            var ctx = new ExportContext();
            ctx.AddPlateOutline(plate);

            foreach (var part in plate.Parts)
            {
                var endpt = part.Location.ToAcadXYZ();
                ctx.AddLine(ctx.CurPos, endpt, ctx.RapidLayer);
                ctx.CurPos = part.Location.ToAcadXYZ();
                ctx.AddProgram(part.Program);
            }

            using var writer = new DxfWriter(stream, ctx.Document, false);
            writer.Write();
        }

        #endregion

        #region Private

        private static List<Entity> ConvertEntities(CadDocument doc)
        {
            var entities = new List<Entity>();
            var lines = new List<Line>();
            var arcs = new List<Arc>();

            foreach (var entity in doc.Entities)
            {
                if (IsNonCutLayer(entity.Layer?.Name))
                    continue;

                switch (entity)
                {
                    case ACadSharp.Entities.Line line:
                        lines.Add(line.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Arc arc:
                        arcs.Add(arc.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Circle circle:
                        entities.Add(circle.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Spline spline:
                        foreach (var e in spline.ToOpenNest())
                        {
                            if (e is Line l) lines.Add(l);
                            else if (e is Arc a) arcs.Add(a);
                        }
                        break;

                    case ACadSharp.Entities.LwPolyline lwPolyline:
                        lines.AddRange(lwPolyline.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Polyline polyline:
                        lines.AddRange(polyline.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Ellipse ellipse:
                        foreach (var e in ellipse.ToOpenNest())
                        {
                            if (e is Line l) lines.Add(l);
                            else if (e is Arc a) arcs.Add(a);
                        }
                        break;
                }
            }

            GeometryOptimizer.Optimize(lines);
            GeometryOptimizer.Optimize(arcs);

            entities.AddRange(lines);
            entities.AddRange(arcs);

            return entities;
        }

        private static bool IsNonCutLayer(string layerName)
        {
            return string.Equals(layerName, "BEND", StringComparison.OrdinalIgnoreCase)
                || string.Equals(layerName, "ETCH", StringComparison.OrdinalIgnoreCase);
        }

        private class ExportContext
        {
            public CadDocument Document { get; }
            public XYZ CurPos { get; set; }
            public Layer CutLayer { get; }
            public Layer RapidLayer { get; }
            public Layer PlateLayer { get; }

            private Mode mode;

            public ExportContext()
            {
                Document = new CadDocument();

                CutLayer = new Layer("Cut") { Color = new Color(1) };
                RapidLayer = new Layer("Rapid") { Color = new Color(5) };
                PlateLayer = new Layer("Plate") { Color = new Color(4) };

                Document.Layers.Add(CutLayer);
                Document.Layers.Add(RapidLayer);
                Document.Layers.Add(PlateLayer);
            }

            public void AddLine(XYZ start, XYZ end, Layer layer)
            {
                var ln = new AcadLine
                {
                    StartPoint = start,
                    EndPoint = end,
                    Layer = layer
                };
                Document.Entities.Add(ln);
            }

            public void AddPlateOutline(Plate plate)
            {
                XYZ pt1, pt2, pt3, pt4;

                switch (plate.Quadrant)
                {
                    case 1:
                        pt1 = new XYZ(0, 0, 0);
                        pt2 = new XYZ(0, plate.Size.Width, 0);
                        pt3 = new XYZ(plate.Size.Length, plate.Size.Width, 0);
                        pt4 = new XYZ(plate.Size.Length, 0, 0);
                        break;

                    case 2:
                        pt1 = new XYZ(0, 0, 0);
                        pt2 = new XYZ(0, plate.Size.Width, 0);
                        pt3 = new XYZ(-plate.Size.Length, plate.Size.Width, 0);
                        pt4 = new XYZ(-plate.Size.Length, 0, 0);
                        break;

                    case 3:
                        pt1 = new XYZ(0, 0, 0);
                        pt2 = new XYZ(0, -plate.Size.Width, 0);
                        pt3 = new XYZ(-plate.Size.Length, -plate.Size.Width, 0);
                        pt4 = new XYZ(-plate.Size.Length, 0, 0);
                        break;

                    case 4:
                        pt1 = new XYZ(0, 0, 0);
                        pt2 = new XYZ(0, -plate.Size.Width, 0);
                        pt3 = new XYZ(plate.Size.Length, -plate.Size.Width, 0);
                        pt4 = new XYZ(plate.Size.Length, 0, 0);
                        break;

                    default:
                        return;
                }

                AddLine(pt1, pt2, PlateLayer);
                AddLine(pt2, pt3, PlateLayer);
                AddLine(pt3, pt4, PlateLayer);
                AddLine(pt4, pt1, PlateLayer);

                var m1 = new XYZ(pt1.X + plate.EdgeSpacing.Left, pt1.Y + plate.EdgeSpacing.Bottom, 0);
                var m2 = new XYZ(m1.X, pt2.Y - plate.EdgeSpacing.Top, 0);
                var m3 = new XYZ(pt3.X - plate.EdgeSpacing.Right, m2.Y, 0);
                var m4 = new XYZ(m3.X, m1.Y, 0);

                AddLine(m1, m2, PlateLayer);
                AddLine(m2, m3, PlateLayer);
                AddLine(m3, m4, PlateLayer);
                AddLine(m4, m1, PlateLayer);
            }

            public void AddProgram(Program program)
            {
                mode = program.Mode;

                for (var i = 0; i < program.Length; ++i)
                {
                    var code = program[i];

                    switch (code.Type)
                    {
                        case CodeType.ArcMove:
                            AddArcMove((ArcMove)code);
                            break;

                        case CodeType.LinearMove:
                            AddLinearMove((LinearMove)code);
                            break;

                        case CodeType.RapidMove:
                            AddRapidMove((RapidMove)code);
                            break;

                        case CodeType.SubProgramCall:
                            var tmpmode = mode;
                            AddProgram(((SubProgramCall)code).Program);
                            mode = tmpmode;
                            break;
                    }
                }
            }

            private void AddLinearMove(LinearMove line)
            {
                var pt = line.EndPoint.ToAcadXYZ();

                if (mode == Mode.Incremental)
                    pt = new XYZ(pt.X + CurPos.X, pt.Y + CurPos.Y, 0);

                AddLine(CurPos, pt, CutLayer);
                CurPos = pt;
            }

            private void AddRapidMove(RapidMove rapid)
            {
                var pt = rapid.EndPoint.ToAcadXYZ();

                if (mode == Mode.Incremental)
                    pt = new XYZ(pt.X + CurPos.X, pt.Y + CurPos.Y, 0);

                AddLine(CurPos, pt, RapidLayer);
                CurPos = pt;
            }

            private void AddArcMove(ArcMove arc)
            {
                var center = arc.CenterPoint.ToAcadXYZ();
                var endpt = arc.EndPoint.ToAcadXYZ();

                if (mode == Mode.Incremental)
                {
                    endpt = new XYZ(endpt.X + CurPos.X, endpt.Y + CurPos.Y, 0);
                    center = new XYZ(center.X + CurPos.X, center.Y + CurPos.Y, 0);
                }

                var startAngle = System.Math.Atan2(
                    CurPos.Y - center.Y,
                    CurPos.X - center.X);

                var endAngle = System.Math.Atan2(
                    endpt.Y - center.Y,
                    endpt.X - center.X);

                if (arc.Rotation == RotationType.CW)
                    Generic.Swap(ref startAngle, ref endAngle);

                var dx = endpt.X - center.X;
                var dy = endpt.Y - center.Y;
                var radius = System.Math.Sqrt(dx * dx + dy * dy);

                if (startAngle.IsEqualTo(endAngle))
                {
                    var circle = new AcadCircle
                    {
                        Center = center,
                        Radius = radius,
                        Layer = CutLayer
                    };
                    Document.Entities.Add(circle);
                }
                else
                {
                    var acadArc = new AcadArc
                    {
                        Center = center,
                        Radius = radius,
                        StartAngle = startAngle,
                        EndAngle = endAngle,
                        Layer = CutLayer
                    };
                    Document.Entities.Add(acadArc);
                }

                CurPos = endpt;
            }
        }

        #endregion
    }
}
