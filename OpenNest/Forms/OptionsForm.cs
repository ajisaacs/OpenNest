using OpenNest.Engine.Strategies;
using OpenNest.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class OptionsForm : Form
    {
        private readonly List<CheckBox> _strategyCheckBoxes = new();

        public OptionsForm()
        {
            InitializeComponent();
            BuildStrategyCheckBoxes();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LoadSettings();
        }

        private void BuildStrategyCheckBoxes()
        {
            var strategies = FillStrategyRegistry.AllStrategies;
            var y = 20;

            foreach (var strategy in strategies)
            {
                var cb = new CheckBox
                {
                    Text = strategy.Name,
                    Tag = strategy.Name,
                    AutoSize = true,
                    Location = new System.Drawing.Point(10, y),
                };
                strategyGroupBox.Controls.Add(cb);
                _strategyCheckBoxes.Add(cb);
                y += 24;
            }
        }

        private void LoadSettings()
        {
            textBox1.Text = Settings.Default.NestTemplatePath;
            checkBox1.Checked = Settings.Default.CreateNewNestOnOpen;
            numericUpDown1.Value = (decimal)Settings.Default.AutoSizePlateFactor;
            numericUpDown2.Value = (decimal)Settings.Default.ImportSplinePrecision;

            var disabledNames = ParseDisabledStrategies(Settings.Default.DisabledStrategies);
            foreach (var cb in _strategyCheckBoxes)
                cb.Checked = !disabledNames.Contains((string)cb.Tag);
        }

        private void SaveSettings()
        {
            Settings.Default.NestTemplatePath = textBox1.Text;
            Settings.Default.CreateNewNestOnOpen = checkBox1.Checked;
            Settings.Default.AutoSizePlateFactor = (double)numericUpDown1.Value;
            Settings.Default.ImportSplinePrecision = (int)numericUpDown2.Value;

            var disabledNames = _strategyCheckBoxes
                .Where(cb => !cb.Checked)
                .Select(cb => (string)cb.Tag);
            Settings.Default.DisabledStrategies = string.Join(",", disabledNames);

            Settings.Default.Save();
            ApplyDisabledStrategies();
        }

        /// <summary>
        /// Applies the DisabledStrategies setting to the FillStrategyRegistry.
        /// Called on save and at startup from MainForm.
        /// </summary>
        public static void ApplyDisabledStrategies()
        {
            // Re-enable all, then disable the persisted set.
            var all = FillStrategyRegistry.AllStrategies.Select(s => s.Name).ToArray();
            FillStrategyRegistry.Enable(all);

            var disabled = ParseDisabledStrategies(Settings.Default.DisabledStrategies);
            if (disabled.Count > 0)
                FillStrategyRegistry.Disable(disabled.ToArray());
        }

        private static HashSet<string> ParseDisabledStrategies(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return new HashSet<string>(
                value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        private void SaveSettings_Click(object sender, EventArgs e)
        {
            SaveSettings();
        }

        private void BrowseNestTemplatePath_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Template File|*.nstdot";

            if (dlg.ShowDialog() == DialogResult.OK)
                textBox1.Text = dlg.FileName;
        }
    }
}
