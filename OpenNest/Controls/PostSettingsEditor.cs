using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using OpenNest.PostSettings;

namespace OpenNest.Controls
{
    /// <summary>
    /// Sectioned settings editor for a post-processor config described by
    /// <see cref="PostSettingsLayout"/>. Edits are held in the controls and
    /// written to the config only by <see cref="TryApply"/>, so cancelling
    /// leaves the config untouched.
    /// </summary>
    public sealed class PostSettingsEditor : UserControl
    {
        private static readonly Color HelpColor = SystemColors.GrayText;

        private readonly object config;
        private readonly IReadOnlyList<PostSettingsSection> sections;
        private readonly ListBox sectionList;
        private readonly Panel pageHost;
        private readonly List<Panel> pages = new();
        private readonly List<Label> wrapLabels = new();
        private readonly List<(PostSettingsField Field, int Section, Func<object> Read)> readers =
            new();

        public PostSettingsEditor(object config, IReadOnlyList<PostSettingsSection> sections)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.sections = sections ?? throw new ArgumentNullException(nameof(sections));

            sectionList = new ListBox
            {
                Dock = DockStyle.Left,
                Width = 170,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 34,
                IntegralHeight = false,
                TabIndex = 0,
            };
            sectionList.DrawItem += SectionList_DrawItem;
            sectionList.SelectedIndexChanged += (_, _) => ShowSection(sectionList.SelectedIndex);

            var divider = new VerticalLine { Dock = DockStyle.Left, Width = 2 };

            pageHost = new Panel { Dock = DockStyle.Fill, TabIndex = 1 };

            for (var i = 0; i < sections.Count; i++)
            {
                sectionList.Items.Add(sections[i].Name);
                var page = BuildPage(sections[i], i);
                page.Visible = false;
                pages.Add(page);
                pageHost.Controls.Add(page);
            }

            Controls.Add(pageHost);
            Controls.Add(divider);
            Controls.Add(sectionList);

            if (sections.Count > 0)
                sectionList.SelectedIndex = 0;
        }

        /// <summary>
        /// Validates every field and, only if all are valid, writes them to the
        /// config. On failure the config is unchanged, the offending section is
        /// shown, and <paramref name="error"/> describes the problem.
        /// </summary>
        public bool TryApply(out string error)
        {
            var values = new List<(PostSettingsField Field, object Value)>();

            foreach (var (field, section, read) in readers)
            {
                try
                {
                    values.Add((field, read()));
                }
                catch (FormatException ex)
                {
                    sectionList.SelectedIndex = section;
                    error = $"{field.Label}: {ex.Message}";
                    return false;
                }
            }

            foreach (var (field, value) in values)
                field.SetValue(config, value);

            error = null;
            return true;
        }

        private void ShowSection(int index)
        {
            for (var i = 0; i < pages.Count; i++)
                pages[i].Visible = i == index;
        }

        private Panel BuildPage(PostSettingsSection section, int sectionIndex)
        {
            var page = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(16, 12, 16, 12),
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = section.Name,
                AutoSize = true,
                Font = new Font(Font.FontFamily, 13f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 4),
            };
            AddSpanning(grid, title);

            if (section.Description.Length > 0)
            {
                var description = WrapLabel(section.Description, SystemColors.ControlText);
                description.Margin = new Padding(0, 0, 0, 10);
                AddSpanning(grid, description);
            }

            AddSpanning(grid, new HorizontalLine { Height = 8, Dock = DockStyle.Fill });

            foreach (var field in section.Fields)
                AddField(grid, field, sectionIndex);

            page.Controls.Add(grid);
            page.Resize += (_, _) => UpdateWrapWidths(page);
            return page;
        }

        private void AddField(TableLayoutPanel grid, PostSettingsField field, int sectionIndex)
        {
            var value = field.GetValue(config);
            Control input;
            Func<object> read;

            switch (field.Kind)
            {
                case PostSettingKind.Boolean:
                    {
                        var check = new CheckBox
                        {
                            Text = field.Label,
                            Checked = value is true,
                            AutoSize = true,
                        };
                        input = check;
                        read = () => check.Checked;
                        break;
                    }
                case PostSettingKind.Integer:
                case PostSettingKind.Decimal:
                    {
                        var number = new System.Windows.Forms.NumericUpDown
                        {
                            Minimum = (decimal)field.Minimum,
                            Maximum = (decimal)field.Maximum,
                            DecimalPlaces = field.DecimalPlaces,
                            Increment =
                                field.Kind == PostSettingKind.Integer ? 1m
                                : field.DecimalPlaces >= 2 ? 0.25m
                                : 1m,
                            Width = 120,
                            TextAlign = HorizontalAlignment.Right,
                            ThousandsSeparator = false,
                        };
                        number.Value = Clamp(number, Convert.ToDecimal(value, CultureInfo.InvariantCulture));
                        input = number;
                        read = field.Kind == PostSettingKind.Integer
                            ? () => (object)(int)number.Value
                            : () => (object)(double)number.Value;
                        break;
                    }
                case PostSettingKind.Choice:
                    {
                        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
                        combo.Items.AddRange(field.ChoiceNames);
                        combo.SelectedItem = value?.ToString();
                        input = combo;
                        read = () =>
                            Enum.Parse(field.Property.PropertyType, (string)combo.SelectedItem ?? field.ChoiceNames[0]);
                        break;
                    }
                case PostSettingKind.StringMap:
                    AddMapField(grid, field, sectionIndex, value as IDictionary<string, string>);
                    return;
                default:
                    {
                        var text = new TextBox { Text = value as string ?? "", Width = 280 };
                        input = text;
                        read = () => text.Text;
                        break;
                    }
            }

            input.Margin = new Padding(3, 10, 3, 0);
            input.Anchor = AnchorStyles.Left;

            var row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (field.Kind != PostSettingKind.Boolean)
            {
                var label = new Label
                {
                    Text = field.Label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 10, 8, 0),
                };
                grid.Controls.Add(label, 0, row);
            }
            grid.Controls.Add(input, 1, row);

            AddHelp(grid, field.Description);
            readers.Add((field, sectionIndex, read));
        }

        private void AddMapField(
            TableLayoutPanel grid,
            PostSettingsField field,
            int sectionIndex,
            IDictionary<string, string> map
        )
        {
            var label = new Label
            {
                Text = field.Label,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 12, 0, 2),
            };
            AddSpanning(grid, label);
            AddHelp(grid, field.Description, spanning: true);

            var table = new DataGridView
            {
                Dock = DockStyle.Fill,
                Height = 200,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                RowHeadersWidth = 28,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                Margin = new Padding(0, 4, 0, 0),
            };
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = field.KeyHeader, FillWeight = 65 });
            table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = field.ValueHeader, FillWeight = 35 });

            if (map != null)
            {
                foreach (var entry in map.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                    table.Rows.Add(entry.Key, entry.Value);
            }

            AddSpanning(grid, table);

            var removeButton = new Button
            {
                Text = "Remove selected",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0),
            };
            removeButton.Click += (_, _) =>
            {
                foreach (DataGridViewRow selected in table.SelectedRows)
                {
                    if (!selected.IsNewRow)
                        table.Rows.Remove(selected);
                }
            };
            AddSpanning(grid, removeButton);
            AddHelp(grid, "Type in the empty last row to add an entry.", spanning: true);

            var comparer = (map as Dictionary<string, string>)?.Comparer ?? StringComparer.Ordinal;
            readers.Add(
                (
                    field,
                    sectionIndex,
                    () =>
                    {
                        table.EndEdit();
                        var rows = table
                            .Rows.Cast<DataGridViewRow>()
                            .Where(r => !r.IsNewRow)
                            .Select(r => new KeyValuePair<string, string>(
                                r.Cells[0].Value as string,
                                r.Cells[1].Value as string
                            ));
                        return PostSettingsLayout.BuildMap(rows, comparer);
                    }
            )
            );
        }

        private void AddHelp(TableLayoutPanel grid, string text, bool spanning = false)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var help = WrapLabel(text, HelpColor);
            help.Margin = new Padding(3, 2, 3, 4);

            if (spanning)
            {
                AddSpanning(grid, help);
                return;
            }

            var row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(help, 1, row);
        }

        private static void AddSpanning(TableLayoutPanel grid, Control control)
        {
            var row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, 2);
        }

        private Label WrapLabel(string text, Color color)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color,
            };
            wrapLabels.Add(label);
            return label;
        }

        private void UpdateWrapWidths(Panel page)
        {
            var width = System.Math.Max(200, page.ClientSize.Width - page.Padding.Horizontal - 24);
            foreach (var label in wrapLabels)
            {
                if (IsOnPage(label, page))
                    label.MaximumSize = new System.Drawing.Size(width, 0);
            }
        }

        private static bool IsOnPage(Control control, Control page)
        {
            for (var parent = control.Parent; parent != null; parent = parent.Parent)
            {
                if (parent == page)
                    return true;
            }
            return false;
        }

        private void SectionList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            var selected = (e.State & DrawItemState.Selected) != 0;
            var back = selected ? SystemColors.Highlight : sectionList.BackColor;
            var fore = selected ? SystemColors.HighlightText : SystemColors.ControlText;

            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);

            var textBounds = new Rectangle(
                e.Bounds.X + 12,
                e.Bounds.Y,
                e.Bounds.Width - 12,
                e.Bounds.Height
            );
            TextRenderer.DrawText(
                e.Graphics,
                sectionList.Items[e.Index].ToString(),
                sectionList.Font,
                textBounds,
                fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis
            );
        }

        private static decimal Clamp(System.Windows.Forms.NumericUpDown box, decimal value) =>
            System.Math.Min(box.Maximum, System.Math.Max(box.Minimum, value));
    }
}
