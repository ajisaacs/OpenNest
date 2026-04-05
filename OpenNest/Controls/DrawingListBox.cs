using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    using Size = System.Drawing.Size;

    public class DrawingListBox : ListBox
    {
        private readonly Size imageSize;
        private readonly Font nameFont;
        private Point lastClickLocation;

        public DrawingListBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer, true);

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
            menu.Opening += (s, e) =>
            {
                if (SelectedItem == null)
                    e.Cancel = true;
            };
            menu.Items.Add(deleteItem);
            ContextMenuStrip = menu;
        }

        public event EventHandler<Drawing> DeleteRequested;

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
                var borderRect = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                using var borderPen = new Pen(SystemColors.Highlight, 2);
                e.Graphics.DrawRectangle(borderPen, borderRect);
            }

            if (!HideQuantity && dwg.Quantity.Required > 0)
            {
                var barWidth = 4;
                var barColor = dwg.Quantity.Nested >= dwg.Quantity.Required
                    ? Color.FromArgb(76, 175, 80)
                    : Color.FromArgb(255, 152, 0);
                using var barBrush = new SolidBrush(barColor);
                e.Graphics.FillRectangle(barBrush, e.Bounds.X, e.Bounds.Y, barWidth, e.Bounds.Height);
            }

            var pt = new PointF(5, e.Bounds.Y + 5);

            var brush = new SolidBrush(dwg.Color);
            var pen = new Pen(ControlPaint.Dark(dwg.Color));

            var img = dwg.Program.GetImage(imageSize, pen, brush);

            pen.Dispose();
            brush.Dispose();

            e.Graphics.DrawImage(img, pt);

            pt.X += imageSize.Width + 10;

            var textBrush = Brushes.Black;
            var detailBrush = Brushes.Gray;

            e.Graphics.DrawString(dwg.Name, nameFont, textBrush, pt);

            var bounds = dwg.Program.BoundingBox();
            var text2 = bounds.Size.ToString(4);
            var text3 = string.Format("{0} sq/{1}", System.Math.Round(dwg.Area, 4), UnitsHelper.GetShortString(Units));

            if (HideQuantity)
            {
                pt.Y += 22;
                e.Graphics.DrawString(text2, Font, detailBrush, pt);
                pt.Y += 18;
                e.Graphics.DrawString(text3, Font, detailBrush, pt);
            }
            else
            {
                var text1 = string.Format("{0} of {1} nested", dwg.Quantity.Nested, dwg.Quantity.Required);
                pt.Y += 22;
                e.Graphics.DrawString(text1, Font, detailBrush, pt);
                pt.Y += 18;
                e.Graphics.DrawString(text2, Font, detailBrush, pt);
                pt.Y += 18;
                e.Graphics.DrawString(text3, Font, detailBrush, pt);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var item = SelectedItem as Drawing;

            if (item == null)
                return;

            if (e.Button == MouseButtons.Left && e.Location.DistanceTo(lastClickLocation) > 3)
                DoDragDrop(item, DragDropEffects.Copy);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            lastClickLocation = e.Location;
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
