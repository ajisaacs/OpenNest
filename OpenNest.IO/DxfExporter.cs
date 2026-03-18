using System;
using System.Diagnostics;
using System.IO;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using OpenNest.CNC;
using OpenNest.Math;

namespace OpenNest.IO
{
    using AcadArc = ACadSharp.Entities.Arc;
    using AcadCircle = ACadSharp.Entities.Circle;
    using AcadLine = ACadSharp.Entities.Line;
    using Layer = ACadSharp.Tables.Layer;

    public class DxfExporter
    {
        private CadDocument doc;
        private XYZ curpos;
        private Mode mode;
        private readonly Layer cutLayer;
        private readonly Layer rapidLayer;
        private readonly Layer plateLayer;

        public DxfExporter()
        {
            doc = new CadDocument();

            cutLayer = new Layer("Cut");
            cutLayer.Color = new Color(1);

            rapidLayer = new Layer("Rapid");
            rapidLayer.Color = new Color(5);

            plateLayer = new Layer("Plate");
            plateLayer.Color = new Color(4);
        }

        public void ExportProgram(Program program, Stream stream)
        {
            doc = new CadDocument();
            EnsureLayers();
            AddProgram(program);
            using (var writer = new DxfWriter(stream, doc, false))
            {
                writer.Write();
            }
        }

        public bool ExportProgram(Program program, string path)
        {
            Stream stream = null;
            var success = false;

            try
            {
                stream = File.Create(path);
                ExportProgram(program, stream);
                success = true;
            }
            catch
            {
                Debug.Fail("DxfExporter.ExportProgram failed to write program to file: " + path);
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            return success;
        }

        public void ExportPlate(Plate plate, Stream stream)
        {
            doc = new CadDocument();
            EnsureLayers();
            AddPlateOutline(plate);

            foreach (var part in plate.Parts)
            {
                var endpt = part.Location.ToAcadXYZ();
                AddLine(curpos, endpt, rapidLayer);
                curpos = part.Location.ToAcadXYZ();
                AddProgram(part.Program);
            }

            using (var writer = new DxfWriter(stream, doc, false))
            {
                writer.Write();
            }
        }

        public bool ExportPlate(Plate plate, string path)
        {
            Stream stream = null;
            var success = false;

            try
            {
                stream = File.Create(path);
                ExportPlate(plate, stream);
                success = true;
            }
            catch
            {
                Debug.Fail("DxfExporter.ExportPlate failed to write plate to file: " + path);
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            return success;
        }

        private void EnsureLayers()
        {
            doc.Layers.Add(cutLayer);
            doc.Layers.Add(rapidLayer);
            doc.Layers.Add(plateLayer);
        }

        private void AddLine(XYZ start, XYZ end, Layer layer)
        {
            var ln = new AcadLine();
            ln.StartPoint = start;
            ln.EndPoint = end;
            ln.Layer = layer;
            doc.Entities.Add(ln);
        }

        private void AddPlateOutline(Plate plate)
        {
            XYZ pt1;
            XYZ pt2;
            XYZ pt3;
            XYZ pt4;

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

            AddLine(pt1, pt2, plateLayer);
            AddLine(pt2, pt3, plateLayer);
            AddLine(pt3, pt4, plateLayer);
            AddLine(pt4, pt1, plateLayer);

            var m1 = new XYZ(pt1.X + plate.EdgeSpacing.Left, pt1.Y + plate.EdgeSpacing.Bottom, 0);
            var m2 = new XYZ(m1.X, pt2.Y - plate.EdgeSpacing.Top, 0);
            var m3 = new XYZ(pt3.X - plate.EdgeSpacing.Right, m2.Y, 0);
            var m4 = new XYZ(m3.X, m1.Y, 0);

            AddLine(m1, m2, plateLayer);
            AddLine(m2, m3, plateLayer);
            AddLine(m3, m4, plateLayer);
            AddLine(m4, m1, plateLayer);
        }

        private void AddProgram(Program program)
        {
            mode = program.Mode;

            for (var i = 0; i < program.Length; ++i)
            {
                var code = program[i];

                switch (code.Type)
                {
                    case CodeType.ArcMove:
                        var arc = (ArcMove)code;
                        AddArcMove(arc);
                        break;

                    case CodeType.LinearMove:
                        var line = (LinearMove)code;
                        AddLinearMove(line);
                        break;

                    case CodeType.RapidMove:
                        var rapid = (RapidMove)code;
                        AddRapidMove(rapid);
                        break;

                    case CodeType.SubProgramCall:
                        var tmpmode = mode;
                        var subpgm = (CNC.SubProgramCall)code;
                        AddProgram(subpgm.Program);
                        mode = tmpmode;
                        break;
                }
            }
        }

        private void AddLinearMove(LinearMove line)
        {
            var pt = line.EndPoint.ToAcadXYZ();

            if (mode == Mode.Incremental)
                pt = new XYZ(pt.X + curpos.X, pt.Y + curpos.Y, 0);

            AddLine(curpos, pt, cutLayer);
            curpos = pt;
        }

        private void AddRapidMove(RapidMove rapid)
        {
            var pt = rapid.EndPoint.ToAcadXYZ();

            if (mode == Mode.Incremental)
                pt = new XYZ(pt.X + curpos.X, pt.Y + curpos.Y, 0);

            AddLine(curpos, pt, rapidLayer);
            curpos = pt;
        }

        private void AddArcMove(ArcMove arc)
        {
            var center = arc.CenterPoint.ToAcadXYZ();
            var endpt = arc.EndPoint.ToAcadXYZ();

            if (mode == Mode.Incremental)
            {
                endpt = new XYZ(endpt.X + curpos.X, endpt.Y + curpos.Y, 0);
                center = new XYZ(center.X + curpos.X, center.Y + curpos.Y, 0);
            }

            var startAngle = System.Math.Atan2(
                curpos.Y - center.Y,
                curpos.X - center.X);

            var endAngle = System.Math.Atan2(
                endpt.Y - center.Y,
                endpt.X - center.X);

            if (arc.Rotation == OpenNest.RotationType.CW)
                Generic.Swap(ref startAngle, ref endAngle);

            var dx = endpt.X - center.X;
            var dy = endpt.Y - center.Y;

            var radius = System.Math.Sqrt(dx * dx + dy * dy);

            if (startAngle.IsEqualTo(endAngle))
            {
                var circle = new AcadCircle();
                circle.Center = center;
                circle.Radius = radius;
                circle.Layer = cutLayer;
                doc.Entities.Add(circle);
            }
            else
            {
                var arc2 = new AcadArc();
                arc2.Center = center;
                arc2.Radius = radius;
                arc2.StartAngle = startAngle;
                arc2.EndAngle = endAngle;
                arc2.Layer = cutLayer;
                doc.Entities.Add(arc2);
            }

            curpos = endpt;
        }
    }
}
