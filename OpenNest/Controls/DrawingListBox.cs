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
        }

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

            if (isSelected)
                bgBrush = SystemBrushes.Highlight;
            else if (!HideQuantity && dwg.Quantity.Nested > 0 && dwg.Quantity.Remaining == 0)
                bgBrush = SystemBrushes.Info;
            else
                bgBrush = Brushes.White;

            e.Graphics.FillRectangle(bgBrush, e.Bounds);

            var pt = new PointF(5, e.Bounds.Y + 5);

            var brush = new SolidBrush(dwg.Color);
            var pen = new Pen(ControlPaint.Dark(dwg.Color));

            var img = dwg.Program.GetImage(imageSize, pen, brush);

            pen.Dispose();
            brush.Dispose();

            e.Graphics.DrawImage(img, pt);

            pt.X += imageSize.Width + 10;

            var textBrush = isSelected ? SystemBrushes.HighlightText : Brushes.Black;
            var detailBrush = isSelected ? SystemBrushes.HighlightText : Brushes.Gray;

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
