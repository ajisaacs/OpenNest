using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Drawing;
using System.Drawing.Drawing2D;

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

        public static void GetGraphicsPaths(this Program pgm, Vector origin,
            out GraphicsPath cutPath, out GraphicsPath leadPath)
        {
            cutPath = new GraphicsPath();
            leadPath = new GraphicsPath();
            var curpos = origin;

            AddProgramSplit(cutPath, leadPath, pgm, pgm.Mode, ref curpos);
        }

        private static void AddProgramSplit(GraphicsPath cutPath, GraphicsPath leadPath,
            Program pgm, Mode mode, ref Vector curpos)
        {
            mode = pgm.Mode;

            for (var i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                switch (code.Type)
                {
                    case CodeType.ArcMove:
                        var arc = (ArcMove)code;
                        if (arc.Suppressed)
                        {
                            var endpt = arc.EndPoint;
                            if (mode == Mode.Incremental) endpt += curpos;
                            curpos = endpt;
                            break;
                        }
                        var arcPath = (arc.Layer == LayerType.Leadin || arc.Layer == LayerType.Leadout)
                            ? leadPath : cutPath;
                        AddArc(arcPath, arc, mode, ref curpos);
                        break;

                    case CodeType.LinearMove:
                        var line = (LinearMove)code;
                        if (line.Suppressed)
                        {
                            var endpt = line.EndPoint;
                            if (mode == Mode.Incremental) endpt += curpos;
                            curpos = endpt;
                            break;
                        }
                        var linePath = (line.Layer == LayerType.Leadin || line.Layer == LayerType.Leadout)
                            ? leadPath : cutPath;
                        AddLine(linePath, line, mode, ref curpos);
                        break;

                    case CodeType.RapidMove:
                        cutPath.StartFigure();
                        leadPath.StartFigure();
                        AddLine(cutPath, (RapidMove)code, mode, ref curpos);
                        break;

                    case CodeType.SubProgramCall:
                        var tmpmode = mode;
                        var subpgm = (SubProgramCall)code;
                        if (subpgm.Program != null)
                        {
                            cutPath.StartFigure();
                            leadPath.StartFigure();
                            var savedPos = curpos;
                            curpos = new Vector(savedPos.X + subpgm.Offset.X, savedPos.Y + subpgm.Offset.Y);
                            AddProgramSplit(cutPath, leadPath, subpgm.Program, mode, ref curpos);
                            curpos = savedPos;
                        }
                        mode = tmpmode;
                        break;
                }
            }
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

            curpos = pt;
        }

        private static void AddLine(GraphicsPath path, RapidMove line, Mode mode, ref Vector curpos)
        {
            var pt = line.EndPoint;

            if (mode == Mode.Incremental)
                pt += curpos;

            curpos = pt;
        }

        private static void AddProgram(GraphicsPath path, Program pgm, Mode mode, ref Vector curpos)
        {
            mode = pgm.Mode;
            GraphicsPath currentFigure = null;

            void Flush()
            {
                if (currentFigure != null)
                {
                    path.AddPath(currentFigure, false);
                    currentFigure.Dispose();
                    currentFigure = null;
                }
            }

            for (int i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                switch (code.Type)
                {
                    case CodeType.ArcMove:
                        {
                            var arc = (ArcMove)code;
                            if (arc.Layer != LayerType.Leadin && arc.Layer != LayerType.Leadout)
                            {
                                if (currentFigure == null) currentFigure = new GraphicsPath();
                                AddArc(currentFigure, arc, mode, ref curpos);
                            }
                            else
                            {
                                Flush();
                                var endpt = arc.EndPoint;
                                if (mode == Mode.Incremental) endpt += curpos;
                                curpos = endpt;
                            }
                        }
                        break;

                    case CodeType.LinearMove:
                        {
                            var line = (LinearMove)code;
                            if (line.Layer != LayerType.Leadin && line.Layer != LayerType.Leadout)
                            {
                                if (currentFigure == null) currentFigure = new GraphicsPath();
                                AddLine(currentFigure, line, mode, ref curpos);
                            }
                            else
                            {
                                Flush();
                                var endpt = line.EndPoint;
                                if (mode == Mode.Incremental) endpt += curpos;
                                curpos = endpt;
                            }
                        }
                        break;

                    case CodeType.RapidMove:
                        Flush();
                        AddLine(path, (RapidMove)code, mode, ref curpos);
                        break;

                    case CodeType.SubProgramCall:
                        {
                            Flush();
                            var tmpmode = mode;
                            var subpgm = (SubProgramCall)code;

                            if (subpgm.Program != null)
                            {
                                var savedPos = curpos;
                                curpos = new Vector(savedPos.X + subpgm.Offset.X, savedPos.Y + subpgm.Offset.Y);
                                AddProgram(path, subpgm.Program, mode, ref curpos);
                                curpos = savedPos;
                            }

                            mode = tmpmode;
                            break;
                        }
                }
            }

            Flush();
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
                if (entity.Layer != null)
                {
                    if (string.Equals(entity.Layer.Name, SpecialLayers.Leadin.Name, System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(entity.Layer.Name, SpecialLayers.Leadout.Name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

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
