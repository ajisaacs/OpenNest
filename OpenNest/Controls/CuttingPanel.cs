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

        private readonly TabControl tabControl;
        private readonly ComboBox cboExternalLeadIn,
            cboExternalLeadOut;
        private readonly ComboBox cboInternalLeadIn,
            cboInternalLeadOut;

        private readonly Panel pnlExternalLeadIn,
            pnlExternalLeadOut;
        private readonly Panel pnlInternalLeadIn,
            pnlInternalLeadOut;

        private readonly CheckBox chkTabsEnabled;
        private readonly NumericUpDown nudTabWidth;
        private readonly RadioButton rbTabAll;
        private readonly RadioButton rbSupportTab;
        private readonly NumericUpDown nudNoBedTabMin;
        private readonly NumericUpDown nudNoBedTabMax;
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

            // Tab control for contour types — wrapped in a fixed-height panel for Dock.Top
            tabControl = new TabControl { Dock = DockStyle.Fill };

            var tabExternal = new TabPage("External") { Padding = new Padding(4) };
            var tabInternal = new TabPage("Internal") { Padding = new Padding(4) };

            SetupTab(
                tabExternal,
                out cboExternalLeadIn,
                out pnlExternalLeadIn,
                out cboExternalLeadOut,
                out pnlExternalLeadOut
            );
            SetupTab(
                tabInternal,
                out cboInternalLeadIn,
                out pnlInternalLeadIn,
                out cboInternalLeadOut,
                out pnlInternalLeadOut
            );

            tabControl.Controls.Add(tabExternal);
            tabControl.Controls.Add(tabInternal);

            var tabWrapper = new Panel { Dock = DockStyle.Top, Height = 340 };
            tabWrapper.Controls.Add(tabControl);

            // Tabs section
            var tabsPanel = new CollapsiblePanel
            {
                HeaderText = "Tabs",
                Dock = DockStyle.Top,
                ExpandedHeight = 132,
                IsExpanded = false,
            };

            chkTabsEnabled = new CheckBox
            {
                Text = "Enable Tabs",
                Location = new Point(12, 4),
                AutoSize = true,
            };
            tabsPanel.ContentPanel.Controls.Add(chkTabsEnabled);

            tabsPanel.ContentPanel.Controls.Add(
                new Label
                {
                    Text = "Tab Size:",
                    Location = new Point(160, 6),
                    AutoSize = true,
                }
            );

            nudTabWidth = CreateNumeric(225, 3, 0.25, 0.0625);
            nudTabWidth.Enabled = false;
            tabsPanel.ContentPanel.Controls.Add(nudTabWidth);

            rbTabAll = new RadioButton
            {
                Text = "Tab all parts",
                Location = new Point(28, 28),
                AutoSize = true,
                Enabled = false,
                Checked = true,
            };
            tabsPanel.ContentPanel.Controls.Add(rbTabAll);

            // Plan Cutting tabs only the parts the plate's support grid cannot hold level.
            rbSupportTab = new RadioButton
            {
                Text = "Tab parts the support grid cannot hold level",
                Location = new Point(28, 50),
                AutoSize = true,
                Enabled = false,
            };
            tabsPanel.ContentPanel.Controls.Add(rbSupportTab);

            // A plate with no support bed tabs the parts whose narrow side is in this range;
            // a maximum of 0 tabs none.
            tabsPanel.ContentPanel.Controls.Add(
                new Label
                {
                    Text = "No bed: tab widths",
                    Location = new Point(44, 76),
                    AutoSize = true,
                }
            );
            nudNoBedTabMin = CreateNumeric(160, 74, 0, 0.25);
            nudNoBedTabMin.Size = new Size(70, 22);
            nudNoBedTabMin.Enabled = false;
            tabsPanel.ContentPanel.Controls.Add(nudNoBedTabMin);
            tabsPanel.ContentPanel.Controls.Add(
                new Label
                {
                    Text = "to",
                    Location = new Point(234, 76),
                    AutoSize = true,
                }
            );
            nudNoBedTabMax = CreateNumeric(254, 74, 0, 0.25);
            nudNoBedTabMax.Size = new Size(70, 22);
            nudNoBedTabMax.Enabled = false;
            tabsPanel.ContentPanel.Controls.Add(nudNoBedTabMax);

            chkTabsEnabled.CheckedChanged += (s, e) =>
            {
                var enabled = chkTabsEnabled.Checked;
                nudTabWidth.Enabled = enabled;
                rbTabAll.Enabled = enabled;
                rbSupportTab.Enabled = enabled;
                UpdateNoBedRange();
                OnParametersChanged();
            };

            // The two choices toggle together, so one handler sees every change.
            rbTabAll.CheckedChanged += (s, e) =>
            {
                UpdateNoBedRange();
                OnParametersChanged();
            };
            nudNoBedTabMin.ValueChanged += (s, e) => OnParametersChanged();
            nudNoBedTabMax.ValueChanged += (s, e) => OnParametersChanged();

            // Pierce section
            var piercePanel = new CollapsiblePanel
            {
                HeaderText = "Pierce",
                Dock = DockStyle.Top,
                ExpandedHeight = 116,
                IsExpanded = true,
            };

            piercePanel.ContentPanel.Controls.Add(
                new Label
                {
                    Text = "Pierce Clearance:",
                    Location = new Point(12, 6),
                    AutoSize = true,
                }
            );

            nudPierceClearance = CreateNumeric(130, 3, 0.0625, 0.0625);
            piercePanel.ContentPanel.Controls.Add(nudPierceClearance);

            // 0 turns the pierce zone off; see docs/geometry/pierce-zone.md.
            piercePanel.ContentPanel.Controls.Add(
                new Label
                {
                    Text = "Pierce Diameter:",
                    Location = new Point(12, 32),
                    AutoSize = true,
                }
            );

            nudPierceDiameter = CreateNumeric(130, 29, 0, 0.0625);
            nudPierceDiameter.ValueChanged += (s, e) => OnParametersChanged();
            piercePanel.ContentPanel.Controls.Add(nudPierceDiameter);

            chkRoundLeadInAngles = new CheckBox
            {
                Text = "Round Lead-In Angles",
                Location = new Point(12, 58),
                AutoSize = true,
            };
            chkRoundLeadInAngles.CheckedChanged += (s, e) =>
            {
                nudLeadInAngleIncrement.Enabled = chkRoundLeadInAngles.Checked;
                OnParametersChanged();
            };
            piercePanel.ContentPanel.Controls.Add(chkRoundLeadInAngles);

            piercePanel.ContentPanel.Controls.Add(
                new Label
                {
                    Text = "Increment:",
                    Location = new Point(175, 60),
                    AutoSize = true,
                }
            );

            nudLeadInAngleIncrement = CreateNumeric(245, 57, 5, 1);
            nudLeadInAngleIncrement.DecimalPlaces = 0;
            nudLeadInAngleIncrement.Minimum = 1;
            nudLeadInAngleIncrement.Maximum = 90;
            nudLeadInAngleIncrement.Enabled = false;
            nudLeadInAngleIncrement.ValueChanged += (s, e) => OnParametersChanged();
            piercePanel.ContentPanel.Controls.Add(nudLeadInAngleIncrement);

            // Add in reverse order — Dock.Top stacks top-down
            Controls.Add(piercePanel);
            Controls.Add(tabsPanel);
            Controls.Add(tabWrapper);

            // Wire up change events
            PopulateDropdowns();
            WireChangeEvents();
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
                nudTabWidth.Value = (decimal)p.TabConfig.Size;
            nudNoBedTabMin.Value = Clamp(nudNoBedTabMin, p.NoBedTabMinWidth);
            nudNoBedTabMax.Value = Clamp(nudNoBedTabMax, p.NoBedTabMaxWidth);
            UpdateNoBedRange();
            nudPierceClearance.Value = (decimal)p.PierceClearance;
            nudPierceDiameter.Value = (decimal)p.PierceDiameter;
            chkRoundLeadInAngles.Checked = p.RoundLeadInAngles;
            nudLeadInAngleIncrement.Value = (decimal)p.LeadInAngleIncrement;
            nudLeadInAngleIncrement.Enabled = p.RoundLeadInAngles;

            suppressEvents = false;
        }

        private void UpdateNoBedRange()
        {
            var enabled = chkTabsEnabled.Checked && rbSupportTab.Checked;
            nudNoBedTabMin.Enabled = enabled;
            nudNoBedTabMax.Enabled = enabled;
        }

        private static decimal Clamp(NumericUpDown control, double value) =>
            double.IsFinite(value)
                ? System.Math.Clamp((decimal)value, control.Minimum, control.Maximum)
                : control.Minimum;

        private void OnParametersChanged()
        {
            if (!suppressEvents)
                ParametersChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void SetupTab(
            TabPage tab,
            out ComboBox leadInCombo,
            out Panel leadInPanel,
            out ComboBox leadOutCombo,
            out Panel leadOutPanel
        )
        {
            var grpLeadIn = new GroupBox
            {
                Text = "Lead-In",
                Location = new Point(4, 4),
                Size = new Size(340, 148),
            };
            tab.Controls.Add(grpLeadIn);

            grpLeadIn.Controls.Add(
                new Label
                {
                    Text = "Type:",
                    Location = new Point(8, 22),
                    AutoSize = true,
                }
            );

            leadInCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(90, 19),
                Size = new Size(230, 24),
            };
            grpLeadIn.Controls.Add(leadInCombo);

            leadInPanel = new Panel
            {
                Location = new Point(8, 48),
                Size = new Size(320, 92),
                AutoScroll = true,
            };
            grpLeadIn.Controls.Add(leadInPanel);

            var grpLeadOut = new GroupBox
            {
                Text = "Lead-Out",
                Location = new Point(4, 156),
                Size = new Size(340, 132),
            };
            tab.Controls.Add(grpLeadOut);

            grpLeadOut.Controls.Add(
                new Label
                {
                    Text = "Type:",
                    Location = new Point(8, 22),
                    AutoSize = true,
                }
            );

            leadOutCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(90, 19),
                Size = new Size(230, 24),
            };
            grpLeadOut.Controls.Add(leadOutCombo);

            leadOutPanel = new Panel
            {
                Location = new Point(8, 48),
                Size = new Size(320, 76),
                AutoScroll = true,
            };
            grpLeadOut.Controls.Add(leadOutPanel);
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

        private Panel GetLeadInPanel(ComboBox combo)
        {
            if (combo == cboExternalLeadIn)
                return pnlExternalLeadIn;
            if (combo == cboInternalLeadIn)
                return pnlInternalLeadIn;
            return null;
        }

        private Panel GetLeadOutPanel(ComboBox combo)
        {
            if (combo == cboExternalLeadOut)
                return pnlExternalLeadOut;
            if (combo == cboInternalLeadOut)
                return pnlInternalLeadOut;
            return null;
        }

        private void BuildLeadInParamControls(Panel panel, int typeIndex)
        {
            panel.Controls.Clear();
            var y = 0;

            switch (typeIndex)
            {
                case 1:
                    AddNumericField(panel, "Length:", 0.25, ref y, "Length");
                    AddNumericField(panel, "Approach Angle:", 90, ref y, "ApproachAngle");
                    break;
                case 2:
                    AddNumericField(panel, "Radius:", 0.25, ref y, "Radius");
                    break;
                case 3:
                    AddNumericField(panel, "Line Length:", 0.25, ref y, "LineLength");
                    AddNumericField(panel, "Arc Radius:", 0.125, ref y, "ArcRadius");
                    AddNumericField(panel, "Approach Angle:", 135, ref y, "ApproachAngle");
                    break;
                case 4:
                    AddNumericField(panel, "Line Length:", 0.25, ref y, "LineLength");
                    AddNumericField(panel, "Arc Radius:", 0.125, ref y, "ArcRadius");
                    AddNumericField(panel, "Kerf:", 0.06, ref y, "Kerf");
                    break;
                case 5:
                    AddNumericField(panel, "Length 1:", 0.25, ref y, "Length1");
                    AddNumericField(panel, "Angle 1:", 90, ref y, "Angle1");
                    AddNumericField(panel, "Length 2:", 0.25, ref y, "Length2");
                    AddNumericField(panel, "Angle 2:", 90, ref y, "Angle2");
                    break;
            }
        }

        private void BuildLeadOutParamControls(Panel panel, int typeIndex)
        {
            panel.Controls.Clear();
            var y = 0;

            switch (typeIndex)
            {
                case 1:
                    AddNumericField(panel, "Length:", 0.25, ref y, "Length");
                    AddNumericField(panel, "Approach Angle:", 90, ref y, "ApproachAngle");
                    break;
                case 2:
                    AddNumericField(panel, "Radius:", 0.25, ref y, "Radius");
                    break;
            }
        }

        private void AddNumericField(
            Panel panel,
            string label,
            double defaultValue,
            ref int y,
            string tag
        )
        {
            panel.Controls.Add(
                new Label
                {
                    Text = label,
                    Location = new Point(0, y + 3),
                    AutoSize = true,
                }
            );

            var nud = CreateNumeric(130, y, defaultValue, 0.0625);
            nud.Tag = tag;
            nud.ValueChanged += (s, e) => OnParametersChanged();
            panel.Controls.Add(nud);

            y += 30;
        }

        private static NumericUpDown CreateNumeric(
            int x,
            int y,
            double defaultValue,
            double increment
        )
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(120, 22),
                DecimalPlaces = 4,
                Increment = (decimal)increment,
                Minimum = 0,
                Maximum = 9999,
                Value = (decimal)defaultValue,
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
                2 => new ArcLeadIn { Radius = GetParam(panel, "Radius", 0.25) },
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
                    nud.Value = (decimal)value;
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
