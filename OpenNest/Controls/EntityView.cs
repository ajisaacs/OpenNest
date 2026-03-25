using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class EntityView : DrawControl
    {
        public List<Entity> Entities;
        public List<Bend> Bends = new List<Bend>();
        public int SelectedBendIndex = -1;

        private readonly Pen gridPen = new Pen(Color.FromArgb(70, 70, 70));
        private readonly Dictionary<int, Pen> penCache = new Dictionary<int, Pen>();

        public EntityView()
        {
            Entities = new List<Entity>();

            this.BackColor = Color.FromArgb(33, 40, 48);
            this.Cursor = Cursors.Cross;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Focused) Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.HighSpeed;
            e.Graphics.DrawLine(gridPen, origin.X, 0, origin.X, Height);
            e.Graphics.DrawLine(gridPen, 0, origin.Y, Width, origin.Y);

            e.Graphics.TranslateTransform(origin.X, origin.Y);

            foreach (var entity in Entities)
            {
                var pen = GetEntityPen(entity.Color);
                DrawEntity(e.Graphics, entity, pen);
            }

            DrawBendLines(e.Graphics);

#if DRAW_OFFSET

            var offsetShape = new Shape();
            offsetShape.Entities.AddRange(Entities);

            foreach (var entity in ((Shape)offsetShape.OffsetEntity(0.25, OffsetSide.Left)).Entities)
                DrawEntity(e.Graphics, entity, Pens.RoyalBlue);
#endif
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            float multiplier = System.Math.Abs(e.Delta / 120.0f);

            if (e.Delta > 0)
                ZoomToControlPoint(e.Location, (float)System.Math.Pow(ZoomInFactor, multiplier));
            else
                ZoomToControlPoint(e.Location, (float)System.Math.Pow(ZoomOutFactor, multiplier));

            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                var diffx = e.X - lastPoint.X;
                var diffy = e.Y - lastPoint.Y;

                origin.X += diffx;
                origin.Y += diffy;

                Invalidate();
            }
            else
            {
                LastPoint = CurrentPoint;
                CurrentPoint = PointControlToWorld(e.Location);
            }

            lastPoint = e.Location;

            base.OnMouseMove(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (e.Button == MouseButtons.Middle)
                ZoomToFit();
        }

        private Pen GetEntityPen(Color color)
        {
            if (color.IsEmpty || color.A == 0)
                color = Color.White;

            // Clamp dark colors to ensure visibility on dark background
            var brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
            if (brightness < 80)
                color = Color.FromArgb(color.A,
                    System.Math.Max(color.R, (byte)80),
                    System.Math.Max(color.G, (byte)80),
                    System.Math.Max(color.B, (byte)80));

            var argb = color.ToArgb();
            if (!penCache.TryGetValue(argb, out var pen))
            {
                pen = new Pen(color);
                penCache[argb] = pen;
            }
            return pen;
        }

        public void ClearPenCache()
        {
            foreach (var pen in penCache.Values)
                pen.Dispose();
            penCache.Clear();
        }

        private void DrawBendLines(Graphics g)
        {
            if (Bends == null || Bends.Count == 0)
                return;

            using var bendPen = new Pen(Color.Yellow, 1.5f)
            {
                DashStyle = DashStyle.Dash
            };
            using var selectedPen = new Pen(Color.Cyan, 2.5f)
            {
                DashStyle = DashStyle.Dash
            };

            for (var i = 0; i < Bends.Count; i++)
            {
                var bend = Bends[i];
                var pt1 = PointWorldToGraph(bend.StartPoint);
                var pt2 = PointWorldToGraph(bend.EndPoint);
                g.DrawLine(i == SelectedBendIndex ? selectedPen : bendPen, pt1, pt2);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearPenCache();
                gridPen.Dispose();
            }
            base.Dispose(disposing);
        }

        private void DrawEntity(Graphics g, Entity e, Pen pen)
        {
            if (!e.Layer.IsVisible || !e.IsVisible)
                return;

            switch (e.Type)
            {
                case EntityType.Arc:
                    DrawArc(g, (Arc)e, pen);
                    break;

                case EntityType.Circle:
                    DrawCircle(g, (Circle)e, pen);
                    break;

                case EntityType.Line:
                    DrawLine(g, (Line)e, pen);
                    break;
            }
        }

        private void DrawLine(Graphics g, Line line, Pen pen)
        {
            var pt1 = PointWorldToGraph(line.StartPoint);
            var pt2 = PointWorldToGraph(line.EndPoint);

            g.DrawLine(pen, pt1, pt2);
        }

        private void DrawArc(Graphics g, Arc arc, Pen pen)
        {
            var center = PointWorldToGraph(arc.Center);
            var radius = LengthWorldToGui(arc.Radius);
            var diameter = radius * 2.0f;

            var startAngle = arc.IsReversed
                ? -(float)Angle.ToDegrees(arc.EndAngle)
                : -(float)Angle.ToDegrees(arc.StartAngle);

            g.DrawArc(
                pen,
                center.X - radius,
                center.Y - radius,
                diameter,
                diameter,
                startAngle,
                -(float)Angle.ToDegrees(arc.SweepAngle()));
        }

        private void DrawCircle(Graphics g, Circle circle, Pen pen)
        {
            var center = PointWorldToGraph(circle.Center);
            var radius = LengthWorldToGui(circle.Radius);
            var diameter = radius * 2.0f;

            g.DrawEllipse(pen,
                center.X - radius,
                center.Y - radius,
                diameter,
                diameter);
        }

        private void DrawPoint(Graphics g, Vector pt, Pen pen)
        {
            var pt1 = PointWorldToGraph(pt);

            g.DrawLine(pen, pt1.X - 3, pt1.Y, pt1.X + 3, pt1.Y);
            g.DrawLine(pen, pt1.X, pt1.Y - 3, pt1.X, pt1.Y + 3);
        }

        private void DrawPoint(Graphics g, Vector pt, Pen pen, Brush fillBrush)
        {
            var pt1 = PointWorldToGraph(pt);
            var rect = new RectangleF(pt1.X - 3, pt1.Y - 3, 6, 6);

            g.FillEllipse(fillBrush, rect);
            g.DrawEllipse(pen, rect);
        }

        private void DrawIntersections(Graphics g)
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                var entity1 = Entities[i];

                if (entity1.Type != EntityType.Line)
                    continue;

                for (int j = i + 1; j < Entities.Count; ++j)
                {
                    var entity2 = Entities[j];

                    if (entity2.Type != EntityType.Line)
                        continue;

                    var line1 = (Line)entity1;
                    var line2 = (Line)entity2;

                    Vector pt;

                    if (line1.Intersects(line2, out pt))
                        DrawPoint(g, pt, Pens.Yellow, Brushes.Red);
                }
            }
        }

        public void ZoomToFit(bool redraw = true)
        {
            ZoomToArea(Entities.GetBoundingBox(), redraw);
        }
    }
}
