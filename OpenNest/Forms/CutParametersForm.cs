using System;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class CutParametersForm : Form
    {
        private Units units;

        public CutParametersForm()
        {
            InitializeComponent();
            Units = OpenNest.Units.Inches;
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
            Properties.Settings.Default.Save();
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
                Units = units
            };
        }
    }
}
