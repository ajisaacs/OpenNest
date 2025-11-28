using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public abstract class DrawControl : Control
    {
        protected PointF origin;
        protected Point lastPoint;
        protected System.Drawing.Size lastSize;

        public Vector LastPoint { get; protected set; }
        public Vector CurrentPoint { get; protected set; }

        public float ViewScale { get; protected set; }
        public float ViewScaleMin { get; protected set; }
        public float ViewScaleMax { get; protected set; }

        internal Matrix Matrix { get; set; }

        protected const int BorderWidth = 50;
        protected const float ZoomInFactor = 1.15f;
        protected const float ZoomOutFactor = 1.0f / ZoomInFactor;

        protected DrawControl()
        {
            ViewScale = 1.0f;
            ViewScaleMin = 0.3f;
            ViewScaleMax = 3000;
            origin = new PointF(100, 100);
        }

        /// <summary>
        /// Returns a point with coordinates relative to the control from the graph.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public Point PointGraphToControl(float x, float y)
        {
            return new Point((int)(x + origin.X), (int)(y + origin.Y));
        }

        /// <summary>
        /// Returns a point with coordinates relative to the control from the graph.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public Point PointGraphToControl(PointF pt)
        {
            return PointGraphToControl(pt.X, pt.Y);
        }

        /// <summary>
        /// Returns a point with coordinates relative to the control from the world.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public Point PointWorldToControl(double x, double y)
        {
            return PointGraphToControl(PointWorldToGraph(x, y));
        }

        /// <summary>
        /// Returns a point with coordinates relative to the control from the world.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public Point PointWorldToControl(Vector pt)
        {
            return PointGraphToControl(PointWorldToGraph(pt.X, pt.Y));
        }

        /// <summary>
        /// Returns a point with coordinates relative to the graph from the control.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public PointF PointControlToGraph(int x, int y)
        {
            return new PointF(x - origin.X, y - origin.Y);
        }

        /// <summary>
        /// Returns a point with coordinates relative to the graph from the control.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public PointF PointControlToGraph(Point pt)
        {
            return PointControlToGraph(pt.X, pt.Y);
        }

        /// <summary>
        /// Returns a point with coordinates relative to the graph from the world.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public PointF PointWorldToGraph(double x, double y)
        {
            return new PointF(ViewScale * (float)x, -ViewScale * (float)y);
        }

        /// <summary>
        /// Returns a point with coordinates relative to the graph from the world.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public PointF PointWorldToGraph(Vector pt)
        {
            return PointWorldToGraph(pt.X, pt.Y);
        }

        /// <summary>
        /// Returns a point with coordinates relative to the world from the control.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public Vector PointControlToWorld(int x, int y)
        {
            return PointGraphToWorld(PointControlToGraph(x, y));
        }

        /// <summary>
        /// Returns a point with coordinates relative to the world from the control.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public Vector PointControlToWorld(Point pt)
        {
            return PointGraphToWorld(PointControlToGraph(pt.X, pt.Y));
        }

        /// <summary>
        /// Returns a point with coordinates relative to the world from the graph.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public Vector PointGraphToWorld(float x, float y)
        {
            return new Vector(x / ViewScale, y / -ViewScale);
        }

        /// <summary>
        /// Returns a point with coordinates relative to the world from the graph.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public Vector PointGraphToWorld(PointF pt)
        {
            return PointGraphToWorld(pt.X, pt.Y);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            origin.X += (Size.Width - lastSize.Width) * 0.5f;
            origin.Y += (Size.Height - lastSize.Height) * 0.5f;

            lastSize = Size;
        }

        public float LengthWorldToGui(double length)
        {
            return ViewScale * (float)length;
        }

        public double LengthGuiToWorld(float length)
        {
            return length / ViewScale;
        }

        public virtual void ZoomToControlPoint(Point pt, float zoomFactor, bool redraw = true)
        {
            var pt2 = PointControlToWorld(pt);
            ZoomToPoint(pt2, zoomFactor, redraw);
        }

        public virtual void ZoomToPoint(Vector pt, float zoomFactor, bool redraw = true)
        {
            var newScale = ViewScale * zoomFactor;

            if (newScale < ViewScaleMin)
                zoomFactor = ViewScaleMin / ViewScale;
            else if (newScale > ViewScaleMax)
                zoomFactor = ViewScaleMax / ViewScale;

            origin.X -= (float)(pt.X * zoomFactor - pt.X) * ViewScale;
            origin.Y += (float)(pt.Y * zoomFactor - pt.Y) * ViewScale;

            ViewScale *= zoomFactor;
            UpdateMatrix();

            if (redraw) Invalidate();
        }

        public virtual void ZoomToArea(Box box, bool redraw = true)
        {
            ZoomToArea(box.X, box.Y, box.Width, box.Height, redraw);
        }

        public virtual void ZoomToArea(double x, double y, double width, double height, bool redraw = true)
        {
            if (width <= 0 || height <= 0)
                return;

            var a = (Height - BorderWidth) / height;
            var b = (Width - BorderWidth) / width;

            ViewScale = (float)(a < b ? a : b);

            if (ViewScale > ViewScaleMax)
                ViewScale = ViewScaleMax;
            else if (ViewScale < ViewScaleMin)
                ViewScale = ViewScaleMin;

            var px = LengthWorldToGui(x);
            var py = LengthWorldToGui(y);
            var pw = LengthWorldToGui(width);
            var ph = LengthWorldToGui(height);

            origin.X = (Width - pw) * 0.5f - px;
            origin.Y = (Height + ph) * 0.5f + py;

            UpdateMatrix();

            if (redraw) Invalidate();
        }

        protected virtual void UpdateMatrix()
        {
            if (Matrix != null)
                Matrix.Dispose();

            Matrix = new Matrix();
            Matrix.Scale(ViewScale, -ViewScale);
        }
    }
}
