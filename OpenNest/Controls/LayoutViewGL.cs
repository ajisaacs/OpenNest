using System.Runtime.Remoting.Messaging;
using libPep;
using libPep.Codes;
using OpenTK.Graphics.OpenGL;
using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Math;

namespace OpenNest.Controls
{
    public class LayoutViewGL : OpenTK.GLControl
    {
        private float scale;
        private bool loaded = false;

        private const double TwoPI = System.Math.PI * 2.0;
        private const int Resolution = 100;
        private const int BorderWidth = 50;
        private const float ZoomInFactor = 1.1f;
        private const float ZoomOutFactor = 0.9f;

        private PointF origin;
        private PointF lastPoint;

        private Vector curpos;
        private ProgrammingMode mode;

        public Color RapidColor { get; set; }
        public Color PlateFillColor { get; set; }
        public Color PlateBorderColor { get; set; }
        public Color GeometryColor { get; set; }

        public Plate Plate;

        public LayoutViewGL()
        {
            scale = 25;
            origin = new PointF(0, 0);
            lastPoint = new PointF();

            BackColor = Color.White;
            RapidColor = Color.Blue;
            GeometryColor = Color.LimeGreen;
            PlateFillColor = Color.WhiteSmoke;
            PlateBorderColor = Color.DarkGray;

            Cursor = Cursors.Cross;

            Plate = new Plate();

            Context.SwapInterval = 0;
        }

        public bool DrawRapid { get; set; }

        private void SetupViewport()
        {
            int w = Width;
            int h = Height;
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Ortho(0, w, 0, h, -1, 1);
            GL.Viewport(0, 0, w, h);
        }

        protected override void OnLoad(EventArgs e)
        {
            SetupViewport();
            GL.ClearColor(this.BackColor);
            loaded = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!loaded)
                return;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();

            //DrawAxis();

            GL.Translate(origin.X, origin.Y, 0);
            GL.Scale(scale, scale, 1);

            curpos = new Vector();

            if (Plate != null)
            {
                GL.Color3(PlateFillColor);
                GL.Begin(PrimitiveType.Quads);
                DrawPlate(Plate);
                GL.End();

                GL.Color3(PlateBorderColor);
                GL.Begin(PrimitiveType.LineLoop);
                DrawPlate(Plate);
                GL.End();

                GL.Color3(GeometryColor);
                DrawProgram(Plate);
            }

            SwapBuffers();
        }

        protected override void OnResize(EventArgs e)
        {
            if (!loaded)
                return;

            SetupViewport();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            float multiplier = System.Math.Abs(e.Delta / 120.0f);

            if (e.Delta > 0)
                ZoomToPoint(e.X, e.Y, (float)System.Math.Pow(ZoomInFactor, multiplier));
            else
                ZoomToPoint(e.X, e.Y, (float)System.Math.Pow(ZoomOutFactor, multiplier));

            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (e.Button == MouseButtons.Middle)
            {
                var diffx = e.X - lastPoint.X;
                var diffy = e.Y - lastPoint.Y;

                origin.X += diffx;
                origin.Y -= diffy;

                Invalidate();
            }

            lastPoint = e.Location;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (e.Button == MouseButtons.Middle)
            {
                ZoomToFit();
                Invalidate();
            }
        }

        private void DrawAxis()
        {
            GL.Begin(PrimitiveType.Lines);
            GL.Vertex2(origin.X, 0);
            GL.Vertex2(origin.X, Height);

            GL.Vertex2(0, origin.Y);
            GL.Vertex2(Width, origin.Y);
            GL.End();
        }

        public void ZoomToPoint(double x, double y, float zoomFactor)
        {
            double x1 = ToRealX(x);
            double y1 = ToRealY(y);

            origin.X -= (float)(x1 * zoomFactor - x1) * scale;
            origin.Y -= (float)(y1 * zoomFactor - y1) * scale;

            scale *= zoomFactor;
        }

        private void DrawPlate(Plate plate)
        {
            switch (plate.Quadrant)
            {
                case 1:
                    GL.Vertex2(0, 0);
                    GL.Vertex2(0, plate.Size.Width);
                    GL.Vertex2(plate.Size.Length, plate.Size.Width);
                    GL.Vertex2(plate.Size.Length, 0);
                    break;

                case 2:
                    GL.Vertex2(0, 0);
                    GL.Vertex2(0, plate.Size.Width);
                    GL.Vertex2(-plate.Size.Length, plate.Size.Width);
                    GL.Vertex2(-plate.Size.Length, 0);
                    break;

                case 3:
                    GL.Vertex2(0, 0);
                    GL.Vertex2(0, -plate.Size.Width);
                    GL.Vertex2(-plate.Size.Length, -plate.Size.Width);
                    GL.Vertex2(-plate.Size.Length, 0);
                    break;

                case 4:
                    GL.Vertex2(0, 0);
                    GL.Vertex2(0, -plate.Size.Width);
                    GL.Vertex2(plate.Size.Length, -plate.Size.Width);
                    GL.Vertex2(plate.Size.Length, 0);
                    break;

                default:
                    return;
            }
        }

        private void DrawProgram(libPep.Program pgm)
        {
            mode = pgm.Mode;

            foreach (var code in pgm)
            {
                switch (code.Type())
                {
                    case CodeType.Arc:
                        var arc = (Arc)code;
                        DrawArc(arc);
                        break;

                    case CodeType.Line:
                        var line = (Line)code;
                        DrawLine(line);
                        break;

                    case CodeType.SubProgramCall:
                        var tmpmode = mode;
                        var subpgm = (SubProgramCall)code;

                        if (subpgm.Loop != null)
                            DrawProgram(subpgm.Loop);

                        mode = tmpmode;
                        break;
                }
            }
        }

        private void DrawLine(Line line)
        {
            var pt = line.EndPoint;

            if (mode == ProgrammingMode.Incremental)
                pt += curpos;

            if (line.IsRapid)
            {
                if (DrawRapid)
                {
                    GL.PushAttrib(AttribMask.EnableBit);
                    GL.Enable(EnableCap.LineStipple);
                    GL.LineStipple(2, 0xCCCC);
                    GL.Color3(RapidColor);
                    DrawLine(curpos, pt);
                    GL.Color3(GeometryColor);
                    GL.PopAttrib();
                }
            }
            else
                DrawLine(curpos, pt);

            curpos = pt;
        }

        private void DrawLine(Vector pt1, Vector pt2)
        {
            GL.Begin(PrimitiveType.Lines);
            GL.Vertex2(pt1.X, pt1.Y);
            GL.Vertex2(pt2.X, pt2.Y);
            GL.End();
        }

        private void DrawArc(Arc arc)
        {
            var endpt = arc.EndPoint;
            var center = arc.CenterPoint;

            if (mode == ProgrammingMode.Incremental)
            {
                endpt += curpos;
                center += curpos;
            }

            // start angle in radians
            var startAngle = System.Math.Atan2(
                curpos.Y - center.Y,
                curpos.X - center.X);

            // end angle in radians
            var endAngle = System.Math.Atan2(
                endpt.Y - center.Y,
                endpt.X - center.X);

            endAngle = NormalizeAngle(endAngle);
            startAngle = NormalizeAngle(startAngle);

            if (arc.Rotation == RotationType.CCW && endAngle < startAngle)
                endAngle += TwoPI;
            else if (arc.Rotation == RotationType.CW && startAngle < endAngle)
                startAngle += TwoPI;

            var dx = endpt.X - center.X;
            var dy = endpt.Y - center.Y;

            var radius = System.Math.Sqrt(dx * dx + dy * dy);

            if (startAngle.IsEqualTo(endAngle))
            {
                GL.End();
                GL.Begin(PrimitiveType.LineLoop);
                DrawCircle(center, radius);
                GL.End();
                GL.Begin(PrimitiveType.Polygon);
            }
            else
                DrawArc(center, radius, endAngle, startAngle);

            curpos = endpt;
        }

        private void DrawArc(Vector center, double radius, double startAngle, double endAngle)
        {
            GL.Begin(PrimitiveType.LineStrip);

            var angle = (endAngle - startAngle) / Resolution;

            for (int i = 0; i <= Resolution; i++)
            {
                GL.Vertex2(
                    System.Math.Cos(startAngle + angle * i) * radius + center.X,
                    System.Math.Sin(startAngle + angle * i) * radius + center.Y);
            }

            GL.End();
        }

        private void DrawCircle(Vector center, double radius)
        {
            const float angle = (float)System.Math.PI * 2.0f;
            const float increment = angle / Resolution;

            for (float i = 0; i <= angle; i += increment)
            {
                GL.Vertex2(
                    System.Math.Cos(i) * radius + center.X,
                    System.Math.Sin(i) * radius + center.Y);
            }
            
        }

        private static double NormalizeAngle(double angle)
        {
            double r = angle % TwoPI;
            return r < 0 ? TwoPI + r : r;
        }

        private static void Swap<T>(ref T a, ref T b)
        {
            T c = a;
            a = b;
            b = c;
        }

        public void ZoomToFit()
        {
            if (Plate.Size.Width <= 0 || Plate.Size.Length <= 0)
                return;

            float a = (this.Height - BorderWidth) / (float)Plate.Size.Width;
            float b = (this.Width - BorderWidth) / (float)Plate.Size.Length;

            scale = a < b ? a : b;

            double px;
            double py;

            switch (Plate.Quadrant)
            {
                case 1:
                    px = py = ToGui(0);
                    break;

                case 2:
                    px = ToGui(-Plate.Size.Length);
                    py = ToGui(0);
                    break;

                case 3:
                    px = ToGui(-Plate.Size.Length);
                    py = ToGui(-Plate.Size.Width);
                    break;

                case 4:
                    px = ToGui(0);
                    py = ToGui(-Plate.Size.Width);
                    break;

                default:
                    return;
            }

            var pw = ToGui(Plate.Size.Length);
            var ph = ToGui(Plate.Size.Width);

            origin.X = (float)((this.Width - pw) * 0.5f - px);
            origin.Y = (float)((this.Height - ph) * 0.5f - py);

            Invalidate();
        }

        public float ToGui(double v)
        {
            return (float)v * scale;
        }

        public double ToReal(double v)
        {
            return v / scale;
        }

        public double ToRealX(double x)
        {
            return (x - origin.X) / scale;
        }

        public double ToRealY(double y)
        {
            return (Height - y - origin.Y) / scale;
        }

        public float ToGuiX(double x)
        {
            return scale * (float)x + origin.X;
        }

        public float ToGuiY(double y)
        {
            return scale * (float)y + origin.Y;
        }
    }
}
