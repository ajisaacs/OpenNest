using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.IO;

namespace OpenNest.Controls
{
    public class CuttingPanel : Panel
    {
        // Every field label shares this column so inputs line up across sections.
        private const int LabelWidth = 150;
        private const int FieldIndent = 16;
        private const int InputWidth = 100;

        private static readonly Color HintColor = SystemColors.GrayText;

        private readonly TableLayoutPanel layout;
        private readonly ToolTip toolTip = new ToolTip { AutoPopDelay = 15000 };

        private readonly TabControl tabControl;
        private readonly LeadsEditor externalLeads = new LeadsEditor { Emphasis = ContourType.External };
        private readonly LeadsEditor internalLeads = new LeadsEditor { Emphasis = ContourType.Internal };

        private readonly CheckBox chkTabsEnabled;
        private readonly TableLayoutPanel tabOptions;
        private readonly NumericUpDown nudTabWidth;
        private readonly RadioButton rbTabAll;
        private readonly RadioButton rbSupportTab;
        private readonly TableLayoutPanel supportTabOptions;
        private readonly NumericUpDown nudNoBedTabMin;
        private readonly NumericUpDown nudNoBedTabMax;
        private readonly NumericUpDown nudTabHeldMax;
        private readonly NumericUpDown nudPierceClearance;
        private readonly NumericUpDown nudPierceDiameter;

        private CuttingParameters loaded;

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
        {
            AutoScroll = true;
            BackColor = Color.White;

            layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(10, 2, 10, 10),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Leads: one tab per contour type, sized for the type with the most fields.
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
            layout.Controls.Add(tabControl);

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
            layout.Controls.Add(CreateInline(chkRoundLeadInAngles, nudLeadInAngleIncrement, CreateHint("°")));

            chkRoundLeadInAngles.CheckedChanged += (s, e) =>
            {
                nudLeadInAngleIncrement.Enabled = chkRoundLeadInAngles.Checked;
                OnParametersChanged();
            };
            nudLeadInAngleIncrement.ValueChanged += (s, e) => OnParametersChanged();

            // Tabs
            AddSection("Tabs");

            chkTabsEnabled = new CheckBox
            {
                Text = "Add tabs to hold parts in the sheet",
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3),
            };
            layout.Controls.Add(chkTabsEnabled);

            tabOptions = CreateGrid(FieldIndent);
            tabOptions.Enabled = false;

            nudTabWidth = CreateNumeric(0.25, 0.0625);
            AddRow(tabOptions, "Tab size:", nudTabWidth, FieldIndent);

            rbTabAll = new RadioButton
            {
                Text = "Tab every part",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 4, 0, 0),
            };
            AddSpanningRow(tabOptions, rbTabAll);

            // Plan Cutting tabs only the parts the plate's support grid cannot hold level.
            rbSupportTab = new RadioButton
            {
                Text = "Only tab parts the support grid can't hold level",
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 0),
            };
            toolTip.SetToolTip(rbSupportTab,
                "Plan Cutting tabs each part that would tip once cut free, where it best keeps the part level.");
            AddSpanningRow(tabOptions, rbSupportTab);

            // A part up to this long is held by its tab alone, wherever the tab goes; a plate with
            // no support bed tabs the parts whose narrow side is in the range below. A maximum of
            // 0 turns either rule off.
            supportTabOptions = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = new Padding(FieldIndent + 2, 0, 0, 0),
            };
            nudTabHeldMax = CreateNumeric(0, 0.25);
            nudTabHeldMax.Width = 80;
            toolTip.SetToolTip(nudTabHeldMax,
                "Parts this short stay in the sheet on their tab, so a tab anywhere holds them without the grid.");
            supportTabOptions.Controls.Add(
                CreateInline(
                    CreateHint("Tab alone holds parts up to", new Padding(0, 0, 4, 0)),
                    nudTabHeldMax,
                    CreateHint("long (0 = off)", new Padding(6, 0, 0, 0))
                )
            );
            supportTabOptions.Controls.Add(
                CreateHint("On plates with no support bed, tab parts whose narrow side is:", new Padding(0, 4, 0, 2))
            );
            nudNoBedTabMin = CreateNumeric(0, 0.25);
            nudNoBedTabMin.Width = 80;
            nudNoBedTabMax = CreateNumeric(0, 0.25);
            nudNoBedTabMax.Width = 80;
            supportTabOptions.Controls.Add(
                CreateInline(
                    nudNoBedTabMin,
                    CreateHint("to", new Padding(4, 0, 4, 0)),
                    nudNoBedTabMax,
                    CreateHint("(0 = none)", new Padding(6, 0, 0, 0))
                )
            );
            AddSpanningRow(tabOptions, supportTabOptions);
            layout.Controls.Add(tabOptions);

            chkTabsEnabled.CheckedChanged += (s, e) =>
            {
                tabOptions.Enabled = chkTabsEnabled.Checked;
                UpdateSupportTabOptions();
                OnParametersChanged();
            };

            // The two choices toggle together, so one handler sees every change.
            rbTabAll.CheckedChanged += (s, e) =>
            {
                UpdateSupportTabOptions();
                OnParametersChanged();
            };
            nudTabWidth.ValueChanged += (s, e) => OnParametersChanged();
            nudNoBedTabMin.ValueChanged += (s, e) => OnParametersChanged();
            nudNoBedTabMax.ValueChanged += (s, e) => OnParametersChanged();
            nudTabHeldMax.ValueChanged += (s, e) => OnParametersChanged();

            // Pierce
            AddSection("Pierce");

            var pierceGrid = CreateGrid(0);
            nudPierceClearance = CreateNumeric(0.0625, 0.0625);
            AddRow(pierceGrid, "Clearance from contour:", nudPierceClearance, 0);
            toolTip.SetToolTip(nudPierceClearance, "How far each pierce stays from the contour it leads into.");

            // 0 turns the pierce zone off; see docs/geometry/pierce-zone.md.
            nudPierceDiameter = CreateNumeric(0, 0.0625);
            AddRow(pierceGrid, "Pierce zone diameter:", nudPierceDiameter, 0, "0 = off");
            toolTip.SetToolTip(nudPierceDiameter,
                "Diameter of the crater a pierce blows through the plate. Each pierce keeps half of it from all part material.");
            layout.Controls.Add(pierceGrid);

            nudPierceClearance.ValueChanged += (s, e) => OnParametersChanged();
            nudPierceDiameter.ValueChanged += (s, e) => OnParametersChanged();

            Controls.Add(layout);

            // Sized for the type with the most fields, so picking one never moves the sections below;
            // the tab strip, page padding and borders take the rest.
            tabControl.Height = externalLeads.GetTallestHeight() + Font.Height + 28;
            UpdatePreviews();
        }

        public override Size GetPreferredSize(Size proposedSize) => layout.GetPreferredSize(proposedSize);

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
            parameters.TabsEnabled = chkTabsEnabled.Checked && rbTabAll.Checked;
            parameters.TabConfig = new NormalTab { Size = (double)nudTabWidth.Value };
            parameters.PierceClearance = (double)nudPierceClearance.Value;
            parameters.PierceDiameter = (double)nudPierceDiameter.Value;
            parameters.RoundLeadInAngles = chkRoundLeadInAngles.Checked;
            parameters.LeadInAngleIncrement = (double)nudLeadInAngleIncrement.Value;
            parameters.SupportTabs = chkTabsEnabled.Checked && rbSupportTab.Checked;
            parameters.NoBedTabMinWidth = (double)nudNoBedTabMin.Value;
            parameters.NoBedTabMaxWidth = (double)nudNoBedTabMax.Value;
            parameters.TabHeldMaxLength = (double)nudTabHeldMax.Value;
            return parameters;
        }

        public void LoadFromParameters(CuttingParameters p)
        {
            suppressEvents = true;
            loaded = CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(p));

            externalLeads.LeadIn = p.ExternalLeadIn;
            externalLeads.LeadOut = p.ExternalLeadOut;
            internalLeads.LeadIn = p.InternalLeadIn;
            internalLeads.LeadOut = p.InternalLeadOut;

            chkTabsEnabled.Checked = p.TabsEnabled || p.SupportTabs;
            rbSupportTab.Checked = p.SupportTabs;
            rbTabAll.Checked = !p.SupportTabs;
            if (p.TabConfig != null)
                nudTabWidth.Value = Clamp(nudTabWidth, p.TabConfig.Size);
            nudNoBedTabMin.Value = Clamp(nudNoBedTabMin, p.NoBedTabMinWidth);
            nudNoBedTabMax.Value = Clamp(nudNoBedTabMax, p.NoBedTabMaxWidth);
            nudTabHeldMax.Value = Clamp(nudTabHeldMax, p.TabHeldMaxLength);
            tabOptions.Enabled = chkTabsEnabled.Checked;
            UpdateSupportTabOptions();
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

        private void UpdateSupportTabOptions()
        {
            supportTabOptions.Enabled = chkTabsEnabled.Checked && rbSupportTab.Checked;
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
                Margin = new Padding(0, layout.Controls.Count == 0 ? 4 : 12, 0, 4),
                Padding = new Padding(0, 0, 0, 3),
            };
            header.Paint += (s, e) =>
                e.Graphics.DrawLine(SystemPens.ControlLight, 0, header.Height - 1, header.Width, header.Height - 1);
            layout.Controls.Add(header);
        }

        /// <summary>A label / input / hint grid whose input column lines up with every other grid's.</summary>
        private static TableLayoutPanel CreateGrid(int indent)
        {
            var grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Margin = new Padding(indent, 0, 0, 0),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelWidth - indent));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return grid;
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
