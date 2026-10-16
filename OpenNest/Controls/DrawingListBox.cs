using System;
using System.Drawing;
using System.Windows.Forms;
using Size = System.Drawing.Size;

namespace OpenNest.Controls
{
    public class DrawingListBox : ListBox
    {
        private const int WM_ERASEBKGND = 0x0014;

        private readonly Size imageSize;
        private readonly Font nameFont;
        private Point lastClickLocation;
        private Drawing pressedDrawing;

        public DrawingListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 85;

            imageSize = new Size(ItemHeight, ItemHeight - 10);
            nameFont = new Font(Font.FontFamily, 10, FontStyle.Bold);

            var menu = new ContextMenuStrip();
            var deleteItem = new ToolStripMenuItem("Delete");
            deleteItem.Click += (s, e) =>
            {
                if (SelectedItem is Drawing drawing)
                    DeleteRequested?.Invoke(this, drawing);
            };
            var saveToLibraryItem = new ToolStripMenuItem("Save to Drawing Library...");
            saveToLibraryItem.Click += (s, e) =>
            {
                if (SelectedItem is Drawing drawing)
                    SaveToLibraryRequested?.Invoke(this, drawing);
            };
            var saveToLibrarySeparator = new ToolStripSeparator();
            menu.ShowItemToolTips = true;
            menu.Opening += (s, e) =>
            {
                if (SelectedItem == null)
                {
                    e.Cancel = true;
                    return;
                }

                // Only hosts that handle the request offer it.
                saveToLibraryItem.Visible = saveToLibrarySeparator.Visible = SaveToLibraryRequested != null;
                saveToLibraryItem.Enabled = SaveToLibraryUnavailableReason == null;
                saveToLibraryItem.ToolTipText = SaveToLibraryUnavailableReason ?? "";
            };
            menu.Items.Add(saveToLibraryItem);
            menu.Items.Add(saveToLibrarySeparator);
            menu.Items.Add(deleteItem);
            ContextMenuStrip = menu;
        }

        public event EventHandler<Drawing> DeleteRequested;

        /// <summary>Raised when Save to Drawing Library is chosen for the selected drawing.</summary>
        public event EventHandler<Drawing> SaveToLibraryRequested;

        /// <summary>Why Save to Drawing Library is disabled, shown as its tooltip; null enables it.</summary>
        public string SaveToLibraryUnavailableReason { get; set; }

        /// <summary>
        /// Raised when the left button is pressed on a drawing's row.
        /// </summary>
        public event EventHandler<Drawing> DrawingPickedUp;

        /// <summary>
        /// Raised when a drawing dragged out of the list was accepted by a drop target.
        /// </summary>
        public event EventHandler<Drawing> DrawingDropped;

        public Units Units { get; set; }

        public bool HideDepletedParts { get; set; }

        public bool HideQuantity { get; set; }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index >= Items.Count || e.Index <= -1)
                return;

            var dwg = Items[e.Index] as Drawing;

            if (dwg == null)
                return;

            var isSelected = (e.State & DrawItemState.Selected) != 0;
            Brush bgBrush;

            if (!HideQuantity && dwg.Quantity.Nested > 0 && dwg.Quantity.Remaining == 0)
                bgBrush = SystemBrushes.Info;
            else
                bgBrush = Brushes.White;

            e.Graphics.FillRectangle(bgBrush, e.Bounds);

            if (isSelected)
            {
                var borderRect = new Rectangle(
                    e.Bounds.X,
                    e.Bounds.Y,
                    e.Bounds.Width - 1,
                    e.Bounds.Height - 1
                );
                using var borderPen = new Pen(SystemColors.Highlight, 2);
                e.Graphics.DrawRectangle(borderPen, borderRect);
            }

            if (!HideQuantity && dwg.Quantity.Required > 0)
            {
                var barWidth = 4;
                var barColor =
                    dwg.Quantity.Nested >= dwg.Quantity.Required
                        ? Color.FromArgb(76, 175, 80)
                        : Color.FromArgb(255, 152, 0);
                using var barBrush = new SolidBrush(barColor);
                e.Graphics.FillRectangle(
                    barBrush,
                    e.Bounds.X,
                    e.Bounds.Y,
                    barWidth,
                    e.Bounds.Height
                );
            }

            var pt = new PointF(5, e.Bounds.Y + 5);

            var brush = new SolidBrush(dwg.Color);
            var pen = new Pen(ColorScheme.Default.GetPartOutlineColor(dwg.Color));

            using var img = dwg.Program.GetImage(imageSize, pen, brush);

            pen.Dispose();
            brush.Dispose();

            e.Graphics.DrawImage(img, pt);

            pt.X += imageSize.Width + 10;

            var textBrush = Brushes.Black;
            var detailBrush = Brushes.Gray;

            e.Graphics.DrawString(dwg.Name, nameFont, textBrush, pt);

            var bounds = dwg.Program.BoundingBox();
            var text2 = bounds.Size.ToString(4);
            var text3 = string.Format(
                "{0} sq/{1}",
                System.Math.Round(dwg.Area, 4),
                UnitsHelper.GetShortString(Units)
            );

            if (HideQuantity)
            {
                pt.Y += 22;
                e.Graphics.DrawString(text2, Font, detailBrush, pt);
                pt.Y += 18;
                e.Graphics.DrawString(text3, Font, detailBrush, pt);
            }
            else
            {
                var text1 = string.Format(
                    "{0} of {1} nested",
                    dwg.Quantity.Nested,
                    dwg.Quantity.Required
                );
                pt.Y += 22;
                e.Graphics.DrawString(text1, Font, detailBrush, pt);
                pt.Y += 18;
                e.Graphics.DrawString(text2, Font, detailBrush, pt);
                pt.Y += 18;
                e.Graphics.DrawString(text3, Font, detailBrush, pt);
            }

            using var separatorPen = new Pen(Color.LightGray);
            var separatorY = e.Bounds.Bottom - 1;
            e.Graphics.DrawLine(separatorPen, e.Bounds.X, separatorY, e.Bounds.Right, separatorY);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var drawing = pressedDrawing;

            if (drawing == null || e.Button != MouseButtons.Left)
                return;

            if (e.Location.DistanceTo(lastClickLocation) <= 3)
                return;

            pressedDrawing = null;

            if (DoDragDrop(drawing, DragDropEffects.Copy) != DragDropEffects.None)
                DrawingDropped?.Invoke(this, drawing);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            lastClickLocation = e.Location;

            // Pick the part up on the press. Waiting for Click loses the pickup
            // whenever the mouse moves a few pixels first: the drag that starts
            // takes mouse capture and the list never raises Click.
            pressedDrawing =
                e.Button == MouseButtons.Left && e.Clicks == 1 ? DrawingAt(e.Location) : null;

            if (pressedDrawing != null)
                DrawingPickedUp?.Invoke(this, pressedDrawing);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressedDrawing = null;
            base.OnMouseUp(e);
        }

        /// <summary>
        /// The drawing whose row contains <paramref name="location"/>, or null
        /// when the point is in the empty area below the last row.
        /// </summary>
        internal Drawing DrawingAt(Point location)
        {
            var index = IndexFromPoint(location);

            if (index < 0 || index >= Items.Count)
                return null;

            return GetItemRectangle(index).Contains(location) ? Items[index] as Drawing : null;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND)
            {
                var itemBottom = 0;

                if (Items.Count > 0)
                {
                    var lastVisible = System.Math.Min(
                        TopIndex + (ClientSize.Height / ItemHeight),
                        Items.Count - 1
                    );
                    itemBottom = GetItemRectangle(lastVisible).Bottom;
                }

                if (itemBottom < ClientSize.Height)
                {
                    using var g = Graphics.FromHdc(m.WParam);
                    g.FillRectangle(
                        Brushes.White,
                        0,
                        itemBottom,
                        ClientSize.Width,
                        ClientSize.Height - itemBottom
                    );
                }

                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }

    public static class PointExtensions
    {
        public static double DistanceTo(this Point pt1, Point pt2)
        {
            var x = pt2.X - pt1.X;
            var y = pt2.Y - pt1.Y;

            return System.Math.Sqrt(x * x + y * y);
        }
    }
}
