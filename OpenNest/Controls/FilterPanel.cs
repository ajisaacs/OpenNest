using OpenNest.Bending;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class FilterPanel : Panel
    {
        private readonly CollapsiblePanel layersPanel;
        private readonly CollapsiblePanel colorsPanel;
        private readonly CollapsiblePanel lineTypesPanel;
        private readonly CollapsiblePanel bendLinesPanel;

        private readonly CheckedListBox layersList;
        private readonly ListBox colorsList;
        private readonly CheckedListBox lineTypesList;
        private readonly ListBox bendLinesList;
        private readonly LinkLabel bendAddLink;

        private List<Entity> currentEntities;
        private List<Bend> currentBends;

        public event EventHandler FilterChanged;
        public event EventHandler<int> BendLineSelected;
        public event EventHandler<int> BendLineRemoved;
        public event EventHandler AddBendLineClicked;

        public FilterPanel()
        {
            AutoScroll = true;
            BackColor = Color.White;

            // Bend Lines
            bendLinesPanel = new CollapsiblePanel
            {
                HeaderText = "Bend Lines (0)",
                Dock = DockStyle.Top,
                ExpandedHeight = 120,
                IsExpanded = false
            };
            bendLinesList = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9f)
            };
            bendLinesList.SelectedIndexChanged += (s, e) =>
                BendLineSelected?.Invoke(this, bendLinesList.SelectedIndex);

            var bendDeleteLink = new LinkLabel
            {
                Text = "Remove",
                AutoSize = true,
                Font = new Font("Segoe UI", 8f)
            };
            bendDeleteLink.LinkClicked += (s, e) =>
            {
                if (bendLinesList.SelectedIndex >= 0)
                    BendLineRemoved?.Invoke(this, bendLinesList.SelectedIndex);
            };

            bendAddLink = new LinkLabel
            {
                Text = "Add Bend Line",
                AutoSize = true,
                Font = new Font("Segoe UI", 8f)
            };
            bendAddLink.LinkClicked += (s, e) =>
                AddBendLineClicked?.Invoke(this, EventArgs.Empty);

            var bendLinksPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 20,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            bendLinksPanel.Controls.Add(bendAddLink);
            bendLinksPanel.Controls.Add(bendDeleteLink);

            bendLinesPanel.ContentPanel.Controls.Add(bendLinesList);
            bendLinesPanel.ContentPanel.Controls.Add(bendLinksPanel);

            // Line Types
            lineTypesPanel = new CollapsiblePanel
            {
                HeaderText = "Line Types (0)",
                Dock = DockStyle.Top,
                ExpandedHeight = 100,
                IsExpanded = true
            };
            lineTypesList = CreateCheckedList();
            lineTypesPanel.ContentPanel.Controls.Add(lineTypesList);

            // Colors
            colorsPanel = new CollapsiblePanel
            {
                HeaderText = "Colors (0)",
                Dock = DockStyle.Top,
                ExpandedHeight = 100,
                IsExpanded = true
            };
            colorsList = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9f),
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 20,
                SelectionMode = SelectionMode.None
            };
            colorsList.DrawItem += ColorsList_DrawItem;
            colorsList.MouseClick += ColorsList_MouseClick;
            colorsPanel.ContentPanel.Controls.Add(colorsList);

            // Layers (always expanded)
            layersPanel = new CollapsiblePanel
            {
                HeaderText = "Layers",
                Dock = DockStyle.Top,
                ExpandedHeight = 160,
                IsExpanded = true
            };

            var checkAllPanel = new Panel { Dock = DockStyle.Top, Height = 22 };
            var checkAll = new LinkLabel { Text = "All", AutoSize = true, Location = new Point(4, 2), Font = new Font("Segoe UI", 8f) };
            var uncheckAll = new LinkLabel { Text = "None", AutoSize = true, Location = new Point(30, 2), Font = new Font("Segoe UI", 8f) };
            checkAll.LinkClicked += (s, e) => SetAllChecked(layersList, true);
            uncheckAll.LinkClicked += (s, e) => SetAllChecked(layersList, false);
            checkAllPanel.Controls.AddRange(new Control[] { checkAll, uncheckAll });

            layersList = CreateCheckedList();
            layersPanel.ContentPanel.Controls.Add(layersList);
            layersPanel.ContentPanel.Controls.Add(checkAllPanel);

            // Add panels in reverse order (Dock.Top stacks top-down)
            Controls.Add(bendLinesPanel);
            Controls.Add(lineTypesPanel);
            Controls.Add(colorsPanel);
            Controls.Add(layersPanel);
        }

        private CheckedListBox CreateCheckedList()
        {
            var list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                CheckOnClick = true,
                Font = new Font("Segoe UI", 9f)
            };
            list.ItemCheck += (s, e) =>
            {
                if (IsHandleCreated)
                    BeginInvoke((Action)(() => FilterChanged?.Invoke(this, EventArgs.Empty)));
            };
            return list;
        }

        public void LoadItem(List<Entity> entities, List<Bend> bends)
        {
            currentEntities = entities;
            currentBends = bends;

            // Layers
            layersList.Items.Clear();
            var layers = entities
                .Where(e => e.Layer != null)
                .Select(e => e.Layer)
                .GroupBy(l => l.Name)
                .Select(g => g.First());
            foreach (var layer in layers)
                layersList.Items.Add(layer.Name, layer.IsVisible);

            layersPanel.HeaderText = $"Layers ({layersList.Items.Count})";

            // Colors
            colorsList.Items.Clear();
            var colors = entities
                .Select(e => e.Color.ToArgb())
                .Distinct()
                .Select(argb => new ColorItem(Color.FromArgb(argb)));
            foreach (var color in colors)
                colorsList.Items.Add(color);

            colorsPanel.HeaderText = $"Colors ({colorsList.Items.Count})";

            // Line Types
            lineTypesList.Items.Clear();
            var lineTypes = entities
                .GroupBy(e => e.LineTypeName ?? "Continuous")
                .Select(g => new { Name = g.Key, Visible = g.Any(e => e.IsVisible) });
            foreach (var lt in lineTypes)
                lineTypesList.Items.Add(lt.Name, lt.Visible);

            lineTypesPanel.HeaderText = $"Line Types ({lineTypesList.Items.Count})";

            // Bend Lines
            bendLinesList.Items.Clear();
            if (bends != null)
            {
                foreach (var bend in bends)
                    bendLinesList.Items.Add(bend.ToString());
            }

            var bendCount = bends?.Count ?? 0;
            bendLinesPanel.HeaderText = $"Bend Lines ({bendCount})";
            bendLinesPanel.IsExpanded = bendCount > 0;
        }

        public void ApplyFilters(List<Entity> entities)
        {
            var hiddenLayers = new HashSet<string>();
            for (var i = 0; i < layersList.Items.Count; i++)
            {
                if (!layersList.GetItemChecked(i))
                    hiddenLayers.Add(layersList.Items[i].ToString());
            }

            var hiddenColors = new HashSet<int>();
            for (var i = 0; i < colorsList.Items.Count; i++)
            {
                var item = (ColorItem)colorsList.Items[i];
                if (!item.IsChecked)
                    hiddenColors.Add(item.Argb);
            }

            var hiddenLineTypes = new HashSet<string>();
            for (var i = 0; i < lineTypesList.Items.Count; i++)
            {
                if (!lineTypesList.GetItemChecked(i))
                    hiddenLineTypes.Add(lineTypesList.Items[i].ToString());
            }

            foreach (var entity in entities)
            {
                var layerVisible = entity.Layer?.Name == null || !hiddenLayers.Contains(entity.Layer.Name);
                var colorVisible = !hiddenColors.Contains(entity.Color.ToArgb());
                var ltVisible = !hiddenLineTypes.Contains(entity.LineTypeName ?? "Continuous");

                entity.IsVisible = layerVisible && colorVisible && ltVisible;
                if (entity.Layer != null)
                    entity.Layer.IsVisible = !hiddenLayers.Contains(entity.Layer.Name);
            }
        }

        private void SetAllChecked(CheckedListBox list, bool isChecked)
        {
            for (var i = 0; i < list.Items.Count; i++)
                list.SetItemChecked(i, isChecked);
        }

        private void ColorsList_MouseClick(object sender, MouseEventArgs e)
        {
            var index = colorsList.IndexFromPoint(e.Location);
            if (index < 0) return;
            var item = (ColorItem)colorsList.Items[index];
            item.IsChecked = !item.IsChecked;
            colorsList.Invalidate(colorsList.GetItemRectangle(index));
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ColorsList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.Graphics.FillRectangle(Brushes.White, e.Bounds);

            var colorItem = (ColorItem)colorsList.Items[e.Index];
            var checkSize = CheckBoxRenderer.GetGlyphSize(e.Graphics,
                System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal);
            var checkY = e.Bounds.Top + (e.Bounds.Height - checkSize.Height) / 2;
            var checkState = colorItem.IsChecked
                ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
            CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(e.Bounds.Left + 2, checkY), checkState);

            var swatchX = e.Bounds.Left + checkSize.Width + 6;
            var swatchRect = new Rectangle(swatchX, e.Bounds.Top + 2, 16, e.Bounds.Height - 4);
            using (var brush = new SolidBrush(colorItem.Color))
                e.Graphics.FillRectangle(brush, swatchRect);
            e.Graphics.DrawRectangle(Pens.Gray, swatchRect);

            TextRenderer.DrawText(e.Graphics, colorItem.ToString(), e.Font,
                new Point(swatchRect.Right + 4, e.Bounds.Top + 1), SystemColors.WindowText);
        }

        public void SetPickMode(bool active)
        {
            bendAddLink.Text = active ? "Cancel (Esc)" : "Add Bend Line";
            bendAddLink.LinkColor = active ? Color.OrangeRed : Color.Empty;
        }
    }

    public class ColorItem
    {
        public int Argb { get; }
        public Color Color { get; }
        public bool IsChecked { get; set; } = true;

        public ColorItem(Color color)
        {
            Color = color;
            Argb = color.ToArgb();
        }

        public override string ToString() => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
        public override bool Equals(object obj) => obj is ColorItem other && Argb == other.Argb;
        public override int GetHashCode() => Argb;
    }
}
