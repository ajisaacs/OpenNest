using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.IO;
using OpenNest.Support;

namespace OpenNest.Controls
{
    public class CuttingPanel : Panel
    {
        // Every field label shares this column so inputs line up across sections.
        private const int FieldIndent = 16;
        private const int InputWidth = 100;
        // The leads column's width when the panel lays out side by side.
        private const int LeadsColumnWidth = 360;

        private static readonly Color HintColor = SystemColors.GrayText;

        // The whole panel: the source line above one column, or above the leads and option
        // columns side by side.
        private readonly TableLayoutPanel layout;
        // The column sections are being added to while the panel is built.
        private TableLayoutPanel column;
        private readonly ToolTip toolTip = new ToolTip { AutoPopDelay = 15000 };

        private readonly TabControl tabControl;
        private readonly LeadsEditor externalLeads = new LeadsEditor { Emphasis = ContourType.External };
        private readonly LeadsEditor internalLeads = new LeadsEditor { Emphasis = ContourType.Internal };

        private readonly CheckBox chkTabsEnabled;
        private readonly TableLayoutPanel tabOptions;
        private readonly NumericUpDown nudTabWidth;
        private readonly RadioButton rbSupportTab;
        private readonly RadioButton rbTabBySize;
        private readonly NumericUpDown nudTabSizeMin;
        private readonly NumericUpDown nudTabSizeMax;
        private readonly CheckBox chkSliceSlugs;
        private readonly ComboBox cboLabelMode;
        private readonly NumericUpDown nudPierceClearance;
        private readonly NumericUpDown nudPierceDiameter;

        private CuttingParameters loaded;

        private readonly Label sourceLabel;
        private bool machineLeads;

        private readonly CheckBox chkRoundLeadInAngles;
        private readonly NumericUpDown nudLeadInAngleIncrement;

        private bool suppressEvents;

        public event EventHandler ParametersChanged;

        public ContourType? ActiveContourType
        {
            get
            {
                return tabControl.SelectedIndex switch
                {
                    0 => ContourType.External,
                    1 => ContourType.Internal,
                    _ => null,
                };
            }
            set
            {
                if (value == null)
                    return;

                var index = value.Value switch
                {
                    ContourType.External => 0,
                    // Circles share the internal leads.
                    ContourType.Internal or ContourType.ArcCircle => 1,
                    _ => -1,
                };

                if (index >= 0 && tabControl.SelectedIndex != index)
                    tabControl.SelectedIndex = index;
            }
        }

        public CuttingPanel()
            : this(showLeads: true) { }

        /// <param name="showLeads">
        /// False leaves out the lead-in and lead-out editors (Machine Configuration edits a
        /// thickness's leads on their own tabs); lead-in angle rounding stays.
        /// </param>
        /// <param name="sideBySide">
        /// True puts the leads in a column of their own beside the other settings, for a dialog;
        /// false stacks everything in one column, for a narrow side panel.
        /// </param>
        public CuttingPanel(bool showLeads, bool sideBySide = false)
        {
            AutoScroll = true;
            BackColor = Color.White;

            layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = sideBySide ? 2 : 1,
                Padding = new Padding(10, 2, 10, 10),
            };
            var leadsColumn = layout;
            var optionsColumn = layout;
            if (sideBySide)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LeadsColumnWidth));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                leadsColumn = CreateColumn(new Padding(0));
                optionsColumn = CreateColumn(new Padding(20, 0, 0, 0));
            }
            else
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            }
            column = layout;

            // Where these settings are saved: the nest's machine thickness, or this PC.
            sourceLabel = new Label
            {
                AutoSize = true,
                ForeColor = HintColor,
                Margin = new Padding(0, 6, 0, 0),
                Visible = false,
            };
            layout.Controls.Add(sourceLabel);
            if (sideBySide)
            {
                layout.SetColumnSpan(sourceLabel, 2);
                layout.Controls.Add(leadsColumn, 0, 1);
                layout.Controls.Add(optionsColumn, 1, 1);
            }

            // Leads: one tab per contour type, sized for the type with the most fields.
            column = leadsColumn;
            if (showLeads)
                AddSection("Leads");

            tabControl = new TabControl
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Margin = new Padding(0, 2, 0, 6),
            };

            var tabExternal = new TabPage("Outside") { Padding = new Padding(6), UseVisualStyleBackColor = true };
            var tabInternal = new TabPage("Holes") { Padding = new Padding(6), UseVisualStyleBackColor = true };
            toolTip.SetToolTip(tabControl, "Outside: each part's perimeter. Holes: inner contours and circles.");

            foreach (var (tab, leads) in new[] { (tabExternal, externalLeads), (tabInternal, internalLeads) })
            {
                leads.Dock = DockStyle.Fill;
                leads.LeadInChanged += (s, e) => OnParametersChanged();
                leads.LeadOutChanged += (s, e) => OnParametersChanged();
                tab.Controls.Add(leads);
            }

            tabControl.Controls.Add(tabExternal);
            tabControl.Controls.Add(tabInternal);
            if (showLeads)
                column.Controls.Add(tabControl);

            chkRoundLeadInAngles = new CheckBox
            {
                Text = "Round lead-in angles to every",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 3, 0, 3),
            };
            nudLeadInAngleIncrement = CreateNumeric(5, 1);
            nudLeadInAngleIncrement.DecimalPlaces = 0;
            nudLeadInAngleIncrement.Minimum = 1;
            nudLeadInAngleIncrement.Maximum = 90;
            nudLeadInAngleIncrement.Width = 56;
            nudLeadInAngleIncrement.Enabled = false;
            toolTip.SetToolTip(chkRoundLeadInAngles, "Snaps each lead-in's angle to a multiple of this many degrees.");
            column.Controls.Add(CreateInline(chkRoundLeadInAngles, nudLeadInAngleIncrement, CreateHint("°")));

            chkRoundLeadInAngles.CheckedChanged += (s, e) =>
            {
                nudLeadInAngleIncrement.Enabled = chkRoundLeadInAngles.Checked;
                OnParametersChanged();
            };
            nudLeadInAngleIncrement.ValueChanged += (s, e) => OnParametersChanged();

            // Tabs
            column = optionsColumn;
            AddSection("Tabs");

            chkTabsEnabled = new CheckBox
            {
                Text = "Add tabs to hold parts in the sheet",
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3),
            };
            toolTip.SetToolTip(chkTabsEnabled,
                "Plan Cutting tabs the parts chosen below; a tabbed part stays in the sheet on its tab.");
            column.Controls.Add(chkTabsEnabled);

            tabOptions = ControlFactory.SettingsGrid(FieldIndent);
            tabOptions.Enabled = false;

            nudTabWidth = CreateNumeric(0.25, 0.0625);
            AddRow(tabOptions, "Tab size:", nudTabWidth, FieldIndent);

            // Plan Cutting tabs only the parts the plate's support grid cannot hold level.
            rbSupportTab = new RadioButton
            {
                Text = "Tab parts the support grid can't hold level",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 4, 0, 0),
            };
            toolTip.SetToolTip(rbSupportTab,
                "Plan Cutting tabs each part that would tip once cut free, and its tab holds it wherever the tab goes. "
                + "A plate with no support bed tabs by the size range instead.");
            AddSpanningRow(tabOptions, rbSupportTab);

            // Tabs the parts whose narrow side is in the range below, whatever the support grid.
            rbTabBySize = new RadioButton
            {
                Text = "Tab parts by size",
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
            };
            toolTip.SetToolTip(rbTabBySize,
                "Ignores the support grid: tabs every part whose narrow side is in the range below.");
            AddSpanningRow(tabOptions, rbTabBySize);

            // The size range: the narrow side of each part's smallest bounding box, at any angle.
            // Tabs by size use it, and so do support grid tabs on a plate with no support bed. A
            // maximum of 0 tabs nothing.
            nudTabSizeMin = CreateNumeric(0, 0.25);
            nudTabSizeMin.Width = 80;
            nudTabSizeMax = CreateNumeric(0, 0.25);
            nudTabSizeMax.Width = 80;
            nudTabSizeMax.Maximum = (decimal)SupportTabSettings.TabEveryPartMaxWidth;
            const string sizeRangeTip =
                "By size, or on a plate with no support bed, parts whose narrow side is in this range are tabbed. "
                + "The narrow side of the part's smallest bounding box, at any angle, so its rotation does not matter. "
                + "A maximum of 0 tabs none.";
            toolTip.SetToolTip(nudTabSizeMin, sizeRangeTip);
            toolTip.SetToolTip(nudTabSizeMax, sizeRangeTip);
            var sizeRange = CreateInline(
                nudTabSizeMin,
                CreateHint("to", new Padding(4, 0, 4, 0)),
                nudTabSizeMax
            );
            AddRow(tabOptions, "Narrow side from:", sizeRange, FieldIndent);
            column.Controls.Add(tabOptions);

            chkTabsEnabled.CheckedChanged += (s, e) =>
            {
                tabOptions.Enabled = chkTabsEnabled.Checked;
                OnParametersChanged();
            };

            // The two choices toggle together, so one handler sees every change.
            rbSupportTab.CheckedChanged += (s, e) => OnParametersChanged();
            nudTabWidth.ValueChanged += (s, e) => OnParametersChanged();
            nudTabSizeMin.ValueChanged += (s, e) => OnParametersChanged();
            nudTabSizeMax.ValueChanged += (s, e) => OnParametersChanged();

            // Cutouts: slugs that would tip are cut in pieces that drop between the supports.
            AddSection("Cutouts");

            chkSliceSlugs = new CheckBox
            {
                Text = "Slice slugs the support grid can't hold level",
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3),
            };
            toolTip.SetToolTip(chkSliceSlugs,
                "Plan Cutting first cuts each cutout slug that would tip along the support grid's lines, "
                + "stopping a kerf short of the cutout, so its pieces drop between the supports.");
            column.Controls.Add(chkSliceSlugs);
            chkSliceSlugs.CheckedChanged += (s, e) => OnParametersChanged();

            // Marks: the part number etched in each drawing's label box, and bend group letters.
            AddSection("Marks");

            var marksGrid = ControlFactory.SettingsGrid(0);
            cboLabelMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 200,
            };
            // Index order is the PartLabelMode value order.
            cboLabelMode.Items.AddRange(new object[] { "Off", "Part number", "Part number and bend groups" });
            cboLabelMode.SelectedIndex = (int)PartLabelMode.Off;
            AddRow(marksGrid, "Etch part label:", cboLabelMode, 0);
            toolTip.SetToolTip(cboLabelMode,
                "Plan Cutting etches each part's number in the label box set in the CAD Converter, "
                + "alone or with each bend's group letter beside its bend line "
                + "(A across the grain, B with it, then C and on). "
                + "Parts with no label box get no part number.");
            column.Controls.Add(marksGrid);
            cboLabelMode.SelectedIndexChanged += (s, e) => OnParametersChanged();

            // Pierce
            AddSection("Pierce");

            var pierceGrid = ControlFactory.SettingsGrid(0);
            nudPierceClearance = CreateNumeric(0.0625, 0.0625);
            AddRow(pierceGrid, "Clearance from contour:", nudPierceClearance, 0);
            toolTip.SetToolTip(nudPierceClearance, "How far each pierce stays from the contour it leads into.");

            // 0 turns the pierce zone off; see docs/geometry/pierce-zone.md.
            nudPierceDiameter = CreateNumeric(0, 0.0625);
            AddRow(pierceGrid, "Pierce zone diameter:", nudPierceDiameter, 0, "0 = off");
            toolTip.SetToolTip(nudPierceDiameter,
                "Diameter of the crater a pierce blows through the plate. Each pierce keeps half of it from all part material.");
            column.Controls.Add(pierceGrid);

            nudPierceClearance.ValueChanged += (s, e) => OnParametersChanged();
            nudPierceDiameter.ValueChanged += (s, e) => OnParametersChanged();

            Controls.Add(layout);

            // Sized for the type with the most fields, so picking one never moves the sections below;
            // the tab strip, page padding and borders take the rest.
            tabControl.Height = externalLeads.GetTallestHeight() + Font.Height + 28;
            UpdatePreviews();
        }

        public override Size GetPreferredSize(Size proposedSize) => layout.GetPreferredSize(proposedSize);

        /// <summary>Where these settings are saved, shown above them; null or empty hides the line.</summary>
        public string Source
        {
            get => sourceLabel.Text;
            set
            {
                sourceLabel.Text = value ?? "";
                sourceLabel.Visible = !string.IsNullOrEmpty(value);
            }
        }

        /// <summary>
        /// Offers only the leads a machine thickness can hold: no Line + Line, no arc sweep (leads
        /// are half circles), a clean hole's kerf from the thickness and no clean hole outside.
        /// </summary>
        public bool MachineLeads
        {
            get => machineLeads;
            set
            {
                machineLeads = value;
                foreach (var leads in new[] { externalLeads, internalLeads })
                {
                    leads.AllowLineLine = !value;
                    leads.ShowArcSweep = !value;
                    leads.ShowCleanHoleKerf = !value;
                }
                externalLeads.AllowCleanHole = !value;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                toolTip.Dispose();
            base.Dispose(disposing);
        }

        // Settings the panel has no field for (kerf, the machine/material/thickness stamp,
        // sequencing) come back unchanged from the parameters last loaded.
        public CuttingParameters BuildParameters()
        {
            var parameters = loaded == null ? new CuttingParameters()
                : CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(loaded));
            parameters.ExternalLeadIn = externalLeads.LeadIn;
            parameters.ExternalLeadOut = externalLeads.LeadOut;
            parameters.InternalLeadIn = internalLeads.LeadIn;
            parameters.InternalLeadOut = internalLeads.LeadOut;
            // Tabs on every part are retired: Plan Cutting and Place Lead-in enable a tab on the
            // copies they give the parts that need one.
            parameters.TabsEnabled = false;
            parameters.TabConfig = new NormalTab { Size = (double)nudTabWidth.Value };
            parameters.PierceClearance = (double)nudPierceClearance.Value;
            parameters.PierceDiameter = (double)nudPierceDiameter.Value;
            parameters.RoundLeadInAngles = chkRoundLeadInAngles.Checked;
            parameters.LeadInAngleIncrement = (double)nudLeadInAngleIncrement.Value;
            parameters.SupportTabs = chkTabsEnabled.Checked;
            parameters.TabBySize = rbTabBySize.Checked;
            parameters.TabSizeMinWidth = (double)nudTabSizeMin.Value;
            parameters.TabSizeMaxWidth = (double)nudTabSizeMax.Value;
            parameters.SliceTippingSlugs = chkSliceSlugs.Checked;
            parameters.LabelMode = (PartLabelMode)System.Math.Max(0, cboLabelMode.SelectedIndex);
            return parameters;
        }

        public void LoadFromParameters(CuttingParameters p)
        {
            suppressEvents = true;
            loaded = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(p));
            // Settings saved with "Tab every part" show as tabs by size over every width.
            SupportTabSettings.MigrateTabEveryPart(loaded);
            p = loaded;

            externalLeads.LeadIn = p.ExternalLeadIn;
            externalLeads.LeadOut = p.ExternalLeadOut;
            internalLeads.LeadIn = p.InternalLeadIn;
            internalLeads.LeadOut = p.InternalLeadOut;

            chkTabsEnabled.Checked = p.SupportTabs;
            rbTabBySize.Checked = p.TabBySize;
            rbSupportTab.Checked = !p.TabBySize;
            if (p.TabConfig != null)
                nudTabWidth.Value = Clamp(nudTabWidth, p.TabConfig.Size);
            nudTabSizeMin.Value = Clamp(nudTabSizeMin, p.TabSizeMinWidth);
            nudTabSizeMax.Value = Clamp(nudTabSizeMax, p.TabSizeMaxWidth);
            tabOptions.Enabled = chkTabsEnabled.Checked;
            chkSliceSlugs.Checked = p.SliceTippingSlugs;
            // A mode the list does not offer (set in code; files load it as Off) shows as Off.
            cboLabelMode.SelectedIndex = System.Enum.IsDefined(p.LabelMode) ? (int)p.LabelMode : (int)PartLabelMode.Off;
            nudPierceClearance.Value = Clamp(nudPierceClearance, p.PierceClearance);
            nudPierceDiameter.Value = Clamp(nudPierceDiameter, p.PierceDiameter);
            chkRoundLeadInAngles.Checked = p.RoundLeadInAngles;
            nudLeadInAngleIncrement.Value = Clamp(nudLeadInAngleIncrement, p.LeadInAngleIncrement);
            nudLeadInAngleIncrement.Enabled = p.RoundLeadInAngles;

            suppressEvents = false;
            UpdatePreviews();
        }

        private void UpdatePreviews()
        {
            var parameters = BuildParameters();
            externalLeads.PreviewParameters = parameters;
            internalLeads.PreviewParameters = parameters;
        }

        private static decimal Clamp(NumericUpDown control, double value) =>
            double.IsFinite(value)
                ? System.Math.Clamp((decimal)value, control.Minimum, control.Maximum)
                : control.Minimum;

        private void OnParametersChanged()
        {
            if (suppressEvents)
                return;
            UpdatePreviews();
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AddSection(string title)
        {
            var header = new Label
            {
                Text = title,
                AutoSize = false,
                Height = Font.Height + 10,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.BottomLeft,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, column.Controls.Count == (column == layout ? 1 : 0) ? 4 : 12, 0, 4),
                Padding = new Padding(0, 0, 0, 3),
            };
            header.Paint += (s, e) =>
                e.Graphics.DrawLine(SystemPens.ControlLight, 0, header.Height - 1, header.Width, header.Height - 1);
            column.Controls.Add(header);
        }

        // One column of sections, in a cell of the side-by-side layout.
        private static TableLayoutPanel CreateColumn(Padding margin)
        {
            var result = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = margin,
            };
            result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return result;
        }

        private static void AddRow(
            TableLayoutPanel grid,
            string label,
            Control input,
            int indent,
            string hint = null
        )
        {
            var row = NextRow(grid);
            var text = new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 4, 0),
            };
            grid.Controls.Add(text, 0, row);

            input.Anchor = AnchorStyles.Left;
            input.Margin = new Padding(0, 2, 0, 2);
            grid.Controls.Add(input, 1, row);

            if (hint != null)
                grid.Controls.Add(CreateHint(hint, new Padding(4, 0, 0, 0)), 2, row);
        }

        private static void AddSpanningRow(TableLayoutPanel grid, Control control)
        {
            var row = NextRow(grid);
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, grid.ColumnCount);
        }

        private static int NextRow(TableLayoutPanel grid)
        {
            var row = grid.RowCount;
            grid.RowCount = row + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return row;
        }

        private static FlowLayoutPanel CreateInline(params Control[] controls)
        {
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0),
            };
            foreach (var control in controls)
            {
                control.Anchor = AnchorStyles.Left;
                flow.Controls.Add(control);
            }
            return flow;
        }

        private static Label CreateHint(string text, Padding? margin = null) =>
            new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = HintColor,
                Anchor = AnchorStyles.Left,
                Margin = margin ?? new Padding(2, 0, 0, 0),
            };

        private static NumericUpDown CreateNumeric(double defaultValue, double increment)
        {
            return new NumericUpDown
            {
                Width = InputWidth,
                DecimalPlaces = 4,
                Increment = (decimal)increment,
                Minimum = 0,
                Maximum = 9999,
                Value = (decimal)defaultValue,
                TextAlign = HorizontalAlignment.Right,
            };
        }
    }
}
