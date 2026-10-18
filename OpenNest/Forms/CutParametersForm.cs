using System;
using System.Windows.Forms;
using NumericUpDown = OpenNest.Controls.NumericUpDown;

namespace OpenNest.Forms
{
    public partial class CutParametersForm : Form
    {
        private Units units;
        private readonly NumericUpDown holeFeedBox;
        private readonly CutTimeMachineBox machineBox;
        private readonly CheckBox skipEtchBox;

        public CutParametersForm()
        {
            InitializeComponent();

            label1.Text = "Outside feed :";
            holeFeedBox = new NumericUpDown
            {
                Anchor = numericUpDown1.Anchor,
                Minimum = numericUpDown1.Minimum,
                Maximum = numericUpDown1.Maximum,
                Size = numericUpDown1.Size,
            };
            machineBox = new CutTimeMachineBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            skipEtchBox = new CheckBox { Text = "Skip etch-only contours", AutoSize = true, Anchor = AnchorStyles.Left };

            // Rows after the designer's feed, rapid and pierce.
            tableLayoutPanel1.RowCount = 6;
            tableLayoutPanel1.RowStyles.Clear();
            for (var i = 0; i < 6; i++)
                tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6));
            tableLayoutPanel1.Controls.Add(RowLabel("Hole feed :"), 0, 3);
            tableLayoutPanel1.Controls.Add(holeFeedBox, 1, 3);
            tableLayoutPanel1.Controls.Add(RowLabel("Acceleration :"), 0, 4);
            tableLayoutPanel1.Controls.Add(machineBox, 1, 4);
            tableLayoutPanel1.Controls.Add(skipEtchBox, 1, 5);
            Height += LogicalToDeviceUnits(120);

            Units = OpenNest.Units.Inches;
        }

        private static Label RowLabel(string text) =>
            new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            var saved = SavedCutParameters.Load(units);
            numericUpDown1.Value = Clamp(numericUpDown1, saved.Feedrate);
            holeFeedBox.Value = Clamp(holeFeedBox, saved.HoleFeedrate ?? saved.Feedrate);
            numericUpDown2.Value = Clamp(numericUpDown2, saved.RapidTravelRate);
            numericUpDown3.Value = Clamp(numericUpDown3, saved.PierceTime.TotalSeconds);
            skipEtchBox.Checked = saved.SkipScribe;
        }

        private static decimal Clamp(System.Windows.Forms.NumericUpDown box, double value) =>
            System.Math.Clamp((decimal)value, box.Minimum, box.Maximum);

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            SavedCutParameters.Save(GetCutParameters());
            machineBox.SaveChoice();
        }

        public Units Units
        {
            get { return units; }
            set
            {
                units = value;
                SetUnits(value);
            }
        }

        private void SetUnits(Units units)
        {
            var suffix = UnitsHelper.GetShortTimeUnitPair(units);

            numericUpDown1.Suffix = " " + suffix;
            numericUpDown2.Suffix = " " + suffix;
            holeFeedBox.Suffix = " " + suffix;
        }

        public CutParameters GetCutParameters()
        {
            return new CutParameters()
            {
                Feedrate = (double)numericUpDown1.Value,
                HoleFeedrate = (double)holeFeedBox.Value,
                RapidTravelRate = (double)numericUpDown2.Value,
                PierceTime = TimeSpan.FromSeconds((double)numericUpDown3.Value),
                SkipScribe = skipEtchBox.Checked,
                Units = units,
                Motion = machineBox.GetMotion(units),
            };
        }
    }
}
