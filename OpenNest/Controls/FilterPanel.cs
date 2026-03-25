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
        private readonly CheckedListBox colorsList;
        private readonly CheckedListBox lineTypesList;
        private readonly ListBox bendLinesList;

        private List<Entity> currentEntities;
        private List<Bend> currentBends;

        public event EventHandler FilterChanged;
        public event EventHandler<int> BendLineSelected;
        public event EventHandler<int> BendLineRemoved;

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
                Text = "Remove Selected",
                Dock = DockStyle.Bottom,
                Height = 20,
                Font = new Font("Segoe UI", 8f)
            };
            bendDeleteLink.LinkClicked += (s, e) =>
            {
                if (bendLinesList.SelectedIndex >= 0)
                    BendLineRemoved?.Invoke(this, bendLinesList.SelectedIndex);
            };

            bendLinesPanel.ContentPanel.Controls.Add(bendLinesList);
            bendLinesPanel.ContentPanel.Controls.Add(bendDeleteLink);

            // Line Types
            lineTypesPanel = new CollapsiblePanel
            {
                HeaderText = "Line Types (0)",
                Dock = DockStyle.Top,
                ExpandedHeight = 100,
                IsExpanded = false
            };
            lineTypesList = CreateCheckedList();
            lineTypesPanel.ContentPanel.Controls.Add(lineTypesList);

            // Colors
            colorsPanel = new CollapsiblePanel
            {
                HeaderText = "Colors (0)",
                Dock = DockStyle.Top,
                ExpandedHeight = 100,
                IsExpanded = false
            };
            colorsList = CreateCheckedList();
            colorsList.DrawMode = DrawMode.OwnerDrawFixed;
            colorsList.ItemHeight = 20;
            colorsList.DrawItem += ColorsList_DrawItem;
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
                BeginInvoke((Action)(() => FilterChanged?.Invoke(this, EventArgs.Empty)));
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
                .Select(e => e.Layer.Name)
                .Distinct();
            foreach (var layer in layers)
                layersList.Items.Add(layer, true); // checked = visible

            layersPanel.HeaderText = $"Layers ({layersList.Items.Count})";

            // Colors
            colorsList.Items.Clear();
            var colors = entities
                .Select(e => e.Color.ToArgb())
                .Distinct()
                .Select(argb => new ColorItem(Color.FromArgb(argb)));
            foreach (var color in colors)
                colorsList.Items.Add(color, true); // checked = visible

            colorsPanel.HeaderText = $"Colors ({colorsList.Items.Count})";

            // Line Types
            lineTypesList.Items.Clear();
            var lineTypes = entities
                .Select(e => e.LineTypeName ?? "Continuous")
                .Distinct();
            foreach (var lt in lineTypes)
                lineTypesList.Items.Add(lt, true); // checked = visible

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
                if (!colorsList.GetItemChecked(i))
                    hiddenColors.Add(((ColorItem)colorsList.Items[i]).Argb);
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

        private void ColorsList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();

            var colorItem = (ColorItem)colorsList.Items[e.Index];
            var swatchRect = new Rectangle(e.Bounds.Left + 20, e.Bounds.Top + 2, 16, e.Bounds.Height - 4);

            using (var brush = new SolidBrush(colorItem.Color))
                e.Graphics.FillRectangle(brush, swatchRect);
            e.Graphics.DrawRectangle(Pens.Gray, swatchRect);

            e.DrawFocusRectangle();
        }
    }

    public class ColorItem
    {
        public int Argb { get; }
        public Color Color { get; }

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
