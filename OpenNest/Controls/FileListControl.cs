// OpenNest/Controls/FileListControl.cs
using OpenNest.Bending;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class FileListItem
    {
        public string Name { get; set; }
        public string Customer { get; set; }
        public int Quantity { get; set; } = 1;
        public string Path { get; set; }
        public List<Entity> Entities { get; set; } = new();
        public List<Entity> OriginalEntities { get; set; }
        public List<Bend> Bends { get; set; } = new();
        public Box Bounds { get; set; }
        public int EntityCount { get; set; }
    }

    public class FileListControl : Control
    {
        private readonly List<FileListItem> items = new();
        private int selectedIndex = -1;
        private int hoveredIndex = -1;
        private int scrollOffset;
        private const int ItemHeight = 48;
        private const int AccentBarWidth = 3;

        private readonly SolidBrush selectedBrush = new SolidBrush(Color.FromArgb(230, 238, 255));
        private readonly SolidBrush hoveredBrush = new SolidBrush(Color.FromArgb(242, 245, 250));
        private readonly SolidBrush accentBrush = new SolidBrush(Color.FromArgb(60, 120, 216));
        private readonly Pen separatorPen = new Pen(Color.FromArgb(230, 230, 230));
        private readonly SolidBrush emptyStateBrush = new SolidBrush(Color.FromArgb(160, 160, 160));

        private readonly VScrollBar scrollBar = new VScrollBar();

        public event EventHandler<int> SelectedIndexChanged;
        public event EventHandler<FileListItem> ItemRightClicked;

        public FileListControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);

            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            scrollBar.Dock = DockStyle.Right;
            scrollBar.Visible = false;
            scrollBar.Scroll += (s, e) => { scrollOffset = e.NewValue; Invalidate(); };
            Controls.Add(scrollBar);
        }

        public IReadOnlyList<FileListItem> Items => items;
        public int SelectedIndex => selectedIndex;

        public FileListItem SelectedItem =>
            selectedIndex >= 0 && selectedIndex < items.Count
                ? items[selectedIndex]
                : null;

        public void AddItem(FileListItem item)
        {
            var index = items.BinarySearch(item, Comparer<FileListItem>.Create(
                (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)));
            if (index < 0) index = ~index;
            items.Insert(index, item);

            if (items.Count == 1)
            {
                selectedIndex = 0;
                SelectedIndexChanged?.Invoke(this, selectedIndex);
            }
            else if (selectedIndex >= 0 && index <= selectedIndex)
            {
                selectedIndex++;
            }
            UpdateScrollBar();
            Invalidate();
        }

        public void RemoveAt(int index)
        {
            items.RemoveAt(index);
            if (selectedIndex >= items.Count)
                selectedIndex = items.Count - 1;
            UpdateScrollBar();
            Invalidate();
            SelectedIndexChanged?.Invoke(this, selectedIndex);
        }

        public void Clear()
        {
            items.Clear();
            selectedIndex = -1;
            scrollOffset = 0;
            UpdateScrollBar();
            Invalidate();
        }

        public void InsertItems(int index, IEnumerable<FileListItem> newItems)
        {
            items.InsertRange(index, newItems);
            Invalidate();
        }

        private void UpdateScrollBar()
        {
            var totalHeight = items.Count * ItemHeight;
            if (totalHeight > Height)
            {
                scrollBar.Maximum = totalHeight;
                scrollBar.LargeChange = Height;
                scrollBar.SmallChange = ItemHeight;
                scrollBar.Visible = true;
            }
            else
            {
                scrollBar.Visible = false;
                scrollOffset = 0;
            }
        }

        private void EnsureVisible(int index)
        {
            if (index < 0 || index >= items.Count) return;
            var itemTop = index * ItemHeight;
            var itemBottom = itemTop + ItemHeight;

            if (itemTop < scrollOffset)
                scrollOffset = itemTop;
            else if (itemBottom > scrollOffset + Height)
                scrollOffset = itemBottom - Height;

            if (scrollBar.Visible)
                scrollBar.Value = System.Math.Min(scrollOffset, scrollBar.Maximum - scrollBar.LargeChange + 1);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollBar();
        }

        public void ProcessArrowKey(Keys keyData)
        {
            if (items.Count == 0) return;

            var newIndex = selectedIndex;
            if (keyData == Keys.Down)
                newIndex = System.Math.Min(selectedIndex + 1, items.Count - 1);
            else if (keyData == Keys.Up)
                newIndex = System.Math.Max(selectedIndex - 1, 0);
            else
                return;

            if (newIndex != selectedIndex)
            {
                selectedIndex = newIndex;
                EnsureVisible(selectedIndex);
                Invalidate();
                SelectedIndexChanged?.Invoke(this, selectedIndex);
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                ProcessArrowKey(e.KeyCode);
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (items.Count == 0)
            {
                DrawEmptyState(g);
                return;
            }

            var boldFont = new Font(Font, FontStyle.Bold);
            var smallFont = new Font("Segoe UI", 8f);
            var mutedBrush = new SolidBrush(Color.FromArgb(130, 130, 130));
            var textBrush = new SolidBrush(ForeColor);
            var contentWidth = scrollBar.Visible ? Width - scrollBar.Width : Width;

            for (var i = 0; i < items.Count; i++)
            {
                var y = i * ItemHeight - scrollOffset;
                if (y + ItemHeight < 0 || y > Height) continue;

                var item = items[i];
                var rect = new Rectangle(0, y, contentWidth, ItemHeight);

                // Background
                if (i == selectedIndex)
                    g.FillRectangle(selectedBrush, rect);
                else if (i == hoveredIndex)
                    g.FillRectangle(hoveredBrush, rect);

                // Accent bar
                if (i == selectedIndex)
                    g.FillRectangle(accentBrush, 0, y, AccentBarWidth, ItemHeight);

                // Name
                var nameRect = new Rectangle(AccentBarWidth + 8, y + 6, contentWidth - 70, 20);
                TextRenderer.DrawText(g, item.Name, boldFont, nameRect, ForeColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                // Dimensions + entity count
                var bounds = item.Bounds;
                var dimText = bounds != null
                    ? $"{bounds.Width:0.#} x {bounds.Length:0.#} — {item.EntityCount} entities"
                    : $"{item.EntityCount} entities";
                var dimRect = new Rectangle(AccentBarWidth + 8, y + 26, contentWidth - 70, 16);
                TextRenderer.DrawText(g, dimText, smallFont, dimRect, Color.FromArgb(130, 130, 130), TextFormatFlags.Left);

                // Quantity badge
                var qtyText = $"x{item.Quantity}";
                var qtyRect = new Rectangle(contentWidth - 50, y + 12, 40, 24);
                TextRenderer.DrawText(g, qtyText, Font, qtyRect, Color.FromArgb(100, 100, 100), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

                // Separator
                if (i < items.Count - 1)
                    g.DrawLine(separatorPen, AccentBarWidth + 8, y + ItemHeight - 1, contentWidth - 8, y + ItemHeight - 1);
            }

            boldFont.Dispose();
            smallFont.Dispose();
            mutedBrush.Dispose();
            textBrush.Dispose();
        }

        private void DrawEmptyState(Graphics g)
        {
            var text = "Drop DXF files here\nor click Add Files...";
            var size = g.MeasureString(text, Font);
            var x = (Width - size.Width) / 2;
            var y = (Height - size.Height) / 2;
            g.DrawString(text, Font, emptyStateBrush, x, y,
                new StringFormat { Alignment = StringAlignment.Center });
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var index = GetIndexAt(e.Y);
            if (index < 0 || index >= items.Count) return;

            if (e.Button == MouseButtons.Right)
            {
                selectedIndex = index;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, selectedIndex);
                ItemRightClicked?.Invoke(this, items[index]);
                return;
            }

            if (index != selectedIndex)
            {
                selectedIndex = index;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, selectedIndex);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = GetIndexAt(e.Y);
            if (index != hoveredIndex)
            {
                hoveredIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoveredIndex = -1;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var maxScroll = System.Math.Max(0, items.Count * ItemHeight - Height);
            scrollOffset = System.Math.Max(0, System.Math.Min(maxScroll, scrollOffset - e.Delta));
            if (scrollBar.Visible)
                scrollBar.Value = System.Math.Min(scrollOffset, scrollBar.Maximum - scrollBar.LargeChange + 1);
            Invalidate();
        }

        private int GetIndexAt(int y) => (y + scrollOffset) / ItemHeight;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                selectedBrush.Dispose();
                hoveredBrush.Dispose();
                accentBrush.Dispose();
                separatorPen.Dispose();
                emptyStateBrush.Dispose();
                scrollBar.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
