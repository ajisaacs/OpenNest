using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest
{
    public class LayoutPart : IPart
    {
        private static Font programIdFont;
        private static Color selectedColor;
        private static Pen selectedPen;
        private static Brush selectedBrush;

        private Color color;
        private Brush brush;
        private Pen pen;

        public readonly Part BasePart;

        static LayoutPart()
        {
            programIdFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold | FontStyle.Underline);
            SelectedColor = Color.FromArgb(90, 150, 200, 255);
        }

        private LayoutPart(Part part)
        {
            this.BasePart = part;

            if (part.BaseDrawing.Color.IsEmpty)
                part.BaseDrawing.Color = Color.FromArgb(130, 204, 130);

            Color = part.BaseDrawing.Color;
        }

        internal bool IsDirty { get; set; }

        public bool IsSelected { get; set; }
        
        public GraphicsPath Path { get; private set; }

        public Color Color
        {
            get { return color; }
            set
            {
                color = value;

                if (brush != null)
                    brush.Dispose();

                brush = new SolidBrush(value);

                if (pen != null)
                    pen.Dispose();

                pen = new Pen(ControlPaint.Dark(value));
            }
        }

        public void Draw(Graphics g)
        {
            if (IsSelected)
            {
                g.FillPath(selectedBrush, Path);
                g.DrawPath(selectedPen, Path);
            }
            else
            {
                g.FillPath(brush, Path);
                g.DrawPath(pen, Path);
            }
        }

        public void Draw(Graphics g, string id)
        {
            if (IsSelected)
            {
                g.FillPath(selectedBrush, Path);
                g.DrawPath(selectedPen, Path);
            }
            else
            {
                g.FillPath(brush, Path);
                g.DrawPath(pen, Path);
            }

            var pt = Path.PointCount > 0 ? Path.PathPoints[0] : PointF.Empty;

            g.DrawString(id, programIdFont, Brushes.Black, pt.X, pt.Y);
        }

        public void Update(DrawControl plateView)
        {
            Path = GraphicsHelper.GetGraphicsPath(BasePart.Program, BasePart.Location);
            Path.Transform(plateView.Matrix);
            IsDirty = false;
        }

        public static LayoutPart Create(Part part, PlateView plateView)
        {
            var layoutPart = new LayoutPart(part);
            layoutPart.Update(plateView);

            return layoutPart;
        }

        public static Color SelectedColor
        {
            get { return selectedColor; }
            set
            {
                selectedColor = value;

                if (selectedBrush != null)
                    selectedBrush.Dispose();

                selectedBrush = new SolidBrush(value);

                if (selectedPen != null)
                    selectedPen.Dispose();

                selectedPen = new Pen(ControlPaint.Dark(value));
            }
        }

        public Vector Location
        {
            get { return BasePart.Location; }
            set
            {
                BasePart.Location = value;
                IsDirty = true;
            }
        }

        public double Rotation
        {
            get { return BasePart.Rotation; }
        }

        public void Rotate(double angle)
        {
            BasePart.Rotate(angle);
            IsDirty = true;
        }

        public void Rotate(double angle, Vector origin)
        {
            BasePart.Rotate(angle, origin);
            IsDirty = true;
        }

        public void Offset(double x, double y)
        {
            BasePart.Offset(x, y);
            IsDirty = true;
        }

        public void Offset(Vector voffset)
        {
            BasePart.Offset(voffset);
            IsDirty = true;
        }

        public Box BoundingBox
        {
            get { return BasePart.BoundingBox; }
        }

        public double Left
        {
            get { return BasePart.Left; }
        }

        public double Right
        {
            get { return BasePart.Right; }
        }

        public double Top
        {
            get { return BasePart.Top; }
        }

        public double Bottom
        {
            get { return BasePart.Bottom; }
        }

        public void UpdateBounds()
        {
            BasePart.UpdateBounds();
        }

        public void Update()
        {
            Color = BasePart.BaseDrawing.Color;
        }
    }
}
