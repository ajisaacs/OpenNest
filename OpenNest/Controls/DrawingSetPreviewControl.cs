using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    /// <summary>
    /// Shows every drawing of a set side by side at one scale, packed by
    /// <see cref="DrawingSetPreviewLayout"/> with a label under each. The layout is repacked
    /// to fit whenever the control is resized; the wheel zooms, the middle button pans and a
    /// middle double-click fits the set again.
    /// </summary>
    public class DrawingSetPreviewControl : PlateView
    {
        private const int MinCellPixels = 90;

        private readonly List<(Part Part, string Label)> items = new();
        private DrawingSetPreviewLayout.Result layout;
        private string[] infoLines;

        public DrawingSetPreviewControl()
        {
            DrawOrigin = false;
            DrawBounds = false;
            DrawPartNumbers = false;
            AllowPan = true;
            AllowSelect = false;
            AllowZoom = true;
            AllowDrop = false;
            BackColor = Color.White;
            // A large set in millimeters fits at well under the default floor of 0.3 px per unit.
            ViewScaleMin = 1e-5f;
        }

        /// <summary>The placed parts, in the order they were given.</summary>
        internal IReadOnlyList<Part> Parts => items.Select(i => i.Part).ToList();

        internal DrawingSetPreviewLayout.Result Layout => layout;

        public void SetInfo(params string[] lines)
        {
            infoLines = lines;
            Invalidate();
        }

        /// <summary>Replaces the shown drawings; each is labeled with its text.</summary>
        public void ShowDrawings(IEnumerable<(Drawing Drawing, string Label)> drawings)
        {
            Plate.Parts.Clear();
            Plate.Size = new Geometry.Size(0, 0);
            items.Clear();
            layout = null;

            foreach (var (drawing, label) in drawings)
            {
                if (!CanShow(drawing))
                    continue;
                var part = new Part(drawing, Vector.Zero);
                Plate.Parts.Add(part);
                items.Add((part, label ?? ""));
            }

            Arrange();
        }

        /// <summary>False for a drawing with no geometry to show; <see cref="ShowDrawings"/> skips it.</summary>
        public static bool CanShow(Drawing drawing) => drawing?.Program?.Codes.Count > 0;

        public override void ZoomToFit(bool redraw = true)
        {
            if (layout == null)
            {
                if (redraw)
                    Invalidate();
                return;
            }

            ZoomToArea(0, -layout.Height, layout.Width, layout.Height, redraw);
        }

        protected override void OnResize(System.EventArgs e)
        {
            base.OnResize(e);
            if (items.Count > 0)
                Arrange();
        }

        private void Arrange()
        {
            if (items.Count == 0 || Width <= BorderWidth || Height <= BorderWidth)
            {
                Invalidate();
                return;
            }

            var sizes = items.Select(i => (i.Part.BoundingBox.Length, i.Part.BoundingBox.Width)).ToList();
            var labelPixels = Font.Height + 2;
            layout = DrawingSetPreviewLayout.Arrange(sizes, Width - BorderWidth, Height - BorderWidth, labelPixels, MinCellPixels);

            foreach (var placement in layout.Placements)
            {
                var part = items[placement.Index].Part;
                var box = part.BoundingBox;
                part.Offset(placement.PartX - box.X, placement.PartY - box.Y);
            }

            // Parts were moved directly, so the cached display geometry must be rebuilt.
            Plate.Parts.Clear();
            foreach (var item in items)
                Plate.Parts.Add(item.Part);
            ZoomToFit();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

            e.Graphics.TranslateTransform(origin.X, origin.Y);
            Renderer.DrawPlate(e.Graphics);
            Renderer.DrawParts(e.Graphics);
            PaintLabels(e.Graphics);
            e.Graphics.ResetTransform();

            PaintInfo(e.Graphics);
        }

        private void PaintLabels(Graphics g)
        {
            if (layout == null)
                return;

            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };

            foreach (var placement in layout.Placements)
            {
                var corner = PointWorldToGraph(placement.CellX, placement.LabelTop);
                var rect = new RectangleF(corner.X, corner.Y + 1,
                    LengthWorldToGui(placement.CellWidth), System.Math.Max(LengthWorldToGui(layout.LabelHeight), Font.Height));
                g.DrawString(items[placement.Index].Label, Font, Brushes.Black, rect, format);
            }
        }

        private void PaintInfo(Graphics g)
        {
            if (infoLines == null)
                return;

            var lineHeight = Font.GetHeight(g) + 1;
            var y = 4f;

            foreach (var line in infoLines)
            {
                if (string.IsNullOrEmpty(line))
                    continue;
                g.DrawString(line, Font, Brushes.Black, 4, y);
                y += lineHeight;
            }
        }
    }
}
