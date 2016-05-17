using System;
using System.Diagnostics;
using System.IO;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using OpenNest.CNC;

namespace OpenNest.IO
{
    using Layer = netDxf.Tables.Layer;
    using Line = netDxf.Entities.Line;

    public class DxfExporter
    {
        private const double RadToDeg = 180.0 / Math.PI;

        private DxfDocument doc;
        private Vector2 curpos;
        private Mode mode;
        private readonly Layer cutLayer;
        private readonly Layer rapidLayer;
        private readonly Layer plateLayer;

        public DxfExporter()
        {
            doc = new DxfDocument();

            cutLayer = new Layer("Cut");
            cutLayer.Color = AciColor.Red;

            rapidLayer = new Layer("Rapid");
            rapidLayer.Color = AciColor.Blue;
            rapidLayer.LineType = LineType.Dashed;

            plateLayer = new Layer("Plate");
            plateLayer.Color = AciColor.Cyan;
        }

        public void ExportProgram(Program program, Stream stream)
        {
            doc = new DxfDocument();
            AddProgram(program);
            doc.Save(stream);
        }

        public bool ExportProgram(Program program, string path)
        {
            Stream stream = null;
            bool success = false;

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
            doc = new DxfDocument();
            AddPlateOutline(plate);

            foreach (var part in plate.Parts)
            {
                var endpt = part.Location.ToNetDxf();
                var line = new netDxf.Entities.Line(curpos, endpt);
                line.Layer = rapidLayer;
                doc.AddEntity(line);
                curpos = part.Location.ToNetDxf();
                AddProgram(part.Program);
            }

            doc.Save(stream);
        }

        public bool ExportPlate(Plate plate, string path)
        {
            Stream stream = null;
            bool success = false;

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

        private void AddPlateOutline(Plate plate)
        {
            Vector2 pt1;
            Vector2 pt2;
            Vector2 pt3;
            Vector2 pt4;

            switch (plate.Quadrant)
            {
                case 1:
                    pt1 = new Vector2(0, 0);
                    pt2 = new Vector2(0, plate.Size.Height);
                    pt3 = new Vector2(plate.Size.Width, plate.Size.Height);
                    pt4 = new Vector2(plate.Size.Width, 0);
                    break;

                case 2:
                    pt1 = new Vector2(0, 0);
                    pt2 = new Vector2(0, plate.Size.Height);
                    pt3 = new Vector2(-plate.Size.Width, plate.Size.Height);
                    pt4 = new Vector2(-plate.Size.Width, 0);
                    break;

                case 3:
                    pt1 = new Vector2(0, 0);
                    pt2 = new Vector2(0, -plate.Size.Height);
                    pt3 = new Vector2(-plate.Size.Width, -plate.Size.Height);
                    pt4 = new Vector2(-plate.Size.Width, 0);
                    break;

                case 4:
                    pt1 = new Vector2(0, 0);
                    pt2 = new Vector2(0, -plate.Size.Height);
                    pt3 = new Vector2(plate.Size.Width, -plate.Size.Height);
                    pt4 = new Vector2(plate.Size.Width, 0);
                    break;

                default:
                    return;
            }

            doc.AddEntity(new Line(pt1, pt2) { Layer = plateLayer });
            doc.AddEntity(new Line(pt2, pt3) { Layer = plateLayer });
            doc.AddEntity(new Line(pt3, pt4) { Layer = plateLayer });
            doc.AddEntity(new Line(pt4, pt1) { Layer = plateLayer });

            pt1.X += plate.EdgeSpacing.Left;
            pt1.Y += plate.EdgeSpacing.Bottom;

            pt2.X = pt1.X;
            pt2.Y -= plate.EdgeSpacing.Top;

            pt3.X -= plate.EdgeSpacing.Right;
            pt3.Y = pt2.Y;

            pt4.X = pt3.X;
            pt4.Y = pt1.Y;

            doc.AddEntity(new Line(pt1, pt2) { Layer = plateLayer, LineType = LineType.Dashed });
            doc.AddEntity(new Line(pt2, pt3) { Layer = plateLayer, LineType = LineType.Dashed });
            doc.AddEntity(new Line(pt3, pt4) { Layer = plateLayer, LineType = LineType.Dashed });
            doc.AddEntity(new Line(pt4, pt1) { Layer = plateLayer, LineType = LineType.Dashed });
        }

        private void AddProgram(Program program)
        {
            mode = program.Mode;

            for (int i = 0; i < program.Length; ++i)
            {
                var code = program[i];

                switch (code.Type)
                {
                    case CodeType.CircularMove:
                        var arc = (CircularMove)code;
                        AddCircularMove(arc);
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
            var pt = line.EndPoint.ToNetDxf();

            if (mode == Mode.Incremental)
                pt += curpos;

            var ln = new Line(curpos, pt);
            ln.Layer = cutLayer;
            doc.AddEntity(ln);
            curpos = pt;
        }

        private void AddRapidMove(RapidMove rapid)
        {
            var pt = rapid.EndPoint.ToNetDxf();

            if (mode == Mode.Incremental)
                pt += curpos;

            var ln = new Line(curpos, pt);
            ln.Layer = rapidLayer;
            doc.AddEntity(ln);
            curpos = pt;
        }

        private void AddCircularMove(CircularMove arc)
        {
            var center = arc.CenterPoint.ToNetDxf();
            var endpt = arc.EndPoint.ToNetDxf();

            if (mode == Mode.Incremental)
            {
                endpt += curpos;
                center += curpos;
            }

            // start angle in radians
            var startAngle = Math.Atan2(
                curpos.Y - center.Y,
                curpos.X - center.X);

            // end angle in radians
            var endAngle = Math.Atan2(
                endpt.Y - center.Y,
                endpt.X - center.X);

            // convert the angles to degrees
            startAngle = Angle.ToDegrees(startAngle);
            endAngle = Angle.ToDegrees(endAngle);

            if (arc.Rotation == OpenNest.RotationType.CW)
                Generic.Swap(ref startAngle, ref endAngle);

            var dx = endpt.X - center.X;
            var dy = endpt.Y - center.Y;

            var radius = Math.Sqrt(dx * dx + dy * dy);

            if (startAngle.IsEqualTo(endAngle))
            {
                var circle = new Circle(center, radius);
                circle.Layer = cutLayer;
                doc.AddEntity(circle);
            }
            else
            {
                var arc2 = new Arc(center, radius, startAngle, endAngle);
                arc2.Layer = cutLayer;
                doc.AddEntity(arc2);
            }

            curpos = endpt;
        }
    }
}
