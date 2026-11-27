using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Geometry;
using Size = System.Drawing.Size;

namespace OpenNest.Controls
{
    /// <summary>
    /// The Plates tab list: one row per plate with a picture of its sheet and parts, its number,
    /// size, quantity, part count and utilization, drawn like the drawings list. Pictures are
    /// cached per plate and redrawn only when the sheet or a part's placement changes.
    /// </summary>
    public class PlateListBox : ListBox
    {
        private const int WM_ERASEBKGND = 0x0014;
        private const int DragThreshold = 3;

        private readonly Size imageSize;
        private readonly Font nameFont;
        private readonly Font quantityFont;
        private readonly ToolTip toolTip = new ToolTip();
        private readonly Dictionary<Plate, Preview> previews = new Dictionary<Plate, Preview>();
        private int toolTipIndex = -1;
        private int pressedIndex = -1;
        private Point pressedLocation;

        public PlateListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 85;
            IntegralHeight = false;

            imageSize = new Size(110, ItemHeight - 10);
            nameFont = new Font(Font.FontFamily, 10, FontStyle.Bold);
            quantityFont = new Font(Font.FontFamily, 16, FontStyle.Bold);
        }

        /// <summary>The scheme the pictures take the sheet colors from.</summary>
        public ColorScheme ColorScheme { get; set; } = ColorScheme.Default;

        /// <summary>The tooltip shown over a plate's row; null or empty shows none.</summary>
        public Func<Plate, string> ToolTipProvider { get; set; }

        /// <summary>
        /// Raised when the left button is pressed on a row and the pointer then moves past the
        /// drag threshold; the argument is the row's index.
        /// </summary>
        public event EventHandler<int> RowDragStarted;

        /// <summary>The tooltip text of the row at <paramref name="index"/>, "" when it has none.</summary>
        public string GetToolTipText(int index) =>
            index >= 0 && index < Items.Count && Items[index] is Plate plate
                ? ToolTipProvider?.Invoke(plate) ?? ""
                : "";

        /// <summary>
        /// Replaces the rows with <paramref name="plates"/>, keeping the scroll position, and
        /// forgets the pictures of plates no longer listed.
        /// </summary>
        public void SetPlates(IReadOnlyList<Plate> plates)
        {
            var topIndex = TopIndex;

            BeginUpdate();
            Items.Clear();
            Items.AddRange(plates.Cast<object>().ToArray());
            if (topIndex < Items.Count)
                TopIndex = topIndex;
            EndUpdate();

            foreach (var gone in previews.Keys.Where(plate => !plates.Contains(plate)).ToList())
            {
                previews[gone].Dispose();
                previews.Remove(gone);
            }
        }

        /// <summary>
        /// The index of the row containing <paramref name="location"/>, or -1 when the point is
        /// in the empty area below the last row.
        /// </summary>
        public int RowAt(Point location)
        {
            var index = IndexFromPoint(location);
            if (index < 0 || index >= Items.Count)
                return -1;

            return GetItemRectangle(index).Contains(location) ? index : -1;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not Plate plate)
                return;

            var g = e.Graphics;
            g.FillRectangle(Brushes.White, e.Bounds);

            if ((e.State & DrawItemState.Selected) != 0)
            {
                using var borderPen = new Pen(SystemColors.Highlight, 2);
                g.DrawRectangle(borderPen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            }

            g.DrawImage(GetPreview(plate), e.Bounds.X + 5, e.Bounds.Y + 5);

            var isSentinel = e.Index == Items.Count - 1 && plate.Parts.Count == 0;
            var pt = new PointF(e.Bounds.X + imageSize.Width + 15, e.Bounds.Y + 5);
            g.DrawString(isSentinel ? "New plate" : $"Plate {e.Index + 1}", nameFont, Brushes.Black, pt);

            var partCount = plate.Parts.Count(part => !part.BaseDrawing.IsCutOff);
            var lines = new List<string>
            {
                plate.Size.ToString(4),
                isSentinel ? "Empty" : $"{partCount} {(partCount == 1 ? "part" : "parts")}",
            };
            if (partCount > 0)
                lines.Add($"{plate.Utilization():P0} utilized");

            foreach (var line in lines)
            {
                pt.Y += line == lines[0] ? 22 : 18;
                g.DrawString(line, Font, Brushes.Gray, pt);
            }

            // How many sheets are cut from this layout, large at the right edge of the row.
            if (plate.Quantity > 1)
            {
                var quantityBounds = new RectangleF(e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Far,
                    LineAlignment = StringAlignment.Center,
                };
                g.DrawString($"×{plate.Quantity}", quantityFont, Brushes.Gray, quantityBounds, format);
            }

            using var separatorPen = new Pen(Color.LightGray);
            var separatorY = e.Bounds.Bottom - 1;
            g.DrawLine(separatorPen, e.Bounds.X, separatorY, e.Bounds.Right, separatorY);
        }

        /// <summary>The plate's picture, redrawn only when what it shows has changed.</summary>
        internal Image GetPreview(Plate plate)
        {
            var signature = Signature(plate);
            if (previews.TryGetValue(plate, out var preview))
            {
                if (preview.Signature == signature)
                    return preview.Image;
                preview.Dispose();
            }

            preview = new Preview(signature, RenderPreview(plate));
            previews[plate] = preview;
            return preview.Image;
        }

        // Everything the picture shows: the sheet, its scheme and each part's program, place and color.
        private int Signature(Plate plate)
        {
            var hash = new HashCode();
            hash.Add(plate.Size.Length);
            hash.Add(plate.Size.Width);
            hash.Add(plate.Quadrant);
            hash.Add(RuntimeHelpers.GetHashCode(plate.Outline));
            hash.Add(RuntimeHelpers.GetHashCode(ColorScheme));
            foreach (var part in plate.Parts)
            {
                hash.Add(RuntimeHelpers.GetHashCode(part.Program));
                hash.Add(part.Location.X);
                hash.Add(part.Location.Y);
                // A rotation or mirror can rewrite the program in place, keeping its reference.
                hash.Add(part.Rotation);
                hash.Add(part.BoundingBox.Length);
                hash.Add(part.BoundingBox.Width);
                hash.Add(part.BaseDrawing.Color.ToArgb());
            }

            return hash.ToHashCode();
        }

        private Bitmap RenderPreview(Plate plate)
        {
            var image = new Bitmap(imageSize.Width, imageSize.Height);
            using var g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            var length = plate.Size.Length;
            var width = plate.Size.Width;
            if (length <= 0 || width <= 0)
                return image;

            // Sheet extents around the origin as the plate view places them for this quadrant.
            var left = plate.Quadrant is 2 or 3 ? -length : 0;
            var bottom = plate.Quadrant is 3 or 4 ? -width : 0;
            const float margin = 2;
            var scale = (float)System.Math.Min(
                (imageSize.Width - 2 * margin) / length,
                (imageSize.Height - 2 * margin) / width);
            var offsetX = (imageSize.Width - (float)length * scale) / 2;
            var offsetY = (imageSize.Height + (float)width * scale) / 2;

            // World Y grows up; the sheet's lower-left corner sits at the picture's bottom-left.
            using var toDevice = new Matrix();
            toDevice.Translate(offsetX, offsetY);
            toDevice.Scale(scale, -scale);
            toDevice.Translate((float)-left, (float)-bottom);

            var gdi = ColorScheme.Gdi();
            using var sheet = SheetPath(plate, left, bottom);
            sheet.Transform(toDevice);
            g.FillPath(gdi.LayoutFillBrush, sheet);

            using var outlinePen = new Pen(Color.FromArgb(70, 70, 70), 0);
            var brushes = new Dictionary<Color, Brush>();
            var pens = new Dictionary<Color, Pen>();
            // Parts placed from one drawing at one rotation share a program, so a path is built once.
            var localPaths = new Dictionary<Program, GraphicsPath>(ReferenceEqualityComparer.Instance);
            try
            {
                foreach (var part in plate.Parts)
                {
                    if (!localPaths.TryGetValue(part.Program, out var local))
                        localPaths[part.Program] = local = part.Program.GetFillPath(Vector.Zero);
                    if (local.PointCount == 0)
                        continue;

                    using var path = (GraphicsPath)local.Clone();
                    using var place = toDevice.Clone();
                    place.Translate((float)part.Location.X, (float)part.Location.Y);
                    path.Transform(place);

                    var color = part.BaseDrawing.Color;
                    if (part.BaseDrawing.IsCutOff)
                    {
                        g.DrawPath(outlinePen, path);
                        continue;
                    }

                    if (!brushes.TryGetValue(color, out var brush))
                        brushes[color] = brush = new SolidBrush(color);
                    if (!pens.TryGetValue(color, out var pen))
                        pens[color] = pen = new Pen(ColorScheme.GetPartOutlineColor(color), 0);
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }
            }
            finally
            {
                foreach (var path in localPaths.Values)
                    path.Dispose();
                foreach (var brush in brushes.Values)
                    brush.Dispose();
                foreach (var pen in pens.Values)
                    pen.Dispose();
            }

            g.DrawPath(gdi.LayoutOutlinePen, sheet);
            return image;
        }

        // The sheet in world units: its outline and obstacles when outlined, else its rectangle.
        private static GraphicsPath SheetPath(Plate plate, double left, double bottom)
        {
            var path = new GraphicsPath(FillMode.Alternate);
            var outline = plate.Outline;
            if (outline == null)
            {
                path.AddRectangle(new RectangleF((float)left, (float)bottom,
                    (float)plate.Size.Length, (float)plate.Size.Width));
                return path;
            }

            var origin = outline.PlacementOrigin(plate.Quadrant);
            path.AddPolygon(ToPoints(outline.Points, origin));
            foreach (var obstacle in outline.Obstacles)
                path.AddPolygon(ToPoints(obstacle, origin));
            return path;
        }

        private static PointF[] ToPoints(IReadOnlyList<Vector> points, Vector origin) =>
            points.Select(p => new PointF((float)(p.X + origin.X), (float)(p.Y + origin.Y))).ToArray();

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // The row menu acts on the current plate, so a right-click picks the row first.
            if (e.Button == MouseButtons.Right)
            {
                var index = RowAt(e.Location);
                if (index >= 0)
                    SelectedIndex = index;
            }

            base.OnMouseDown(e);
            pressedLocation = e.Location;
            pressedIndex = e.Button == MouseButtons.Left && e.Clicks == 1 ? RowAt(e.Location) : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateToolTip(e.Location);

            if (pressedIndex < 0 || e.Button != MouseButtons.Left)
                return;
            if (System.Math.Abs(e.X - pressedLocation.X) <= DragThreshold
                && System.Math.Abs(e.Y - pressedLocation.Y) <= DragThreshold)
                return;

            var index = pressedIndex;
            pressedIndex = -1;
            RowDragStarted?.Invoke(this, index);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressedIndex = -1;
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            toolTipIndex = -1;
            toolTip.Hide(this);
        }

        private void UpdateToolTip(Point location)
        {
            var index = RowAt(location);
            if (index == toolTipIndex)
                return;

            toolTipIndex = index;
            var text = GetToolTipText(index);
            toolTip.SetToolTip(this, string.IsNullOrEmpty(text) ? null : text);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND)
            {
                // Rows paint themselves; only the area below the last row needs erasing. Windows
                // erases a resized window before WinForms hears the new size, so the client
                // size comes from Windows (as in the drawings list).
                GetClientRect(Handle, out var client);
                var clientWidth = client.Right - client.Left;
                var clientHeight = client.Bottom - client.Top;
                var itemBottom = Items.Count > 0 ? System.Math.Max(0, GetItemRectangle(Items.Count - 1).Bottom) : 0;
                if (itemBottom < clientHeight)
                {
                    using var g = Graphics.FromHdc(m.WParam);
                    g.FillRectangle(Brushes.White, 0, itemBottom, clientWidth, clientHeight - itemBottom);
                }

                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var preview in previews.Values)
                    preview.Dispose();
                previews.Clear();
                toolTip.Dispose();
                nameFont.Dispose();
                quantityFont.Dispose();
            }

            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        private sealed class Preview : IDisposable
        {
            public Preview(int signature, Image image)
            {
                Signature = signature;
                Image = image;
            }

            public int Signature { get; }
            public Image Image { get; }

            public void Dispose() => Image.Dispose();
        }
    }
}
