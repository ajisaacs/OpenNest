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
        public OptionsForm()
        {
            InitializeComponent();
            BuildStrategyGrid();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LoadSettings();
        }

        private void BuildStrategyGrid()
        {
            strategyGrid.AutoGenerateColumns = false;

            strategyGrid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "Enabled",
                HeaderText = "",
                Width = 30,
            });

            strategyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "Strategy",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            });

            strategyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Phase",
                HeaderText = "Phase",
                ReadOnly = true,
                Width = 100,
            });

            strategyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Order",
                HeaderText = "Order",
                ReadOnly = true,
                Width = 55,
            });

            foreach (var strategy in FillStrategyRegistry.AllStrategies)
            {
                strategyGrid.Rows.Add(true, strategy.Name, strategy.Phase, strategy.Order);
            }
        }

        private void LoadSettings()
        {
            textBox1.Text = Settings.Default.NestTemplatePath;
            checkBox1.Checked = Settings.Default.CreateNewNestOnOpen;
            numericUpDown1.Value = (decimal)Settings.Default.AutoSizePlateFactor;

            var disabledNames = ParseDisabledStrategies(Settings.Default.DisabledStrategies);
            foreach (DataGridViewRow row in strategyGrid.Rows)
                row.Cells["Enabled"].Value = !disabledNames.Contains((string)row.Cells["Name"].Value);
        }

        private void SaveSettings()
        {
            Settings.Default.NestTemplatePath = textBox1.Text;
            Settings.Default.CreateNewNestOnOpen = checkBox1.Checked;
            Settings.Default.AutoSizePlateFactor = (double)numericUpDown1.Value;

            var disabledNames = new List<string>();
            foreach (DataGridViewRow row in strategyGrid.Rows)
            {
                if (row.Cells["Enabled"].Value is false)
                    disabledNames.Add((string)row.Cells["Name"].Value);
            }
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
