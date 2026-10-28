using System;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Controls
{
    public class CuttingPanel : Panel
    {
        private static readonly string[] LeadInTypes =
        {
            "None",
            "Line",
            "Arc",
            "Line + Arc",
            "Clean Hole",
            "Line + Line",
        };

        private static readonly string[] LeadOutTypes = { "None", "Line", "Arc" };

        // Every field label shares this column so inputs line up across sections.
        private const int LabelWidth = 150;
        private const int FieldIndent = 16;
        private const int InputWidth = 100;
        // The lead preview's least height, below the type with the most fields.
        private const int PreviewHeight = 110;

        private static readonly Color HintColor = SystemColors.GrayText;

        private readonly TableLayoutPanel layout;
        private readonly ToolTip toolTip = new ToolTip { AutoPopDelay = 15000 };

        private readonly TabControl tabControl;
        private readonly ComboBox cboExternalLeadIn,
            cboExternalLeadOut;
        private readonly ComboBox cboInternalLeadIn,
            cboInternalLeadOut;

        private readonly TableLayoutPanel pnlExternalLeadIn,
            pnlExternalLeadOut;
        private readonly TableLayoutPanel pnlInternalLeadIn,
            pnlInternalLeadOut;

        private readonly LeadPreview externalPreview = new LeadPreview { Emphasis = ContourType.External };
        private readonly LeadPreview internalPreview = new LeadPreview { Emphasis = ContourType.Internal };

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

            var externalContent = SetupTab(
                tabExternal,
                externalPreview,
                out cboExternalLeadIn,
                out pnlExternalLeadIn,
                out cboExternalLeadOut,
                out pnlExternalLeadOut
            );
            SetupTab(
                tabInternal,
                internalPreview,
                out cboInternalLeadIn,
                out pnlInternalLeadIn,
                out cboInternalLeadOut,
                out pnlInternalLeadOut
            );

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

            PopulateDropdowns();
            FitLeadTabs(externalContent);
            WireChangeEvents();
            UpdatePreviews();
        }

        public override Size GetPreferredSize(Size proposedSize) => layout.GetPreferredSize(proposedSize);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                toolTip.Dispose();
            base.Dispose(disposing);
        }

        public CuttingParameters BuildParameters()
        {
            return new CuttingParameters
            {
                ExternalLeadIn = BuildLeadIn(cboExternalLeadIn, pnlExternalLeadIn),
                ExternalLeadOut = BuildLeadOut(cboExternalLeadOut, pnlExternalLeadOut),
                InternalLeadIn = BuildLeadIn(cboInternalLeadIn, pnlInternalLeadIn),
                InternalLeadOut = BuildLeadOut(cboInternalLeadOut, pnlInternalLeadOut),
                TabsEnabled = chkTabsEnabled.Checked && rbTabAll.Checked,
                TabConfig = new NormalTab { Size = (double)nudTabWidth.Value },
                PierceClearance = (double)nudPierceClearance.Value,
                PierceDiameter = (double)nudPierceDiameter.Value,
                RoundLeadInAngles = chkRoundLeadInAngles.Checked,
                LeadInAngleIncrement = (double)nudLeadInAngleIncrement.Value,
                SupportTabs = chkTabsEnabled.Checked && rbSupportTab.Checked,
                NoBedTabMinWidth = (double)nudNoBedTabMin.Value,
                NoBedTabMaxWidth = (double)nudNoBedTabMax.Value,
                TabHeldMaxLength = (double)nudTabHeldMax.Value,
            };
        }

        public void LoadFromParameters(CuttingParameters p)
        {
            suppressEvents = true;

            LoadLeadIn(cboExternalLeadIn, pnlExternalLeadIn, p.ExternalLeadIn);
            LoadLeadOut(cboExternalLeadOut, pnlExternalLeadOut, p.ExternalLeadOut);
            LoadLeadIn(cboInternalLeadIn, pnlInternalLeadIn, p.InternalLeadIn);
            LoadLeadOut(cboInternalLeadOut, pnlInternalLeadOut, p.InternalLeadOut);

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
            externalPreview.Parameters = parameters;
            internalPreview.Parameters = parameters;
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

        /// <summary>
        /// Sizes the lead tabs for the lead-in and lead-out with the most fields, so picking a
        /// type never clips its fields or moves the sections below.
        /// </summary>
        private void FitLeadTabs(Control content)
        {
            var tallestLeadIn = 0;
            for (var i = 0; i < LeadInTypes.Length; i++)
            {
                BuildLeadInParamControls(pnlExternalLeadIn, i);
                tallestLeadIn = System.Math.Max(tallestLeadIn, pnlExternalLeadIn.GetPreferredSize(Size.Empty).Height);
            }

            var tallestLeadOut = 0;
            for (var i = 0; i < LeadOutTypes.Length; i++)
            {
                BuildLeadOutParamControls(pnlExternalLeadOut, i);
                tallestLeadOut = System.Math.Max(tallestLeadOut, pnlExternalLeadOut.GetPreferredSize(Size.Empty).Height);
            }

            BuildLeadInParamControls(pnlExternalLeadIn, 0);
            BuildLeadOutParamControls(pnlExternalLeadOut, 0);

            var contentHeight = content.GetPreferredSize(Size.Empty).Height + tallestLeadIn + tallestLeadOut
                + PreviewHeight;
            // Tab strip, page padding and borders.
            tabControl.Height = contentHeight + Font.Height + 28;
        }

        private TableLayoutPanel SetupTab(
            TabPage tab,
            LeadPreview preview,
            out ComboBox leadInCombo,
            out TableLayoutPanel leadInPanel,
            out ComboBox leadOutCombo,
            out TableLayoutPanel leadOutPanel
        )
        {
            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
            };

            leadInCombo = CreateCombo();
            var leadInType = CreateGrid(0);
            AddRow(leadInType, "Lead-in:", leadInCombo, 0, bold: true);
            content.Controls.Add(leadInType);

            leadInPanel = CreateGrid(FieldIndent);
            content.Controls.Add(leadInPanel);

            leadOutCombo = CreateCombo();
            var leadOutType = CreateGrid(0);
            leadOutType.Margin = new Padding(0, 8, 0, 0);
            AddRow(leadOutType, "Lead-out:", leadOutCombo, 0, bold: true);
            content.Controls.Add(leadOutType);

            leadOutPanel = CreateGrid(FieldIndent);
            content.Controls.Add(leadOutPanel);

            // The preview fills whatever the fields leave, so it grows for types with fewer.
            preview.Dock = DockStyle.Fill;
            preview.Margin = new Padding(0, 6, 0, 0);
            tab.Controls.Add(preview);
            tab.Controls.Add(content);
            preview.BringToFront();
            return content;
        }

        private void PopulateDropdowns()
        {
            foreach (
                var combo in new[] { cboExternalLeadIn, cboInternalLeadIn }
            )
            {
                combo.Items.AddRange(LeadInTypes);
                combo.SelectedIndex = 0;
            }

            foreach (
                var combo in new[] { cboExternalLeadOut, cboInternalLeadOut }
            )
            {
                combo.Items.AddRange(LeadOutTypes);
                combo.SelectedIndex = 0;
            }
        }

        private void WireChangeEvents()
        {
            cboExternalLeadIn.SelectedIndexChanged += OnLeadInTypeChanged;
            cboInternalLeadIn.SelectedIndexChanged += OnLeadInTypeChanged;

            cboExternalLeadOut.SelectedIndexChanged += OnLeadOutTypeChanged;
            cboInternalLeadOut.SelectedIndexChanged += OnLeadOutTypeChanged;
        }

        private void OnLeadInTypeChanged(object sender, EventArgs e)
        {
            var combo = (ComboBox)sender;
            var panel = GetLeadInPanel(combo);
            if (panel != null)
                BuildLeadInParamControls(panel, combo.SelectedIndex);
            OnParametersChanged();
        }

        private void OnLeadOutTypeChanged(object sender, EventArgs e)
        {
            var combo = (ComboBox)sender;
            var panel = GetLeadOutPanel(combo);
            if (panel != null)
                BuildLeadOutParamControls(panel, combo.SelectedIndex);
            OnParametersChanged();
        }

        private TableLayoutPanel GetLeadInPanel(ComboBox combo)
        {
            if (combo == cboExternalLeadIn)
                return pnlExternalLeadIn;
            if (combo == cboInternalLeadIn)
                return pnlInternalLeadIn;
            return null;
        }

        private TableLayoutPanel GetLeadOutPanel(ComboBox combo)
        {
            if (combo == cboExternalLeadOut)
                return pnlExternalLeadOut;
            if (combo == cboInternalLeadOut)
                return pnlInternalLeadOut;
            return null;
        }

        private void BuildLeadInParamControls(TableLayoutPanel panel, int typeIndex)
        {
            ClearRows(panel);

            switch (typeIndex)
            {
                case 1:
                    AddNumericField(panel, "Length:", 0.25, "Length");
                    AddAngleField(panel, "Approach angle:", 90, "ApproachAngle");
                    break;
                case 2:
                    AddNumericField(panel, "Radius:", 0.25, "Radius");
                    // Degrees of arc: 90 is a quarter circle, 180 a half circle.
                    AddAngleField(panel, "Sweep angle:", ArcLeadIn.DefaultSweepAngle, "SweepAngle",
                        increment: 15, minimum: 1, maximum: 180);
                    break;
                case 3:
                    AddNumericField(panel, "Line length:", 0.25, "LineLength");
                    AddNumericField(panel, "Arc radius:", 0.125, "ArcRadius");
                    AddAngleField(panel, "Approach angle:", 135, "ApproachAngle");
                    break;
                case 4:
                    AddNumericField(panel, "Line length:", 0.25, "LineLength");
                    AddNumericField(panel, "Arc radius:", 0.125, "ArcRadius");
                    AddNumericField(panel, "Kerf:", 0.06, "Kerf");
                    break;
                case 5:
                    AddNumericField(panel, "First length:", 0.25, "Length1");
                    AddAngleField(panel, "First angle:", 90, "Angle1");
                    AddNumericField(panel, "Second length:", 0.25, "Length2");
                    AddAngleField(panel, "Second angle:", 90, "Angle2");
                    break;
            }
        }

        private void BuildLeadOutParamControls(TableLayoutPanel panel, int typeIndex)
        {
            ClearRows(panel);

            switch (typeIndex)
            {
                case 1:
                    AddNumericField(panel, "Length:", 0.25, "Length");
                    AddAngleField(panel, "Approach angle:", 90, "ApproachAngle");
                    break;
                case 2:
                    AddNumericField(panel, "Radius:", 0.25, "Radius");
                    break;
            }
        }

        private void AddAngleField(
            TableLayoutPanel panel,
            string label,
            double defaultValue,
            string tag,
            double increment = 5,
            double minimum = 0,
            double maximum = 360
        ) => AddNumericField(panel, label, defaultValue, tag, increment, minimum, maximum, "°", 2);

        private void AddNumericField(
            TableLayoutPanel panel,
            string label,
            double defaultValue,
            string tag,
            double increment = 0.0625,
            double minimum = 0,
            double maximum = 9999,
            string suffix = null,
            int decimalPlaces = 4
        )
        {
            var nud = CreateNumeric(defaultValue, increment);
            nud.Minimum = (decimal)minimum;
            nud.Maximum = (decimal)maximum;
            nud.DecimalPlaces = decimalPlaces;
            nud.Value = System.Math.Clamp((decimal)defaultValue, nud.Minimum, nud.Maximum);
            nud.Tag = tag;
            nud.ValueChanged += (s, e) => OnParametersChanged();
            AddRow(panel, label, nud, FieldIndent, suffix);
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
            string hint = null,
            bool bold = false
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
            if (bold)
                text.Font = new Font(text.Font, FontStyle.Bold);
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

        private static void ClearRows(TableLayoutPanel grid)
        {
            grid.SuspendLayout();
            while (grid.Controls.Count > 0)
                grid.Controls[0].Dispose();
            grid.RowStyles.Clear();
            grid.RowCount = 0;
            grid.ResumeLayout();
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

        private static ComboBox CreateCombo() =>
            new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 150,
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

        private static void LoadLeadIn(ComboBox combo, Panel panel, LeadIn leadIn)
        {
            switch (leadIn)
            {
                case LineLeadIn line:
                    combo.SelectedIndex = 1;
                    SetParam(panel, "Length", line.Length);
                    SetParam(panel, "ApproachAngle", line.ApproachAngle);
                    break;
                case ArcLeadIn arc:
                    combo.SelectedIndex = 2;
                    SetParam(panel, "Radius", arc.Radius);
                    SetParam(panel, "SweepAngle", arc.SweepAngle);
                    break;
                case LineArcLeadIn lineArc:
                    combo.SelectedIndex = 3;
                    SetParam(panel, "LineLength", lineArc.LineLength);
                    SetParam(panel, "ArcRadius", lineArc.ArcRadius);
                    SetParam(panel, "ApproachAngle", lineArc.ApproachAngle);
                    break;
                case CleanHoleLeadIn cleanHole:
                    combo.SelectedIndex = 4;
                    SetParam(panel, "LineLength", cleanHole.LineLength);
                    SetParam(panel, "ArcRadius", cleanHole.ArcRadius);
                    SetParam(panel, "Kerf", cleanHole.Kerf);
                    break;
                case LineLineLeadIn lineLine:
                    combo.SelectedIndex = 5;
                    SetParam(panel, "Length1", lineLine.Length1);
                    SetParam(panel, "Angle1", lineLine.ApproachAngle1);
                    SetParam(panel, "Length2", lineLine.Length2);
                    SetParam(panel, "Angle2", lineLine.ApproachAngle2);
                    break;
                default:
                    combo.SelectedIndex = 0;
                    break;
            }
        }

        private static void LoadLeadOut(ComboBox combo, Panel panel, LeadOut leadOut)
        {
            switch (leadOut)
            {
                case LineLeadOut line:
                    combo.SelectedIndex = 1;
                    SetParam(panel, "Length", line.Length);
                    SetParam(panel, "ApproachAngle", line.ApproachAngle);
                    break;
                case ArcLeadOut arc:
                    combo.SelectedIndex = 2;
                    SetParam(panel, "Radius", arc.Radius);
                    break;
                default:
                    combo.SelectedIndex = 0;
                    break;
            }
        }

        private static LeadIn BuildLeadIn(ComboBox combo, Panel panel)
        {
            return combo.SelectedIndex switch
            {
                1 => new LineLeadIn
                {
                    Length = GetParam(panel, "Length", 0.25),
                    ApproachAngle = GetParam(panel, "ApproachAngle", 90),
                },
                2 => new ArcLeadIn
                {
                    Radius = GetParam(panel, "Radius", 0.25),
                    SweepAngle = GetParam(panel, "SweepAngle", ArcLeadIn.DefaultSweepAngle),
                },
                3 => new LineArcLeadIn
                {
                    LineLength = GetParam(panel, "LineLength", 0.25),
                    ArcRadius = GetParam(panel, "ArcRadius", 0.125),
                    ApproachAngle = GetParam(panel, "ApproachAngle", 135),
                },
                4 => new CleanHoleLeadIn
                {
                    LineLength = GetParam(panel, "LineLength", 0.25),
                    ArcRadius = GetParam(panel, "ArcRadius", 0.125),
                    Kerf = GetParam(panel, "Kerf", 0.06),
                },
                5 => new LineLineLeadIn
                {
                    Length1 = GetParam(panel, "Length1", 0.25),
                    ApproachAngle1 = GetParam(panel, "Angle1", 90),
                    Length2 = GetParam(panel, "Length2", 0.25),
                    ApproachAngle2 = GetParam(panel, "Angle2", 90),
                },
                _ => new NoLeadIn(),
            };
        }

        private static LeadOut BuildLeadOut(ComboBox combo, Panel panel)
        {
            return combo.SelectedIndex switch
            {
                1 => new LineLeadOut
                {
                    Length = GetParam(panel, "Length", 0.25),
                    ApproachAngle = GetParam(panel, "ApproachAngle", 90),
                },
                2 => new ArcLeadOut { Radius = GetParam(panel, "Radius", 0.25) },
                _ => new NoLeadOut(),
            };
        }

        private static void SetParam(Panel panel, string tag, double value)
        {
            foreach (Control c in panel.Controls)
            {
                if (c is NumericUpDown nud && (string)nud.Tag == tag)
                {
                    nud.Value = double.IsFinite(value)
                        ? System.Math.Clamp((decimal)value, nud.Minimum, nud.Maximum)
                        : nud.Minimum;
                    return;
                }
            }
        }

        private static double GetParam(Panel panel, string tag, double defaultValue)
        {
            foreach (Control c in panel.Controls)
            {
                if (c is NumericUpDown nud && (string)nud.Tag == tag)
                    return (double)nud.Value;
            }
            return defaultValue;
        }
    }
}
