using OpenNest.CNC.CuttingStrategy;
using System;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class CuttingParametersForm : Form
    {
        private static readonly string[] LeadInTypes =
            { "None", "Line", "Arc", "Line + Arc", "Clean Hole", "Line + Line" };

        private static readonly string[] LeadOutTypes =
            { "None", "Line", "Arc", "Microtab" };

        private ComboBox cboExternalLeadIn, cboExternalLeadOut;
        private ComboBox cboInternalLeadIn, cboInternalLeadOut;
        private ComboBox cboArcCircleLeadIn, cboArcCircleLeadOut;

        private Panel pnlExternalLeadIn, pnlExternalLeadOut;
        private Panel pnlInternalLeadIn, pnlInternalLeadOut;
        private Panel pnlArcCircleLeadIn, pnlArcCircleLeadOut;

        private CheckBox chkTabsEnabled;
        private NumericUpDown nudTabWidth;
        private NumericUpDown nudPierceClearance;

        private bool hasCustomParameters;
        private CuttingParameters parameters = new CuttingParameters();

        public CuttingParameters Parameters
        {
            get => parameters;
            set
            {
                parameters = value;
                hasCustomParameters = true;
            }
        }

        public CuttingParametersForm()
        {
            InitializeComponent();

            SetupTab(tabExternal,
                out cboExternalLeadIn, out pnlExternalLeadIn,
                out cboExternalLeadOut, out pnlExternalLeadOut);
            SetupTab(tabInternal,
                out cboInternalLeadIn, out pnlInternalLeadIn,
                out cboInternalLeadOut, out pnlInternalLeadOut);
            SetupTab(tabArcCircle,
                out cboArcCircleLeadIn, out pnlArcCircleLeadIn,
                out cboArcCircleLeadOut, out pnlArcCircleLeadOut);

            SetupTabsSection();
            PopulateDropdowns();

            cboExternalLeadIn.SelectedIndexChanged += OnLeadInTypeChanged;
            cboInternalLeadIn.SelectedIndexChanged += OnLeadInTypeChanged;
            cboArcCircleLeadIn.SelectedIndexChanged += OnLeadInTypeChanged;

            cboExternalLeadOut.SelectedIndexChanged += OnLeadOutTypeChanged;
            cboInternalLeadOut.SelectedIndexChanged += OnLeadOutTypeChanged;
            cboArcCircleLeadOut.SelectedIndexChanged += OnLeadOutTypeChanged;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // If caller didn't provide custom parameters, try loading saved ones
            if (!hasCustomParameters)
            {
                var json = Properties.Settings.Default.CuttingParametersJson;
                if (!string.IsNullOrEmpty(json))
                {
                    try { Parameters = CuttingParametersSerializer.Deserialize(json); }
                    catch { /* use defaults on corrupt data */ }
                }
            }

            LoadFromParameters(Parameters);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (DialogResult == System.Windows.Forms.DialogResult.OK)
            {
                var json = CuttingParametersSerializer.Serialize(BuildParameters());
                Properties.Settings.Default.CuttingParametersJson = json;
                Properties.Settings.Default.Save();
            }
        }

        private static void SetupTab(TabPage tab,
            out ComboBox leadInCombo, out Panel leadInPanel,
            out ComboBox leadOutCombo, out Panel leadOutPanel)
        {
            var grpLeadIn = new GroupBox
            {
                Text = "Lead-In",
                Location = new System.Drawing.Point(4, 4),
                Size = new System.Drawing.Size(364, 168)
            };
            tab.Controls.Add(grpLeadIn);

            grpLeadIn.Controls.Add(new Label
            {
                Text = "Type:",
                Location = new System.Drawing.Point(8, 22),
                AutoSize = true
            });

            leadInCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new System.Drawing.Point(90, 19),
                Size = new System.Drawing.Size(250, 24)
            };
            grpLeadIn.Controls.Add(leadInCombo);

            leadInPanel = new Panel
            {
                Location = new System.Drawing.Point(8, 48),
                Size = new System.Drawing.Size(340, 112),
                AutoScroll = true
            };
            grpLeadIn.Controls.Add(leadInPanel);

            var grpLeadOut = new GroupBox
            {
                Text = "Lead-Out",
                Location = new System.Drawing.Point(4, 176),
                Size = new System.Drawing.Size(364, 132)
            };
            tab.Controls.Add(grpLeadOut);

            grpLeadOut.Controls.Add(new Label
            {
                Text = "Type:",
                Location = new System.Drawing.Point(8, 22),
                AutoSize = true
            });

            leadOutCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new System.Drawing.Point(90, 19),
                Size = new System.Drawing.Size(250, 24)
            };
            grpLeadOut.Controls.Add(leadOutCombo);

            leadOutPanel = new Panel
            {
                Location = new System.Drawing.Point(8, 48),
                Size = new System.Drawing.Size(340, 76),
                AutoScroll = true
            };
            grpLeadOut.Controls.Add(leadOutPanel);
        }

        private void SetupTabsSection()
        {
            var grpTabs = new GroupBox
            {
                Text = "Tabs",
                Location = new System.Drawing.Point(4, 350),
                Size = new System.Drawing.Size(372, 55),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            chkTabsEnabled = new CheckBox
            {
                Text = "Enable Tabs",
                Location = new System.Drawing.Point(12, 22),
                AutoSize = true
            };
            chkTabsEnabled.CheckedChanged += (s, e) => nudTabWidth.Enabled = chkTabsEnabled.Checked;
            grpTabs.Controls.Add(chkTabsEnabled);

            grpTabs.Controls.Add(new Label
            {
                Text = "Width:",
                Location = new System.Drawing.Point(160, 23),
                AutoSize = true
            });

            nudTabWidth = new NumericUpDown
            {
                Location = new System.Drawing.Point(215, 20),
                Size = new System.Drawing.Size(100, 22),
                DecimalPlaces = 4,
                Increment = 0.0625m,
                Minimum = 0,
                Maximum = 9999,
                Value = 0.25m,
                Enabled = false
            };
            grpTabs.Controls.Add(nudTabWidth);

            Controls.Add(grpTabs);

            var grpPierce = new GroupBox
            {
                Text = "Pierce",
                Location = new System.Drawing.Point(4, 410),
                Size = new System.Drawing.Size(372, 55),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            grpPierce.Controls.Add(new Label
            {
                Text = "Pierce Clearance:",
                Location = new System.Drawing.Point(12, 23),
                AutoSize = true
            });

            nudPierceClearance = new NumericUpDown
            {
                Location = new System.Drawing.Point(130, 20),
                Size = new System.Drawing.Size(100, 22),
                DecimalPlaces = 4,
                Increment = 0.0625m,
                Minimum = 0,
                Maximum = 9999,
                Value = 0.0625m
            };
            grpPierce.Controls.Add(nudPierceClearance);

            Controls.Add(grpPierce);
        }

        private void PopulateDropdowns()
        {
            foreach (var combo in new[] { cboExternalLeadIn, cboInternalLeadIn, cboArcCircleLeadIn })
            {
                combo.Items.AddRange(LeadInTypes);
                combo.SelectedIndex = 0;
            }

            foreach (var combo in new[] { cboExternalLeadOut, cboInternalLeadOut, cboArcCircleLeadOut })
            {
                combo.Items.AddRange(LeadOutTypes);
                combo.SelectedIndex = 0;
            }
        }

        private void OnLeadInTypeChanged(object sender, EventArgs e)
        {
            var combo = (ComboBox)sender;
            var panel = GetLeadInPanel(combo);
            if (panel != null)
                BuildLeadInParamControls(panel, combo.SelectedIndex);
        }

        private void OnLeadOutTypeChanged(object sender, EventArgs e)
        {
            var combo = (ComboBox)sender;
            var panel = GetLeadOutPanel(combo);
            if (panel != null)
                BuildLeadOutParamControls(panel, combo.SelectedIndex);
        }

        private Panel GetLeadInPanel(ComboBox combo)
        {
            if (combo == cboExternalLeadIn) return pnlExternalLeadIn;
            if (combo == cboInternalLeadIn) return pnlInternalLeadIn;
            if (combo == cboArcCircleLeadIn) return pnlArcCircleLeadIn;
            return null;
        }

        private Panel GetLeadOutPanel(ComboBox combo)
        {
            if (combo == cboExternalLeadOut) return pnlExternalLeadOut;
            if (combo == cboInternalLeadOut) return pnlInternalLeadOut;
            if (combo == cboArcCircleLeadOut) return pnlArcCircleLeadOut;
            return null;
        }

        private static void BuildLeadInParamControls(Panel panel, int typeIndex)
        {
            panel.Controls.Clear();
            var y = 0;

            switch (typeIndex)
            {
                case 1: // Line
                    AddNumericField(panel, "Length:", 0.25, ref y, "Length");
                    AddNumericField(panel, "Approach Angle:", 90, ref y, "ApproachAngle");
                    break;
                case 2: // Arc
                    AddNumericField(panel, "Radius:", 0.25, ref y, "Radius");
                    break;
                case 3: // Line + Arc
                    AddNumericField(panel, "Line Length:", 0.25, ref y, "LineLength");
                    AddNumericField(panel, "Arc Radius:", 0.125, ref y, "ArcRadius");
                    AddNumericField(panel, "Approach Angle:", 135, ref y, "ApproachAngle");
                    break;
                case 4: // Clean Hole
                    AddNumericField(panel, "Line Length:", 0.25, ref y, "LineLength");
                    AddNumericField(panel, "Arc Radius:", 0.125, ref y, "ArcRadius");
                    AddNumericField(panel, "Kerf:", 0.06, ref y, "Kerf");
                    break;
                case 5: // Line + Line
                    AddNumericField(panel, "Length 1:", 0.25, ref y, "Length1");
                    AddNumericField(panel, "Angle 1:", 90, ref y, "Angle1");
                    AddNumericField(panel, "Length 2:", 0.25, ref y, "Length2");
                    AddNumericField(panel, "Angle 2:", 90, ref y, "Angle2");
                    break;
            }
        }

        private static void BuildLeadOutParamControls(Panel panel, int typeIndex)
        {
            panel.Controls.Clear();
            var y = 0;

            switch (typeIndex)
            {
                case 1: // Line
                    AddNumericField(panel, "Length:", 0.25, ref y, "Length");
                    AddNumericField(panel, "Approach Angle:", 90, ref y, "ApproachAngle");
                    break;
                case 2: // Arc
                    AddNumericField(panel, "Radius:", 0.25, ref y, "Radius");
                    break;
                case 3: // Microtab
                    AddNumericField(panel, "Gap Size:", 0.06, ref y, "GapSize");
                    break;
            }
        }

        private static void AddNumericField(Panel panel, string label, double defaultValue,
            ref int y, string tag)
        {
            panel.Controls.Add(new Label
            {
                Text = label,
                Location = new System.Drawing.Point(0, y + 3),
                AutoSize = true
            });

            panel.Controls.Add(new NumericUpDown
            {
                Location = new System.Drawing.Point(130, y),
                Size = new System.Drawing.Size(120, 22),
                DecimalPlaces = 4,
                Increment = 0.0625m,
                Minimum = 0,
                Maximum = 9999,
                Value = (decimal)defaultValue,
                Tag = tag
            });

            y += 30;
        }

        private void LoadFromParameters(CuttingParameters p)
        {
            LoadLeadIn(cboExternalLeadIn, pnlExternalLeadIn, p.ExternalLeadIn);
            LoadLeadOut(cboExternalLeadOut, pnlExternalLeadOut, p.ExternalLeadOut);

            LoadLeadIn(cboInternalLeadIn, pnlInternalLeadIn, p.InternalLeadIn);
            LoadLeadOut(cboInternalLeadOut, pnlInternalLeadOut, p.InternalLeadOut);

            LoadLeadIn(cboArcCircleLeadIn, pnlArcCircleLeadIn, p.ArcCircleLeadIn);
            LoadLeadOut(cboArcCircleLeadOut, pnlArcCircleLeadOut, p.ArcCircleLeadOut);

            chkTabsEnabled.Checked = p.TabsEnabled;
            if (p.TabConfig != null)
                nudTabWidth.Value = (decimal)p.TabConfig.Size;

            nudPierceClearance.Value = (decimal)p.PierceClearance;
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
                case MicrotabLeadOut microtab:
                    combo.SelectedIndex = 3;
                    SetParam(panel, "GapSize", microtab.GapSize);
                    break;
                default:
                    combo.SelectedIndex = 0;
                    break;
            }
        }

        public CuttingParameters BuildParameters()
        {
            var p = new CuttingParameters
            {
                ExternalLeadIn = BuildLeadIn(cboExternalLeadIn, pnlExternalLeadIn),
                ExternalLeadOut = BuildLeadOut(cboExternalLeadOut, pnlExternalLeadOut),
                InternalLeadIn = BuildLeadIn(cboInternalLeadIn, pnlInternalLeadIn),
                InternalLeadOut = BuildLeadOut(cboInternalLeadOut, pnlInternalLeadOut),
                ArcCircleLeadIn = BuildLeadIn(cboArcCircleLeadIn, pnlArcCircleLeadIn),
                ArcCircleLeadOut = BuildLeadOut(cboArcCircleLeadOut, pnlArcCircleLeadOut),
                TabsEnabled = chkTabsEnabled.Checked,
                TabConfig = new NormalTab { Size = (double)nudTabWidth.Value },
                PierceClearance = (double)nudPierceClearance.Value
            };
            return p;
        }

        private static LeadIn BuildLeadIn(ComboBox combo, Panel panel)
        {
            switch (combo.SelectedIndex)
            {
                case 1:
                    return new LineLeadIn
                    {
                        Length = GetParam(panel, "Length", 0.25),
                        ApproachAngle = GetParam(panel, "ApproachAngle", 90)
                    };
                case 2:
                    return new ArcLeadIn
                    {
                        Radius = GetParam(panel, "Radius", 0.25)
                    };
                case 3:
                    return new LineArcLeadIn
                    {
                        LineLength = GetParam(panel, "LineLength", 0.25),
                        ArcRadius = GetParam(panel, "ArcRadius", 0.125),
                        ApproachAngle = GetParam(panel, "ApproachAngle", 135)
                    };
                case 4:
                    return new CleanHoleLeadIn
                    {
                        LineLength = GetParam(panel, "LineLength", 0.25),
                        ArcRadius = GetParam(panel, "ArcRadius", 0.125),
                        Kerf = GetParam(panel, "Kerf", 0.06)
                    };
                case 5:
                    return new LineLineLeadIn
                    {
                        Length1 = GetParam(panel, "Length1", 0.25),
                        ApproachAngle1 = GetParam(panel, "Angle1", 90),
                        Length2 = GetParam(panel, "Length2", 0.25),
                        ApproachAngle2 = GetParam(panel, "Angle2", 90)
                    };
                default:
                    return new NoLeadIn();
            }
        }

        private static LeadOut BuildLeadOut(ComboBox combo, Panel panel)
        {
            switch (combo.SelectedIndex)
            {
                case 1:
                    return new LineLeadOut
                    {
                        Length = GetParam(panel, "Length", 0.25),
                        ApproachAngle = GetParam(panel, "ApproachAngle", 90)
                    };
                case 2:
                    return new ArcLeadOut
                    {
                        Radius = GetParam(panel, "Radius", 0.25)
                    };
                case 3:
                    return new MicrotabLeadOut
                    {
                        GapSize = GetParam(panel, "GapSize", 0.06)
                    };
                default:
                    return new NoLeadOut();
            }
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
