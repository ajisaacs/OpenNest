using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Controls
{
    /// <summary>
    /// Edits one contour type's lead-in and lead-out: a type list for each, the chosen type's
    /// fields below it, and a <see cref="LeadPreview"/> filling the rest. Cutting Settings and
    /// Machine Configuration both use it; the options hide what a host cannot hold.
    /// </summary>
    public class LeadsEditor : UserControl
    {
        private const string None = "None";
        private const string Line = "Line";
        private const string Arc = "Arc";
        private const string LineArc = "Line + Arc";
        private const string CleanHole = "Clean Hole";
        private const string LineLine = "Line + Line";

        private static readonly string[] AllLeadInTypes = { None, Line, Arc, LineArc, CleanHole, LineLine };
        private static readonly string[] LeadOutTypes = { None, Line, Arc };

        // Every field label shares this column so inputs line up across hosts.
        private const int LabelWidth = 150;
        private const int FieldIndent = 16;
        private const int InputWidth = 100;
        // The preview's least height, below the type with the most fields.
        private const int PreviewHeight = 110;

        private static readonly Color HintColor = SystemColors.GrayText;

        private readonly TableLayoutPanel content;
        private readonly ComboBox leadInCombo;
        private readonly ComboBox leadOutCombo;
        private readonly TableLayoutPanel leadInFields;
        private readonly TableLayoutPanel leadOutFields;
        private readonly LeadPreview preview = new();

        private bool allowCleanHole = true;
        private bool allowLineLine = true;
        private bool showArcSweep = true;
        private bool showCleanHoleKerf = true;
        private string lengthUnit;
        private bool loading;

        /// <summary>Raised when the user changes the lead-in's type or fields.</summary>
        public event EventHandler LeadInChanged;

        /// <summary>Raised when the user changes the lead-out's type or fields.</summary>
        public event EventHandler LeadOutChanged;

        public LeadsEditor()
        {
            BackColor = Color.White;

            content = new TableLayoutPanel
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
            leadInFields = CreateGrid(FieldIndent);
            content.Controls.Add(leadInFields);

            leadOutCombo = CreateCombo();
            var leadOutType = CreateGrid(0);
            leadOutType.Margin = new Padding(0, 8, 0, 0);
            AddRow(leadOutType, "Lead-out:", leadOutCombo, 0, bold: true);
            content.Controls.Add(leadOutType);
            leadOutFields = CreateGrid(FieldIndent);
            content.Controls.Add(leadOutFields);

            // The preview fills whatever the fields leave, so it grows for types with fewer.
            preview.Dock = DockStyle.Fill;
            preview.Margin = new Padding(0, 6, 0, 0);
            Controls.Add(preview);
            Controls.Add(content);
            preview.BringToFront();

            FillCombo(leadInCombo, LeadInTypes(), None);
            FillCombo(leadOutCombo, LeadOutTypes, None);
            leadInCombo.SelectedIndexChanged += (s, e) =>
            {
                BuildLeadInFields();
                if (!loading)
                    LeadInChanged?.Invoke(this, EventArgs.Empty);
            };
            leadOutCombo.SelectedIndexChanged += (s, e) =>
            {
                BuildLeadOutFields();
                if (!loading)
                    LeadOutChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        /// <summary>The contour the preview draws strong; the other one's leads are faint.</summary>
        [DefaultValue(ContourType.External)]
        public ContourType Emphasis
        {
            get => preview.Emphasis;
            set
            {
                preview.Emphasis = value;
                preview.Invalidate();
            }
        }

        /// <summary>Whether Clean Hole is offered; a lead already of that type still shows.</summary>
        [DefaultValue(true)]
        public bool AllowCleanHole
        {
            get => allowCleanHole;
            set
            {
                allowCleanHole = value;
                ReloadLeadInTypes();
            }
        }

        /// <summary>Whether Line + Line is offered; a lead already of that type still shows.</summary>
        [DefaultValue(true)]
        public bool AllowLineLine
        {
            get => allowLineLine;
            set
            {
                allowLineLine = value;
                ReloadLeadInTypes();
            }
        }

        /// <summary>Whether an arc lead-in shows its sweep angle; hidden, it is the default half circle.</summary>
        [DefaultValue(true)]
        public bool ShowArcSweep
        {
            get => showArcSweep;
            set
            {
                showArcSweep = value;
                RebuildFields();
            }
        }

        /// <summary>Whether a clean-hole lead-in shows its kerf; hidden, the host supplies it.</summary>
        [DefaultValue(true)]
        public bool ShowCleanHoleKerf
        {
            get => showCleanHoleKerf;
            set
            {
                showCleanHoleKerf = value;
                RebuildFields();
            }
        }

        /// <summary>A unit shown after each length field, or null for none.</summary>
        [DefaultValue(null)]
        public string LengthUnit
        {
            get => lengthUnit;
            set
            {
                lengthUnit = value;
                RebuildFields();
            }
        }

        /// <summary>The whole plate's leads, which the preview draws.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public CuttingParameters PreviewParameters
        {
            get => preview.Parameters;
            set => preview.Parameters = value;
        }

        /// <summary>Whether the type lists and fields take edits; the preview stays.</summary>
        [DefaultValue(true)]
        public bool FieldsEnabled
        {
            get => content.Enabled;
            set => content.Enabled = value;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public LeadIn LeadIn
        {
            get => (string)leadInCombo.SelectedItem switch
            {
                Line => new LineLeadIn
                {
                    Length = GetParam(leadInFields, "Length", 0.25),
                    ApproachAngle = GetParam(leadInFields, "ApproachAngle", 90),
                },
                Arc => new ArcLeadIn
                {
                    Radius = GetParam(leadInFields, "Radius", 0.25),
                    SweepAngle = GetParam(leadInFields, "SweepAngle", ArcLeadIn.DefaultSweepAngle),
                },
                LineArc => new LineArcLeadIn
                {
                    LineLength = GetParam(leadInFields, "LineLength", 0.25),
                    ArcRadius = GetParam(leadInFields, "ArcRadius", 0.125),
                    ApproachAngle = GetParam(leadInFields, "ApproachAngle", 135),
                },
                CleanHole => new CleanHoleLeadIn
                {
                    LineLength = GetParam(leadInFields, "LineLength", 0.25),
                    ArcRadius = GetParam(leadInFields, "ArcRadius", 0.125),
                    Kerf = GetParam(leadInFields, "Kerf", 0.06),
                },
                LineLine => new LineLineLeadIn
                {
                    Length1 = GetParam(leadInFields, "Length1", 0.25),
                    ApproachAngle1 = GetParam(leadInFields, "Angle1", 90),
                    Length2 = GetParam(leadInFields, "Length2", 0.25),
                    ApproachAngle2 = GetParam(leadInFields, "Angle2", 90),
                },
                _ => new NoLeadIn(),
            };
            set
            {
                loading = true;
                FillCombo(leadInCombo, LeadInTypes(), TypeOf(value));
                switch (value)
                {
                    case LineLeadIn line:
                        SetParam(leadInFields, "Length", line.Length);
                        SetParam(leadInFields, "ApproachAngle", line.ApproachAngle);
                        break;
                    case ArcLeadIn arc:
                        SetParam(leadInFields, "Radius", arc.Radius);
                        SetParam(leadInFields, "SweepAngle", arc.SweepAngle);
                        break;
                    case LineArcLeadIn lineArc:
                        SetParam(leadInFields, "LineLength", lineArc.LineLength);
                        SetParam(leadInFields, "ArcRadius", lineArc.ArcRadius);
                        SetParam(leadInFields, "ApproachAngle", lineArc.ApproachAngle);
                        break;
                    case CleanHoleLeadIn cleanHole:
                        SetParam(leadInFields, "LineLength", cleanHole.LineLength);
                        SetParam(leadInFields, "ArcRadius", cleanHole.ArcRadius);
                        SetParam(leadInFields, "Kerf", cleanHole.Kerf);
                        break;
                    case LineLineLeadIn lineLine:
                        SetParam(leadInFields, "Length1", lineLine.Length1);
                        SetParam(leadInFields, "Angle1", lineLine.ApproachAngle1);
                        SetParam(leadInFields, "Length2", lineLine.Length2);
                        SetParam(leadInFields, "Angle2", lineLine.ApproachAngle2);
                        break;
                }
                loading = false;
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public LeadOut LeadOut
        {
            get => (string)leadOutCombo.SelectedItem switch
            {
                Line => new LineLeadOut
                {
                    Length = GetParam(leadOutFields, "Length", 0.25),
                    ApproachAngle = GetParam(leadOutFields, "ApproachAngle", 90),
                },
                Arc => new ArcLeadOut { Radius = GetParam(leadOutFields, "Radius", 0.25) },
                _ => new NoLeadOut(),
            };
            set
            {
                loading = true;
                FillCombo(leadOutCombo, LeadOutTypes, value switch
                {
                    LineLeadOut => Line,
                    ArcLeadOut => Arc,
                    _ => None,
                });
                switch (value)
                {
                    case LineLeadOut line:
                        SetParam(leadOutFields, "Length", line.Length);
                        SetParam(leadOutFields, "ApproachAngle", line.ApproachAngle);
                        break;
                    case ArcLeadOut arc:
                        SetParam(leadOutFields, "Radius", arc.Radius);
                        break;
                }
                loading = false;
            }
        }

        /// <summary>
        /// The height that fits the lead-in and lead-out with the most fields above the preview's
        /// least height, so a host can size for it and picking a type never clips its fields.
        /// </summary>
        public int GetTallestHeight()
        {
            var (savedIn, savedOut) = (LeadIn, LeadOut);
            loading = true;

            var tallestLeadIn = 0;
            foreach (var type in leadInCombo.Items.Cast<string>().ToList())
            {
                leadInCombo.SelectedItem = type;
                tallestLeadIn = System.Math.Max(tallestLeadIn, leadInFields.GetPreferredSize(Size.Empty).Height);
            }
            var tallestLeadOut = 0;
            foreach (var type in leadOutCombo.Items.Cast<string>().ToList())
            {
                leadOutCombo.SelectedItem = type;
                tallestLeadOut = System.Math.Max(tallestLeadOut, leadOutFields.GetPreferredSize(Size.Empty).Height);
            }

            leadInCombo.SelectedItem = None;
            leadOutCombo.SelectedItem = None;
            var rows = content.GetPreferredSize(Size.Empty).Height;
            loading = false;
            LeadIn = savedIn;
            LeadOut = savedOut;
            return rows + tallestLeadIn + tallestLeadOut + PreviewHeight + Padding.Vertical;
        }

        private IEnumerable<string> LeadInTypes() =>
            AllLeadInTypes.Where(t => (allowCleanHole || t != CleanHole) && (allowLineLine || t != LineLine));

        // Keeps the values while the fields are laid out again.
        private void RebuildFields()
        {
            var (leadIn, leadOut) = (LeadIn, LeadOut);
            loading = true;
            BuildLeadInFields();
            BuildLeadOutFields();
            loading = false;
            LeadIn = leadIn;
            LeadOut = leadOut;
        }

        private void ReloadLeadInTypes()
        {
            var lead = LeadIn;
            LeadIn = lead;
        }

        private static string TypeOf(LeadIn leadIn) => leadIn switch
        {
            LineLeadIn => Line,
            ArcLeadIn => Arc,
            LineArcLeadIn => LineArc,
            CleanHoleLeadIn => CleanHole,
            LineLineLeadIn => LineLine,
            _ => None,
        };

        // A type the list does not offer still shows when it is the lead's.
        private static void FillCombo(ComboBox combo, IEnumerable<string> types, string selected)
        {
            var items = types.ToList();
            if (!items.Contains(selected))
                items.Add(selected);
            if (combo.Items.Cast<string>().SequenceEqual(items))
            {
                combo.SelectedItem = selected;
                return;
            }
            combo.Items.Clear();
            combo.Items.AddRange(items.ToArray());
            combo.SelectedItem = selected;
        }

        private void BuildLeadInFields()
        {
            ClearRows(leadInFields);
            switch ((string)leadInCombo.SelectedItem)
            {
                case Line:
                    AddLength(leadInFields, "Length:", 0.25, "Length");
                    AddAngle(leadInFields, "Approach angle:", 90, "ApproachAngle");
                    break;
                case Arc:
                    AddLength(leadInFields, "Radius:", 0.25, "Radius");
                    // Degrees of arc: 90 is a quarter circle, 180 a half circle.
                    if (showArcSweep)
                        AddAngle(leadInFields, "Sweep angle:", ArcLeadIn.DefaultSweepAngle, "SweepAngle",
                            increment: 15, minimum: 1, maximum: 180);
                    break;
                case LineArc:
                    AddLength(leadInFields, "Line length:", 0.25, "LineLength");
                    AddLength(leadInFields, "Arc radius:", 0.125, "ArcRadius");
                    AddAngle(leadInFields, "Approach angle:", 135, "ApproachAngle");
                    break;
                case CleanHole:
                    AddLength(leadInFields, "Line length:", 0.25, "LineLength");
                    AddLength(leadInFields, "Arc radius:", 0.125, "ArcRadius");
                    if (showCleanHoleKerf)
                        AddLength(leadInFields, "Kerf:", 0.06, "Kerf");
                    break;
                case LineLine:
                    AddLength(leadInFields, "First length:", 0.25, "Length1");
                    AddAngle(leadInFields, "First angle:", 90, "Angle1");
                    AddLength(leadInFields, "Second length:", 0.25, "Length2");
                    AddAngle(leadInFields, "Second angle:", 90, "Angle2");
                    break;
            }
        }

        private void BuildLeadOutFields()
        {
            ClearRows(leadOutFields);
            switch ((string)leadOutCombo.SelectedItem)
            {
                case Line:
                    AddLength(leadOutFields, "Length:", 0.25, "Length");
                    AddAngle(leadOutFields, "Approach angle:", 90, "ApproachAngle");
                    break;
                case Arc:
                    AddLength(leadOutFields, "Radius:", 0.25, "Radius");
                    break;
            }
        }

        private void AddLength(TableLayoutPanel grid, string label, double defaultValue, string tag) =>
            AddNumeric(grid, label, defaultValue, tag, 0.0625, 0, 9999, lengthUnit, 4);

        private void AddAngle(
            TableLayoutPanel grid,
            string label,
            double defaultValue,
            string tag,
            double increment = 5,
            double minimum = 0,
            double maximum = 360
        ) => AddNumeric(grid, label, defaultValue, tag, increment, minimum, maximum, "°", 2);

        private void AddNumeric(
            TableLayoutPanel grid,
            string label,
            double defaultValue,
            string tag,
            double increment,
            double minimum,
            double maximum,
            string suffix,
            int decimalPlaces
        )
        {
            var nud = new NumericUpDown
            {
                Width = InputWidth,
                DecimalPlaces = decimalPlaces,
                Increment = (decimal)increment,
                Minimum = (decimal)minimum,
                Maximum = (decimal)maximum,
                TextAlign = HorizontalAlignment.Right,
                Tag = tag,
            };
            nud.Value = System.Math.Clamp((decimal)defaultValue, nud.Minimum, nud.Maximum);
            var leadInField = grid == leadInFields;
            nud.ValueChanged += (s, e) =>
            {
                if (loading)
                    return;
                (leadInField ? LeadInChanged : LeadOutChanged)?.Invoke(this, EventArgs.Empty);
            };
            AddRow(grid, label, nud, FieldIndent, suffix);
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
            var row = grid.RowCount;
            grid.RowCount = row + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

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
                grid.Controls.Add(
                    new Label
                    {
                        Text = hint,
                        AutoSize = true,
                        ForeColor = HintColor,
                        Anchor = AnchorStyles.Left,
                        Margin = new Padding(4, 0, 0, 0),
                    },
                    2,
                    row
                );
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

        private static ComboBox CreateCombo() =>
            new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 150,
            };
    }
}
