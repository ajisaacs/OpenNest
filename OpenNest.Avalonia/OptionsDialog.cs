using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenNest.Data;
using OpenNest.Desktop;

namespace OpenNest.Avalonia
{
    /// <summary>
    /// Tools &gt; Options, as the WinForms Options form: Save writes the general options and the
    /// fill strategies to the user settings both apps share and applies them; the nest server URL
    /// (storage.json) and the update feed URL have their own buttons that save at once. The
    /// MaterialLedger connection is not here: its API key is kept with Windows data protection,
    /// so it is set in the WinForms app.
    /// </summary>
    internal sealed class OptionsDialog : Window
    {
        private static readonly IBrush Muted = Brush.Parse("#6B7280");

        private readonly string settingsPath;
        private readonly CheckBox createNewNest = new() { Content = "Create a new nest on startup" };
        private readonly NumericUpDown autoSizeFactor = new()
        {
            Minimum = (decimal)DesktopOptions.AutoSizePlateFactorMinimum,
            Maximum = (decimal)DesktopOptions.AutoSizePlateFactorMaximum,
            Increment = (decimal)DesktopOptions.AutoSizePlateFactorIncrement,
            FormatString = "0.####",
            Width = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        private readonly ComboBox colorScheme = new() { Width = 200 };
        private readonly NumericUpDown planningEffort = new()
        {
            Minimum = (decimal)DesktopOptions.CuttingPlanEffortMinimum,
            Maximum = (decimal)DesktopOptions.CuttingPlanEffortMaximum,
            Increment = (decimal)DesktopOptions.CuttingPlanEffortIncrement,
            FormatString = "0.0",
            Width = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        private readonly ComboBox clonePlacement = new() { Width = 200 };
        private readonly TextBox serverUrl = new() { Watermark = "http://server:port" };
        private readonly TextBox updateFeedUrl = new();
        private readonly CheckBox includeDevBuilds = new()
        {
            Content = "Include development builds (latest master; takes effect after restart)",
        };
        private readonly List<(CheckBox Enabled, string Name)> strategies = new();

        private OptionsDialog(string settingsPath)
        {
            this.settingsPath = settingsPath;
            Title = "Options";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var general = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
                ColumnSpacing = 12,
                RowSpacing = 8,
            };
            AddRow(general, 0, "Auto-size plate factor:", autoSizeFactor,
                "The amount to round the plate size up.");
            AddRow(general, 1, "Color scheme:", colorScheme, null);
            AddRow(general, 2, "Cut planning effort:", planningEffort,
                "How long Plan Cutting searches for a route; 1 is normal.");
            AddRow(general, 3, "Clone push placement:", clonePlacement,
                "Which way Shift+click and Ctrl+click copies are pushed.");
            Grid.SetRow(createNewNest, 4);
            Grid.SetColumnSpan(createNewNest, 2);
            general.Children.Add(createNewNest);

            var setServer = new Button { Content = "Set / Change Server" };
            setServer.Click += async (_, _) => await SetServerAsync();
            var server = Group(
                "Nest server URL (File / Database mode is selected under File > Storage Mode)",
                UrlRow(serverUrl, setServer));

            var saveFeed = new Button { Content = "Save Feed URL" };
            saveFeed.Click += async (_, _) => await SaveFeedAsync();
            var updates = Group(
                "Auto-update feed URL (blank: nest server mirror in Database mode, else GitHub Releases)",
                UrlRow(updateFeedUrl, saveFeed), includeDevBuilds);

            var ledger = new TextBlock
            {
                Text = "MaterialLedger is set in the Windows app: its API key is saved with Windows data protection.",
                Foreground = Muted,
                TextWrapping = TextWrapping.Wrap,
            };

            var save = new Button { Content = "Save", IsDefault = true, MinWidth = 80 };
            save.Click += async (_, _) => await SaveAsync();
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            cancel.Click += (_, _) => Close(false);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { save, cancel },
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(18, 18, 18, 8),
                Spacing = 14,
                Children =
                {
                    general,
                    server,
                    ledger,
                    updates,
                    Group("Fill Strategies", StrategyGrid()),
                },
            };
            // Save and Cancel stay in view when a small screen makes the options scroll.
            buttons.Margin = new Thickness(18, 8, 18, 18);
            DockPanel.SetDock(buttons, Dock.Bottom);
            Content = new DockPanel
            {
                Children = { buttons, new ScrollViewer { Content = panel } },
            };
            Load();
        }

        /// <summary>
        /// Shows the dialog over <paramref name="owner"/>; true when the options were saved.
        /// <paramref name="settingsPath"/> is the shared user-settings.json.
        /// </summary>
        public static async Task<bool> ShowAsync(Window owner, string settingsPath) =>
            await new OptionsDialog(settingsPath).ShowDialog<bool?>(owner) == true;

        private void Load()
        {
            var settings = UserSettingsFile.Read(settingsPath);
            var options = DesktopOptions.Read(settings);
            serverUrl.Text = DesktopSettings.ServerUrl;
            updateFeedUrl.Text = UpdateFeedOption.Read(settings);
            includeDevBuilds.IsChecked = options.UpdateIncludeDevBuilds;
            createNewNest.IsChecked = options.CreateNewNestOnOpen;
            autoSizeFactor.Value = (decimal)options.AutoSizePlateFactorForEditing;
            planningEffort.Value = (decimal)options.CuttingPlanEffortForEditing;

            var schemes = DesktopOptions.ColorSchemeNames;
            colorScheme.ItemsSource = schemes;
            colorScheme.SelectedIndex = options.ColorSchemeIndex(schemes);

            clonePlacement.ItemsSource = ClonePlacement.Choices.Select(c => c.Text).ToList();
            clonePlacement.SelectedIndex = ClonePlacement.IndexOf(options.ClonePlacementOrder);

            var disabled = new HashSet<string>(options.DisabledStrategies, StringComparer.OrdinalIgnoreCase);
            foreach (var (enabled, name) in strategies)
                enabled.IsChecked = !disabled.Contains(name);
        }

        private DesktopOptions FromControls() => new()
        {
            CreateNewNestOnOpen = createNewNest.IsChecked == true,
            UpdateIncludeDevBuilds = includeDevBuilds.IsChecked == true,
            // A cleared box keeps the value it had, as a WinForms numeric box does.
            AutoSizePlateFactor = (double)(autoSizeFactor.Value ?? (decimal)DesktopOptions.Defaults.AutoSizePlateFactor),
            CuttingPlanEffort = (double)(planningEffort.Value ?? (decimal)DesktopOptions.Defaults.CuttingPlanEffort),
            ColorScheme = colorScheme.SelectedItem as string ?? ColorSchemeRegistry.DefaultSchemeName,
            ClonePlacementOrder = ClonePlacement.At(clonePlacement.SelectedIndex),
            DisabledStrategies = strategies.Where(s => s.Enabled.IsChecked != true).Select(s => s.Name).ToList(),
        };

        private async Task SaveAsync()
        {
            var options = FromControls();
            if (!UserSettingsFile.Update(settingsPath, options.WriteTo))
            {
                await ConfirmDialog.ShowAsync(this, "Options",
                    $"Could not save the options to {settingsPath}. Nothing was changed.");
                return;
            }
            options.Apply();
            Close(true);
        }

        private async Task SetServerAsync()
        {
            var (notice, url) = NestServerUrlOption.Save(serverUrl.Text, DesktopSettings.SaveServerUrl);
            if (url != null)
                serverUrl.Text = DesktopSettings.ServerUrl;
            await ConfirmDialog.ShowAsync(this, notice.Title, notice.Text);
        }

        private async Task SaveFeedAsync()
        {
            var (notice, url) = UpdateFeedOption.Save(updateFeedUrl.Text, next =>
            {
                if (!UserSettingsFile.Update(settingsPath, values => values[UpdateFeedOption.SettingName] = next))
                    throw new System.IO.IOException($"{settingsPath} could not be written.");
            });
            if (url != null)
                updateFeedUrl.Text = url;
            await ConfirmDialog.ShowAsync(this, notice.Title, notice.Text);
        }

        private Grid StrategyGrid()
        {
            var rows = FillStrategyOptions.Rows;
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                ColumnSpacing = 16,
                RowSpacing = 4,
            };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var header = 1;
            foreach (var (text, column) in new[] { ("Strategy", 1), ("Phase", 2), ("Order", 3) })
            {
                var cell = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold };
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var enabled = new CheckBox { IsChecked = true, MinWidth = 0, MinHeight = 0 };
                strategies.Add((enabled, row.Name));
                var cells = new Control[]
                {
                    enabled,
                    Cell(row.Name),
                    Cell(row.Phase.ToString()),
                    Cell(row.Order.ToString()),
                };
                for (var column = 0; column < cells.Length; column++)
                {
                    Grid.SetRow(cells[column], i + header);
                    Grid.SetColumn(cells[column], column);
                    grid.Children.Add(cells[column]);
                }
            }
            return grid;
        }

        private static TextBlock Cell(string text) =>
            new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

        private static void AddRow(Grid grid, int row, string label, Control editor, string tip)
        {
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, row);
            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, 1);
            if (tip != null)
                ToolTip.SetTip(editor, tip);
            grid.Children.Add(text);
            grid.Children.Add(editor);
        }

        private static Grid UrlRow(TextBox box, Button button)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            Grid.SetColumn(button, 1);
            button.MinWidth = 150;
            row.Children.Add(box);
            row.Children.Add(button);
            return row;
        }

        private static StackPanel Group(string header, params Control[] children)
        {
            var group = new StackPanel { Spacing = 8 };
            group.Children.Add(new TextBlock
            {
                Text = header,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (var child in children)
                group.Children.Add(child);
            return group;
        }
    }
}
