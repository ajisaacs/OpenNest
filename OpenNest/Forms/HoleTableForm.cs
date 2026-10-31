using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Dimensions;

namespace OpenNest.Forms
{
    /// <summary>
    /// A small tool window listing the current drawing's round holes by diameter, with the
    /// quantity of each and the color its holes are drawn in on the CAD view.
    /// </summary>
    public class HoleTableForm : Form
    {
        // Bright on the CAD view's dark background and clear of the reserved colors: magenta
        // (etch), cyan (display), yellow (bends) and white (ordinary cut entities).
        private static readonly Color[] Palette =
        {
            Color.FromArgb(255, 85, 85),
            Color.FromArgb(90, 220, 90),
            Color.FromArgb(80, 160, 255),
            Color.FromArgb(255, 170, 40),
            Color.FromArgb(0, 205, 175),
            Color.FromArgb(190, 140, 255),
            Color.FromArgb(255, 140, 190),
            Color.FromArgb(175, 215, 60),
            Color.FromArgb(205, 135, 85),
        };

        private readonly DataGridView grid;
        private readonly Label summary;

        public HoleTableForm()
        {
            Text = "Hole Table";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(240, 260);
            MinimumSize = new Size(200, 150);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
                TabStop = false,
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Color",
                HeaderText = "",
                FillWeight = 20,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Size",
                HeaderText = "Diameter",
                FillWeight = 50,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Quantity",
                HeaderText = "Qty",
                FillWeight = 30,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight },
            });

            summary = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                Padding = new Padding(4, 2, 4, 2),
                ForeColor = SystemColors.GrayText,
            };

            Controls.Add(grid);
            Controls.Add(summary);
        }

        /// <summary>The color of the hole size at <paramref name="index"/> in the table.</summary>
        public static Color ColorFor(int index)
        {
            if (index < Palette.Length)
                return Palette[index];

            // Past the palette, step the hue by the golden angle so neighbors stay apart.
            var hue = (index * 137.508) % 360;
            return FromHsv(hue, 0.6, 1.0);
        }

        /// <summary>
        /// Shows <paramref name="sizes"/> and returns each hole entity's color for the CAD view.
        /// </summary>
        public IReadOnlyDictionary<Guid, Color> ShowSizes(IReadOnlyList<HoleSize> sizes, string drawingName)
        {
            var colors = new Dictionary<Guid, Color>();
            grid.Rows.Clear();
            for (var i = 0; i < sizes.Count; i++)
            {
                var color = ColorFor(i);
                var row = grid.Rows[grid.Rows.Add("", sizes[i].DiameterText, sizes[i].Quantity)];
                var swatch = row.Cells[0].Style;
                swatch.BackColor = color;
                swatch.SelectionBackColor = color;
                foreach (var id in sizes[i].EntityIds)
                    colors[id] = color;
            }
            grid.ClearSelection();

            var total = sizes.Sum(s => s.Quantity);
            Text = string.IsNullOrEmpty(drawingName) ? "Hole Table" : $"Hole Table - {drawingName}";
            summary.Text = sizes.Count == 0
                ? "No round holes. Slots and other shapes are not listed."
                : $"{total} hole{(total == 1 ? "" : "s")}, {sizes.Count} size{(sizes.Count == 1 ? "" : "s")}. "
                    + "Round holes only, in drawing units.";
            return colors;
        }

        private static Color FromHsv(double hue, double saturation, double value)
        {
            var c = value * saturation;
            var x = c * (1 - System.Math.Abs((hue / 60) % 2 - 1));
            var m = value - c;
            var (r, g, b) = (int)(hue / 60) switch
            {
                0 => (c, x, 0.0),
                1 => (x, c, 0.0),
                2 => (0.0, c, x),
                3 => (0.0, x, c),
                4 => (x, 0.0, c),
                _ => (c, 0.0, x),
            };
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }
    }
}
