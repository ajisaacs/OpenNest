using System;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class CutParametersForm : Form
    {
        private Units units;
        private readonly CutTimeMachineBox machineBox;

        public CutParametersForm()
        {
            InitializeComponent();
            Units = OpenNest.Units.Inches;

            machineBox = new CutTimeMachineBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            var label = new Label
            {
                Text = "Acceleration:",
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
            };
            tableLayoutPanel1.RowCount = 4;
            tableLayoutPanel1.RowStyles.Clear();
            for (var i = 0; i < 4; i++)
                tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.Controls.Add(label, 0, 3);
            tableLayoutPanel1.Controls.Add(machineBox, 1, 3);
            Height += LogicalToDeviceUnits(40);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            numericUpDown1.Value = Properties.Settings.Default.LastFeedrate;
            numericUpDown2.Value = Properties.Settings.Default.LastRapidFeedrate;
            numericUpDown3.Value = Properties.Settings.Default.LastPierceTime;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            Properties.Settings.Default.LastFeedrate = numericUpDown1.Value;
            Properties.Settings.Default.LastRapidFeedrate = numericUpDown2.Value;
            Properties.Settings.Default.LastPierceTime = numericUpDown3.Value;
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
        }

        public CutParameters GetCutParameters()
        {
            return new CutParameters()
            {
                Feedrate = (double)numericUpDown1.Value,
                RapidTravelRate = (double)numericUpDown2.Value,
                PierceTime = TimeSpan.FromSeconds((double)numericUpDown3.Value),
                Units = units,
                Motion = machineBox.GetMotion(units),
            };
        }
    }
}
