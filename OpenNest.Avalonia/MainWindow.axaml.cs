using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpenNest.Desktop;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;
using OpenNest.IO;

namespace OpenNest.Avalonia
{
    public partial class MainWindow : Window
    {
        private static readonly IBrush Accent = Brush.Parse("#3368A0");
        private static readonly IBrush Muted = Brush.Parse("#6B7280");
        private static readonly IBrush InactiveTab = Brush.Parse("#DDE1E5");

        private readonly NestTabs tabs = new();
        private readonly Dictionary<NestTab, NestView> views = new();
        private string notice;
        private CancellationTokenSource nesting;
        private NestTab nestingTab;
        private Task autoNest = Task.CompletedTask;
        private bool closeConfirmed;

        public MainWindow()
        {
            InitializeComponent();
            // As the desktop app: plug-in engines first, then the saved engine selection.
            NestingEngineRegistry.LoadPlugins(Path.Combine(AppContext.BaseDirectory, "Engines"));
            var engineWarning = EngineSelection.LoadSavedSelection();
            if (!string.IsNullOrEmpty(engineWarning))
                notice = engineWarning;
            tabs.Changed += (_, _) => ShowActiveTab();
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.O, KeyModifiers.Control),
                Command = new Command(() => OnOpen(null, null)),
            });
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.PageUp), Command = new Command(() => StepPlate(-1)) });
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.PageDown), Command = new Command(() => StepPlate(1)) });
            // Ctrl+Tab before focus navigation takes the Tab key.
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
            Closing += OnWindowClosing;
            ShowActiveTab();
        }

        // Null while a new tab's view is still being created.
        private NestView ActiveView =>
            tabs.Active != null && views.TryGetValue(tabs.Active, out var view) ? view : null;

        /// <summary>Opens the nest in a new tab, or switches to its tab if open.</summary>
        public void OpenNest(string path)
        {
            try
            {
                var tab = tabs.Open(
                    path, file => new Document { Nest = new NestReader(file).Read() });
                if (!views.ContainsKey(tab))
                {
                    var view = new NestView(tab);
                    view.ViewChanged += (_, _) => { if (view == ActiveView) UpdateStatus(); };
                    views[tab] = view;
                    notice = null;
                    ShowActiveTab();
                }
            }
            catch (Exception ex)
            {
                ShowNotice($"Could not open {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores the window's size, position and maximized state from the user settings the
        /// WinForms app shares, and saves them on close. Not used for --snapshot, whose image
        /// must not depend on the user's settings.
        /// </summary>
        public void RememberPlacement() =>
            WindowPlacementBinding.Remember(
                this, WindowPlacement.MainWindow, Data.UserSettingsFile.DefaultPath);

        /// <summary>Runs Auto Nest once the window opens, for --auto-nest.</summary>
        public void AutoNestOnOpen() => Opened += (_, _) => autoNest = RunAutoNestAsync();

        public void SaveSnapshotAndClose(string path, int plateNumber)
        {
            Opened += async (_, _) =>
            {
                ActiveView?.ShowPlate(plateNumber - 1);
                await autoNest;
                await Task.Delay(500);
                var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
                using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(size);
                bitmap.Render(this);
                bitmap.Save(path);
                // The status line beside the image, so headless checks can read it.
                File.WriteAllText(path + ".txt", Status.Text);
                closeConfirmed = true;
                Close();
            };
        }

        private void ShowActiveTab()
        {
            var view = ActiveView;
            NestHost.Content = view;
            EmptyHint.IsVisible = view == null;
            CloseMenuItem.IsEnabled = view != null;
            CloseAllMenuItem.IsEnabled = view != null;
            Title = view == null
                ? "OpenNest (Avalonia prototype)"
                : $"{view.Tab.Name} - OpenNest (Avalonia prototype)";
            RebuildTabStrip();
            UpdateStatus();
        }

        private void RebuildTabStrip()
        {
            TabStrip.Children.Clear();
            for (var i = 0; i < tabs.Tabs.Count; i++)
                TabStrip.Children.Add(TabButton(tabs.Tabs[i], i == tabs.ActiveIndex));

            var add = new Button
            {
                Content = "+",
                FontSize = 15,
                Padding = new Thickness(10, 2),
                Margin = new Thickness(4, 4, 0, 0),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Top,
            };
            ToolTip.SetTip(add, "Open a nest");
            add.Click += OnOpen;
            TabStrip.Children.Add(add);
        }

        private Control TabButton(NestTab tab, bool active)
        {
            var name = new TextBlock
            {
                Text = tab.Modified ? tab.Name + "  \u2022" : tab.Name,
                FontSize = 13,
                FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = active ? Brushes.Black : Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var close = new Button
            {
                Content = "\u2715",
                FontSize = 10,
                Padding = new Thickness(5, 1),
                Background = Brushes.Transparent,
                Foreground = Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(close, "Close");
            close.Click += async (_, e) =>
            {
                e.Handled = true;
                await CloseTabAsync(tab);
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(name);
            row.Children.Add(close);
            var border = new Border
            {
                Child = row,
                Padding = new Thickness(14, 5, 6, 5),
                Margin = new Thickness(0, 0, 2, 0),
                CornerRadius = new CornerRadius(0, 0, 6, 6),
                Background = active ? Brushes.White : InactiveTab,
                BorderBrush = active ? Accent : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(border, tab.Path);
            border.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
                    tabs.Activate(tabs.Tabs.ToList().IndexOf(tab));
            };
            return border;
        }

        private async Task<bool> CloseTabAsync(NestTab tab)
        {
            if (tab == nestingTab)
            {
                ShowNotice($"Auto Nest is running on {tab.Name}. Stop it before closing the nest.");
                return false;
            }

            if (tab.Modified && !await ConfirmDialog.AskAsync(this, "Close Nest",
                    $"{tab.Name} has Auto Nest results that are not saved. This version of "
                    + "OpenNest cannot save nests yet, so closing it discards them.",
                    "Discard and Close"))
                return false;

            tabs.Close(tab);
            views.Remove(tab);
            return true;
        }

        private async void OnCloseNest(object sender, RoutedEventArgs e)
        {
            if (tabs.Active != null)
                await CloseTabAsync(tabs.Active);
        }

        private async void OnCloseAll(object sender, RoutedEventArgs e)
        {
            foreach (var tab in tabs.Tabs.Reverse().ToList())
            {
                if (!await CloseTabAsync(tab))
                    return;
            }
        }

        private async void OnWindowClosing(object sender, WindowClosingEventArgs e)
        {
            if (closeConfirmed || !tabs.AnyModified)
            {
                nesting?.Cancel();
                return;
            }

            e.Cancel = true;
            var count = tabs.Tabs.Count(t => t.Modified);
            var what = count == 1 ? "One open nest has" : $"{count} open nests have";
            if (await ConfirmDialog.AskAsync(this, "Exit OpenNest",
                    $"{what} Auto Nest results that are not saved. This version of OpenNest "
                    + "cannot save nests yet, so exiting discards them.",
                    "Discard and Exit"))
            {
                closeConfirmed = true;
                Close();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                StepNest(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
            }
        }

        private void OnNextNest(object sender, RoutedEventArgs e) => StepNest(1);

        private void OnPreviousNest(object sender, RoutedEventArgs e) => StepNest(-1);

        private void StepNest(int step)
        {
            var count = tabs.Tabs.Count;
            if (count > 1)
                tabs.Activate(((tabs.ActiveIndex + step) % count + count) % count);
        }

        private async void OnOpen(object sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Nest",
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType("Nest files") { Patterns = new[] { "*.nest" } } },
            });
            foreach (var path in files.Select(f => f.TryGetLocalPath()).Where(p => p != null))
                OpenNest(path);
        }

        private void OnExit(object sender, RoutedEventArgs e) => Close();

        private void OnAutoNest(object sender, RoutedEventArgs e)
        {
            // Stop always cancels and discards the whole proposal.
            if (nesting != null)
            {
                nesting.Cancel();
                return;
            }
            autoNest = RunAutoNestAsync();
        }

        /// <summary>
        /// Nests every drawing's remaining quantity of the active nest on sheets like its shown
        /// plate, through the shared pipeline off the UI thread. Other nests can be opened and
        /// viewed meanwhile; the nest being nested cannot be closed until it finishes.
        /// </summary>
        private async Task RunAutoNestAsync()
        {
            var view = ActiveView;
            if (view == null || nesting != null)
                return;
            var tab = view.Tab;
            var nest = tab.Document.Nest;
            var engineName = EngineSelection.EngineName;
            var template = view.PlateView.Plate
                ?? nest.Plates.FirstOrDefault()
                ?? nest.PlateDefaults.CreateNew();
            var request = AutoNestJob.ForRemaining(nest, template, engineName);
            if (request == null)
            {
                ShowNotice("Auto Nest: nothing has a remaining quantity to nest.");
                return;
            }

            nesting = new CancellationTokenSource();
            nestingTab = tab;
            var token = nesting.Token;
            SetNesting(true);
            ShowNotice($"{engineName}: starting...");
            var progress = new Progress<NestProgress>(p =>
            {
                if (!token.IsCancellationRequested)
                    ShowNotice(p.Description ?? $"{engineName}: plate {p.PlateNumber}");
            });
            try
            {
                var jobProgress = JobEngineNest.CreateProgress(engineName, progress);
                var result = await Task.Run(() => NestPipeline.Run(request, jobProgress, token));
                // Engines that ignore cancellation still have their proposal discarded.
                token.ThrowIfCancellationRequested();
                var firstNew = nest.Plates.Count(p => p.Parts.Count > 0);
                var kept = AutoNestJob.Keep(nest, result);
                if (kept.Nested)
                {
                    tab.MarkModified();
                    view.Refresh(firstNew);
                    RebuildTabStrip();
                }
                ShowNotice(kept.Nested
                    ? $"{tab.Name}: {engineName}: {kept.Note}"
                    : AutoNestJob.RefusalReason(result));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                ShowNotice("Auto Nest stopped; nothing was changed.");
            }
            catch (Exception ex)
            {
                ShowNotice($"Auto Nest failed: {ex.Message}");
            }
            finally
            {
                nesting.Dispose();
                nesting = null;
                nestingTab = null;
                SetNesting(false);
            }
        }

        private void SetNesting(bool running)
        {
            AutoNestButton.Content = running ? "Stop" : "Auto Nest";
            AutoNestMenuItem.Header = running ? "_Stop Auto Nest" : "_Auto Nest";
        }

        private void ShowNotice(string text)
        {
            notice = text;
            UpdateStatus();
        }

        private void OnFit(object sender, RoutedEventArgs e) => ActiveView?.PlateView.ZoomToFit();

        private void OnPreviousPlate(object sender, RoutedEventArgs e) => StepPlate(-1);

        private void OnNextPlate(object sender, RoutedEventArgs e) => StepPlate(1);

        private void StepPlate(int step) => ActiveView?.StepPlate(step);

        private void UpdateStatus()
        {
            var view = ActiveView;
            var canvas = view?.PlateView;
            if (canvas?.Plate == null)
            {
                var empty = view == null
                    ? "Open a .nest file to begin."
                    : $"{view.Tab.Name}   No plates";
                Status.Text = notice ?? empty;
                return;
            }

            var text = $"{view.Tab.Name}   Plate {view.PlateIndex + 1} of {view.PlateCount}"
                + $"   Zoom {canvas.Zoom:0.##} px/unit";
            if (canvas.CursorWorld is { } at)
                text += $"   X {at.X:0.000}  Y {at.Y:0.000}";
            var part = canvas.HoveredPart ?? canvas.SelectedPart;
            if (part != null)
            {
                var how = ReferenceEquals(part, canvas.SelectedPart) ? "Selected" : "Over";
                text += $"   {how}: {part.BaseDrawing.Name}";
            }
            if (notice != null)
                text += $"   |   {notice}";
            Status.Text = text;
        }

        private sealed class Command : System.Windows.Input.ICommand
        {
            private readonly Action action;

            public Command(Action action) => this.action = action;

            public event EventHandler CanExecuteChanged { add { } remove { } }

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter) => action();
        }
    }
}
