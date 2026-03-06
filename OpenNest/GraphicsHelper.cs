using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    internal static class GraphicsHelper
    {
        public static GraphicsPath GetGraphicsPath(this Program pgm)
        {
            var path = new GraphicsPath();
            var curpos = Vector.Zero;

            AddProgram(path, pgm, pgm.Mode, ref curpos);

            return path;
        }

        public static GraphicsPath GetGraphicsPath(this Program pgm, Vector origin)
        {
            var path = new GraphicsPath();
            var curpos = origin;

            AddProgram(path, pgm, pgm.Mode, ref curpos);

            return path;
        }

        public static GraphicsPath GetGraphicsPath(this Shape shape)
        {
            var path = new GraphicsPath();

            AddShape(path, shape);

            return path;
        }

        public static Image GetImage(this Program pgm, System.Drawing.Size size)
        {
            return pgm.GetImage(size, Pens.Black, null);
        }

        public static Image GetImage(this Program pgm, System.Drawing.Size size, Pen pen)
        {
            return pgm.GetImage(size, pen, null);
        }

        public static Image GetImage(this Program pgm, System.Drawing.Size size, Pen pen, Brush brush)
        {
            var img = new Bitmap(size.Width, size.Height);
            var path = pgm.GetGraphicsPath();
            var bounds = path.GetBounds();
            
            var scalex = (size.Height - 10) / bounds.Height;
            var scaley = (size.Width - 10) / bounds.Width;
            var scale = scalex < scaley ? scalex : scaley;

            var matrix = new Matrix();
            matrix.Scale(scale, -scale);

            path.Transform(matrix);

            bounds = path.GetBounds();

            var offset = new PointF(
                (size.Width - bounds.Width) * 0.5f - bounds.X,
                (size.Height - bounds.Height) * 0.5f - bounds.Y);

            var graphics = Graphics.FromImage(img);
            graphics.TranslateTransform(offset.X, offset.Y);

            if (brush != null)
                graphics.FillPath(brush, path);

            if (pen == null)
                pen = Pens.Black;

            graphics.DrawPath(pen, path);

            matrix.Dispose();
            graphics.Dispose();

            return img;
        }

        private static void AddArc(GraphicsPath path, ArcMove arc, Mode mode, ref Vector curpos)
        {
            var endpt = arc.EndPoint;
            var center = arc.CenterPoint;

            if (mode == Mode.Incremental)
            {
                endpt += curpos;
                center += curpos;
            }

            // start angle in degrees
            var startAngle = Angle.ToDegrees(System.Math.Atan2(
                curpos.Y - center.Y,
                curpos.X - center.X));

            // end angle in degrees
            var endAngle = Angle.ToDegrees(System.Math.Atan2(
                endpt.Y - center.Y,
                endpt.X - center.X));

            endAngle = Angle.NormalizeDeg(endAngle);
            startAngle = Angle.NormalizeDeg(startAngle);

            if (arc.Rotation == RotationType.CCW && endAngle < startAngle)
                endAngle += 360.0;
            else if (arc.Rotation == RotationType.CW && startAngle < endAngle)
                startAngle += 360.0;

            var dx = endpt.X - center.X;
            var dy = endpt.Y - center.Y;

            var radius = System.Math.Sqrt(dx * dx + dy * dy);

            var pt = new PointF((float)(center.X - radius), (float)(center.Y - radius));
            var size = (float)(radius * 2.0);

            if (startAngle.IsEqualTo(endAngle))
            {
                path.AddEllipse(pt.X, pt.Y, size, size);
            }
            else
            {
                var sweepAngle = (endAngle - startAngle);

                path.AddArc(
                    pt.X, pt.Y, 
                    size, size,
                    (float)startAngle, 
                    (float)sweepAngle);

                if (arc.Layer == LayerType.Leadin || arc.Layer == LayerType.Leadout)
                {
                    path.AddArc(pt.X, pt.Y, size, size, (float)(-startAngle + sweepAngle), (float)-sweepAngle);
                    path.CloseFigure();
                }
            }

            curpos = endpt;
        }

        private static void AddLine(GraphicsPath path, LinearMove line, Mode mode, ref Vector curpos)
        {
            var pt = line.EndPoint;

            if (mode == Mode.Incremental)
                pt += curpos;

            var pt1 = new PointF((float)curpos.X, (float)curpos.Y);
            var pt2 = new PointF((float)pt.X, (float)pt.Y);

            path.AddLine(pt1, pt2);

            if (line.Layer == LayerType.Leadin || line.Layer == LayerType.Leadout)
                path.CloseFigure();

            curpos = pt;
        }

        private static void AddLine(GraphicsPath path, RapidMove line, Mode mode, ref Vector curpos)
        {
            var pt = line.EndPoint;

            if (mode == Mode.Incremental)
                pt += curpos;

            path.CloseFigure();

            curpos = pt;
        }

        private static void AddProgram(GraphicsPath path, Program pgm, Mode mode, ref Vector curpos)
        {
            mode = pgm.Mode;

            for (int i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                switch (code.Type)
                {
                    case CodeType.ArcMove:
                        AddArc(path, (ArcMove)code, mode, ref curpos);
                        break;

                    case CodeType.LinearMove:
                        AddLine(path, (LinearMove)code, mode, ref curpos);
                        break;

                    case CodeType.RapidMove:
                        AddLine(path, (RapidMove)code, mode, ref curpos);
                        break;

                    case CodeType.SubProgramCall:
                        {
                            var tmpmode = mode;
                            var subpgm = (SubProgramCall)code;

                            if (subpgm.Program != null)
                            {
                                path.StartFigure();
                                AddProgram(path, subpgm.Program, mode, ref curpos);
                            }

                            mode = tmpmode;
                            break;
                        }
                }
            }
        }

        private static void AddArc(GraphicsPath path, Arc arc)
        {
            var diameter = arc.Diameter;

            var endAngle = Angle.NormalizeDeg(Angle.ToDegrees(arc.EndAngle));
            var startAngle = Angle.NormalizeDeg(Angle.ToDegrees(arc.StartAngle));

            if (arc.Rotation == RotationType.CCW && endAngle < startAngle)
                endAngle += 360.0;
            else if (arc.Rotation == RotationType.CW && startAngle < endAngle)
                startAngle += 360.0;

            var sweepAngle = (endAngle - startAngle);

            path.AddArc(
                (float)(arc.Center.X - arc.Radius),
                (float)(arc.Center.Y - arc.Radius),
                (float)diameter,
                (float)diameter,
                (float)(startAngle),
                (float)sweepAngle);
        }

        private static void AddCircle(GraphicsPath path, Circle circle)
        {
            var diameter = circle.Diameter;

            path.AddEllipse(
                (float)(circle.Center.X - circle.Radius),
                (float)(circle.Center.Y - circle.Radius),
                (float)diameter,
                (float)diameter);
        }

        private static void AddLine(GraphicsPath path, Line line)
        {
            path.AddLine(
                (float)line.StartPoint.X,
                (float)line.StartPoint.Y,
                (float)line.EndPoint.X,
                (float)line.EndPoint.Y);
        }

        private static void AddShape(GraphicsPath path, Shape shape)
        {
            foreach (var entity in shape.Entities)
            {
                switch (entity.Type)
                {
                    case EntityType.Arc:
                        AddArc(path, (Arc)entity);
                        break;

                    case EntityType.Circle:
                        AddCircle(path, (Circle)entity);
                        break;

                    case EntityType.Line:
                        AddLine(path, (Line)entity);
                        break;

                    case EntityType.Polygon:
                        AddPolygon(path, (Polygon)entity);
                        break;

                    case EntityType.Shape:
                        var subpath = new GraphicsPath();
                        AddShape(subpath, (Shape)entity);
                        path.AddPath(subpath, false);
                        subpath.Dispose();
                        break;
                }
            }
        }

        private static void AddPolygon(GraphicsPath path, Polygon polygon)
        {
            var pts = new PointF[polygon.Vertices.Count];

            for (int i = 0; i < pts.Length; i++)
            {
                var pt = polygon.Vertices[i];
                pts[i] = new PointF((float)pt.X, (float)pt.Y);
            }

            path.AddPolygon(pts);
        }
    }
}
