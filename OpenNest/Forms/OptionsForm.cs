using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Data.MaterialLedger;
using OpenNest.Desktop;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    public partial class OptionsForm : Form
    {
        public OptionsForm()
        {
            InitializeComponent();
            planningEffortBox.Minimum = (decimal)CuttingPlanBatch.MinimumBudgetMultiplier;
            planningEffortBox.Maximum = (decimal)CuttingPlanBatch.MaximumBudgetMultiplier;
            GridStyle.Apply(strategyGrid, sizeColumns: false);
            BuildStrategyGrid();
            GridStyle.Apply(serverGrid, sizeColumns: false);
            BuildServerGrid();
            navList.SelectedIndex = System.Math.Clamp(lastPageIndex, 0, pages.Length - 1);
        }

        // The page shown last, so reopening Options in the same session returns to it.
        private static int lastPageIndex;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LoadSettings();
        }

        private void NavList_SelectedIndexChanged(object sender, EventArgs e)
        {
            var index = navList.SelectedIndex;
            if (index < 0)
                return;
            lastPageIndex = index;
            for (var i = 0; i < pages.Length; i++)
                pages[i].Visible = i == index;
            navList.Invalidate();
        }

        private void NavList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            var selected = (e.State & DrawItemState.Selected) != 0;
            var bounds = e.Bounds;
            using (var back = new System.Drawing.SolidBrush(selected ? NavSelectedColor : NavBackColor))
                e.Graphics.FillRectangle(back, bounds);
            if (selected)
            {
                using var accent = new System.Drawing.SolidBrush(NavAccentColor);
                e.Graphics.FillRectangle(accent, bounds.Left, bounds.Top + 7, 3, bounds.Height - 14);
            }

            var textBounds = new System.Drawing.Rectangle(bounds.Left + 16, bounds.Top, bounds.Width - 20, bounds.Height);
            TextRenderer.DrawText(e.Graphics, (string)navList.Items[e.Index], navList.Font, textBounds,
                System.Drawing.Color.FromArgb(32, 32, 32),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0 && (e.State & DrawItemState.NoFocusRect) == 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, System.Drawing.Rectangle.Inflate(bounds, -2, -2));
        }

        private static readonly System.Drawing.Color NavSelectedColor = System.Drawing.Color.FromArgb(220, 234, 248);
        private static readonly System.Drawing.Color NavAccentColor = System.Drawing.Color.FromArgb(0, 103, 192);

        private void BuildStrategyGrid()
        {
            strategyGrid.AutoGenerateColumns = false;

            strategyGrid.Columns.Add(
                new DataGridViewCheckBoxColumn
                {
                    Name = "Enabled",
                    HeaderText = "",
                    Width = 32,
                }
            );

            strategyGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name = "Name",
                    HeaderText = "Strategy",
                    ReadOnly = true,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                    MinimumWidth = 120,
                }
            );

            strategyGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name = "Phase",
                    HeaderText = "Phase",
                    ReadOnly = true,
                    Width = 100,
                }
            );

            strategyGrid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    Name = "Order",
                    HeaderText = "Order",
                    ReadOnly = true,
                    Width = 55,
                }
            );

            foreach (var strategy in FillStrategyOptions.Rows)
            {
                strategyGrid.Rows.Add(true, strategy.Name, strategy.Phase, strategy.Order);
            }
        }

        private void LoadSettings()
        {
            var options = ReadOptions();
            LoadServers();
            updateFeedUrlBox.Text = Settings.Default.UpdateFeedUrl;
            updateChannelCombo.Items.Clear();
            foreach (var (_, text) in UpdateChannelOption.Choices)
                updateChannelCombo.Items.Add(text);
            updateChannelCombo.SelectedIndex = UpdateChannelOption.IndexOf(options.UpdateChannel);
            ledgerUrlBox.Text = MaterialLedgerStore.Settings.BaseUrl;
            ledgerKeyBox.Text = MaterialLedgerStore.Settings.ApiKey;
            createNestOnStartupCheck.Checked = options.CreateNewNestOnOpen;
            autoSizeFactorBox.Value = (decimal)options.AutoSizePlateFactorForEditing;
            planningEffortBox.Value = (decimal)options.CuttingPlanEffortForEditing;

            colorSchemeCombo.Items.Clear();
            var schemes = DesktopOptions.ColorSchemeNames;
            foreach (var name in schemes)
                colorSchemeCombo.Items.Add(name);
            colorSchemeCombo.SelectedIndex = options.ColorSchemeIndex(schemes);

            clonePlacementCombo.Items.Clear();
            foreach (var (_, text) in ClonePlacement.Choices)
                clonePlacementCombo.Items.Add(text);
            clonePlacementCombo.SelectedIndex = ClonePlacement.IndexOf(options.ClonePlacementOrder);

            var disabledNames = new HashSet<string>(options.DisabledStrategies, StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in strategyGrid.Rows)
                row.Cells["Enabled"].Value = !disabledNames.Contains(
                    (string)row.Cells["Name"].Value
                );
        }

        private void SaveSettings()
        {
            var disabledNames = new List<string>();
            foreach (DataGridViewRow row in strategyGrid.Rows)
            {
                if (row.Cells["Enabled"].Value is false)
                    disabledNames.Add((string)row.Cells["Name"].Value);
            }

            var options = new DesktopOptions
            {
                CreateNewNestOnOpen = createNestOnStartupCheck.Checked,
                UpdateChannel = UpdateChannelOption.At(updateChannelCombo.SelectedIndex),
                AutoSizePlateFactor = (double)autoSizeFactorBox.Value,
                CuttingPlanEffort = (double)planningEffortBox.Value,
                ColorScheme = colorSchemeCombo.SelectedItem as string ?? ColorSchemeRegistry.DefaultSchemeName,
                ClonePlacementOrder = ClonePlacement.At(clonePlacementCombo.SelectedIndex),
                DisabledStrategies = disabledNames,
            };
            WriteOptions(options);
            Settings.Default.Save();
            options.Apply();

            foreach (Form f in Application.OpenForms)
                f.Invalidate(invalidateChildren: true);
        }

        // The app settings in the shared Options model; they persist to user-settings.json under
        // the same names through AppDataSettingsProvider.
        private static DesktopOptions ReadOptions() => new()
        {
            CreateNewNestOnOpen = Settings.Default.CreateNewNestOnOpen,
            UpdateChannel = UpdateChannelOption.Read(
                Settings.Default.UpdateIncludeDevBuilds, Settings.Default.UpdateDevBuildInterval),
            AutoSizePlateFactor = Settings.Default.AutoSizePlateFactor,
            CuttingPlanEffort = Settings.Default.CuttingPlanEffort,
            ColorScheme = Settings.Default.ActiveColorScheme,
            ClonePlacementOrder = ClonePlacement.Parse(Settings.Default.ClonePlacementOrder),
            DisabledStrategies = FillStrategyOptions.ParseDisabled(Settings.Default.DisabledStrategies),
        };

        private static void WriteOptions(DesktopOptions options)
        {
            Settings.Default.CreateNewNestOnOpen = options.CreateNewNestOnOpen;
            Settings.Default.UpdateIncludeDevBuilds = options.UpdateIncludeDevBuilds;
            if (UpdateChannelOption.IntervalText(options.UpdateChannel) is { } interval)
                Settings.Default.UpdateDevBuildInterval = interval;
            Settings.Default.AutoSizePlateFactor = options.AutoSizePlateFactor;
            Settings.Default.CuttingPlanEffort = options.CuttingPlanEffort;
            Settings.Default.ActiveColorScheme = options.ColorScheme;
            Settings.Default.ClonePlacementOrder = options.ClonePlacementOrder.ToString();
            Settings.Default.DisabledStrategies = FillStrategyOptions.FormatDisabled(options.DisabledStrategies);
        }

        /// <summary>
        /// Applies the DisabledStrategies setting to the FillStrategyRegistry.
        /// Called on save and at startup from MainForm.
        /// </summary>
        public static void ApplyDisabledStrategies() =>
            FillStrategyOptions.Apply(FillStrategyOptions.ParseDisabled(Settings.Default.DisabledStrategies));

        private void SaveSettings_Click(object sender, EventArgs e)
        {
            // The server list is checked first so a bad row saves nothing and keeps the dialog open.
            if (!SaveServers())
            {
                DialogResult = DialogResult.None;
                return;
            }

            SaveSettings();
        }

        private void ShowNotice(OptionNotice notice) =>
            MessageBox.Show(this, notice.Text, notice.Title, MessageBoxButtons.OK, notice.Kind switch
            {
                OptionNoticeKind.Warning => MessageBoxIcon.Warning,
                OptionNoticeKind.Error => MessageBoxIcon.Error,
                _ => MessageBoxIcon.Information,
            });

        private void SaveLedger_Click(object sender, EventArgs e)
        {
            if (!MaterialLedgerSettings.TryNormalizeUrl(ledgerUrlBox.Text, out var url))
            {
                MessageBox.Show(this, "Enter a valid http or https MaterialLedger URL, or clear it to turn the ledger off.",
                    "MaterialLedger", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                MaterialLedgerStore.Save(url, ledgerKeyBox.Text.Trim());
                ledgerUrlBox.Text = MaterialLedgerStore.Settings.BaseUrl;
                MessageBox.Show(this, url.Length == 0
                        ? "MaterialLedger cleared. Auto Nest will not offer ledger stock."
                        : "MaterialLedger settings saved.",
                    "MaterialLedger", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not save the MaterialLedger settings: {ex.Message}",
                    "MaterialLedger", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void TestLedger_Click(object sender, EventArgs e)
        {
            if (!MaterialLedgerSettings.TryNormalizeUrl(ledgerUrlBox.Text, out var url) || url.Length == 0)
            {
                MessageBox.Show(this, "Enter a valid http or https MaterialLedger URL.",
                    "MaterialLedger", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            testLedgerButton.Enabled = false;
            try
            {
                var client = MaterialLedgerStore.CreateClient(
                    new MaterialLedgerSettings { BaseUrl = url, ApiKey = ledgerKeyBox.Text.Trim() });
                // No mapping names this material, so a working ledger answers mapped:false.
                var result = await client.GetStockAsync("OpenNest connection test", "", 0.25, CancellationToken.None);
                if (IsDisposed)
                    return;
                var (text, icon) = DescribeLedgerTest(result);
                MessageBox.Show(this, text, "MaterialLedger", MessageBoxButtons.OK, icon);
            }
            finally
            {
                if (!IsDisposed)
                    testLedgerButton.Enabled = true;
            }
        }

        internal static (string Text, MessageBoxIcon Icon) DescribeLedgerTest(LedgerStockResult result) => result switch
        {
            LedgerStockResult.Ok or LedgerStockResult.Unmapped =>
                ("Connected to MaterialLedger. The API key was accepted.", MessageBoxIcon.Information),
            LedgerStockResult.Unauthorized =>
                ("MaterialLedger answered but rejected the API key. Check Api:Keys:OpenNest.", MessageBoxIcon.Warning),
            LedgerStockResult.Unreachable unreachable =>
                ($"Could not reach MaterialLedger: {unreachable.Reason}", MessageBoxIcon.Warning),
            _ => ("Enter a MaterialLedger URL.", MessageBoxIcon.Warning),
        };

        private void SetUpdateFeed_Click(object sender, EventArgs e)
        {
            var (notice, url) = UpdateFeedOption.Save(updateFeedUrlBox.Text, next =>
            {
                Settings.Default.UpdateFeedUrl = next;
                Settings.Default.Save();
            });
            if (url != null)
                updateFeedUrlBox.Text = url;
            ShowNotice(notice);
        }
    }
}
