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

        private Panel pnlExternalParams, pnlInternalParams, pnlArcCircleParams;

        public CuttingParameters Parameters { get; set; } = new CuttingParameters();

        public CuttingParametersForm()
        {
            InitializeComponent();

            SetupTab(tabExternal, out cboExternalLeadIn, out cboExternalLeadOut, out pnlExternalParams);
            SetupTab(tabInternal, out cboInternalLeadIn, out cboInternalLeadOut, out pnlInternalParams);
            SetupTab(tabArcCircle, out cboArcCircleLeadIn, out cboArcCircleLeadOut, out pnlArcCircleParams);

            PopulateDropdowns();

            cboExternalLeadIn.SelectedIndexChanged += OnLeadInTypeChanged;
            cboInternalLeadIn.SelectedIndexChanged += OnLeadInTypeChanged;
            cboArcCircleLeadIn.SelectedIndexChanged += OnLeadInTypeChanged;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LoadFromParameters(Parameters);
        }

        private static void SetupTab(TabPage tab, out ComboBox leadInCombo,
            out ComboBox leadOutCombo, out Panel paramPanel)
        {
            var y = 12;

            var lblLeadIn = new Label
            {
                Text = "Lead-In:",
                Location = new System.Drawing.Point(8, y + 3),
                AutoSize = true
            };
            tab.Controls.Add(lblLeadIn);

            leadInCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new System.Drawing.Point(100, y),
                Size = new System.Drawing.Size(240, 24)
            };
            tab.Controls.Add(leadInCombo);

            y += 32;

            var lblLeadOut = new Label
            {
                Text = "Lead-Out:",
                Location = new System.Drawing.Point(8, y + 3),
                AutoSize = true
            };
            tab.Controls.Add(lblLeadOut);

            leadOutCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new System.Drawing.Point(100, y),
                Size = new System.Drawing.Size(240, 24)
            };
            tab.Controls.Add(leadOutCombo);

            y += 40;

            paramPanel = new Panel
            {
                Location = new System.Drawing.Point(8, y),
                Size = new System.Drawing.Size(332, 170),
                AutoScroll = true
            };
            tab.Controls.Add(paramPanel);
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
            var panel = GetParamPanel(combo);
            if (panel != null)
                BuildParamControls(panel, combo.SelectedIndex);
        }

        private Panel GetParamPanel(ComboBox combo)
        {
            if (combo == cboExternalLeadIn) return pnlExternalParams;
            if (combo == cboInternalLeadIn) return pnlInternalParams;
            if (combo == cboArcCircleLeadIn) return pnlArcCircleParams;
            return null;
        }

        private static void BuildParamControls(Panel panel, int typeIndex)
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

        private static void AddNumericField(Panel panel, string label, double defaultValue,
            ref int y, string tag)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new System.Drawing.Point(0, y + 3),
                AutoSize = true
            };
            panel.Controls.Add(lbl);

            var nud = new System.Windows.Forms.NumericUpDown
            {
                Location = new System.Drawing.Point(130, y),
                Size = new System.Drawing.Size(120, 22),
                DecimalPlaces = 4,
                Increment = 0.0625m,
                Minimum = 0,
                Maximum = 9999,
                Value = (decimal)defaultValue,
                Tag = tag
            };
            panel.Controls.Add(nud);

            y += 30;
        }

        private void LoadFromParameters(CuttingParameters p)
        {
            LoadLeadIn(cboExternalLeadIn, pnlExternalParams, p.ExternalLeadIn);
            LoadLeadOut(cboExternalLeadOut, p.ExternalLeadOut);

            LoadLeadIn(cboInternalLeadIn, pnlInternalParams, p.InternalLeadIn);
            LoadLeadOut(cboInternalLeadOut, p.InternalLeadOut);

            LoadLeadIn(cboArcCircleLeadIn, pnlArcCircleParams, p.ArcCircleLeadIn);
            LoadLeadOut(cboArcCircleLeadOut, p.ArcCircleLeadOut);
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

        private static void LoadLeadOut(ComboBox combo, LeadOut leadOut)
        {
            switch (leadOut)
            {
                case LineLeadOut _:
                    combo.SelectedIndex = 1;
                    break;
                case ArcLeadOut _:
                    combo.SelectedIndex = 2;
                    break;
                case MicrotabLeadOut _:
                    combo.SelectedIndex = 3;
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
                ExternalLeadIn = BuildLeadIn(cboExternalLeadIn, pnlExternalParams),
                ExternalLeadOut = BuildLeadOut(cboExternalLeadOut),
                InternalLeadIn = BuildLeadIn(cboInternalLeadIn, pnlInternalParams),
                InternalLeadOut = BuildLeadOut(cboInternalLeadOut),
                ArcCircleLeadIn = BuildLeadIn(cboArcCircleLeadIn, pnlArcCircleParams),
                ArcCircleLeadOut = BuildLeadOut(cboArcCircleLeadOut)
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

        private static LeadOut BuildLeadOut(ComboBox combo)
        {
            switch (combo.SelectedIndex)
            {
                case 1:
                    return new LineLeadOut { Length = 0.25, ApproachAngle = 90 };
                case 2:
                    return new ArcLeadOut { Radius = 0.25 };
                case 3:
                    return new MicrotabLeadOut();
                default:
                    return new NoLeadOut();
            }
        }

        private static void SetParam(Panel panel, string tag, double value)
        {
            foreach (Control c in panel.Controls)
            {
                if (c is System.Windows.Forms.NumericUpDown nud && (string)nud.Tag == tag)
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
                if (c is System.Windows.Forms.NumericUpDown nud && (string)nud.Tag == tag)
                    return (double)nud.Value;
            }
            return defaultValue;
        }
    }
}
