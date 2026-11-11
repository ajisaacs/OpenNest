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
        private bool snapshotMode;
        // A save or export waiting on its file picker or write; another one waits for it.
        private bool writingFile;
        // True while Exit asks about unsaved nests, so a second close request is ignored.
        private bool askingToExit;
        // Load warnings shown one dialog at a time, once the window is open.
        private readonly Queue<(string Name, IReadOnlyList<string> Warnings)> pendingWarnings =
            new();
        private bool showingWarnings;

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
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.S, KeyModifiers.Control),
                Command = new Command(() => OnSave(null, null)),
            });
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.PageUp), Command = new Command(() => StepPlate(-1)) });
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.PageDown), Command = new Command(() => StepPlate(1)) });
            // Ctrl+Tab before focus navigation takes the Tab key.
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
            Closing += OnWindowClosing;
            Opened += (_, _) =>
            {
                if (!snapshotMode)
                    _ = ShowLoadWarningsAsync();
            };
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
                IReadOnlyList<string> warnings = Array.Empty<string>();
                var tab = tabs.Open(path, file =>
                {
                    var reader = new NestReader(file);
                    var document = new Document { Nest = reader.Read() };
                    warnings = reader.Warnings;
                    return document;
                });
                if (!views.ContainsKey(tab))
                {
                    var view = new NestView(tab);
                    view.ViewChanged += (_, _) => { if (view == ActiveView) UpdateStatus(); };
                    views[tab] = view;
                    notice = null;
                    ShowActiveTab();
                }
                if (warnings.Count > 0)
                {
                    var count = warnings.Count == 1
                        ? "1 load warning"
                        : $"{warnings.Count} load warnings";
                    ShowNotice($"{tab.Name}: {count}");
                    pendingWarnings.Enqueue((tab.Name, warnings));
                    if (IsVisible && !snapshotMode)
                        _ = ShowLoadWarningsAsync();
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
            // No dialogs: the image is taken and the window closes unattended.
            snapshotMode = true;
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
            SaveMenuItem.IsEnabled = view != null;
            SaveAsMenuItem.IsEnabled = view != null;
            ExportNestMenuItem.IsEnabled = view != null;
            SaveButton.IsEnabled = view != null;
            SaveAsButton.IsEnabled = view != null;
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
            if (writingFile)
            {
                ShowNotice("A nest is being saved or exported. Close the nest once that finishes.");
                return false;
            }

            if (!await ConfirmUnsavedAsync(tab))
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
            if (closeConfirmed || (!tabs.AnyModified && !writingFile))
            {
                nesting?.Cancel();
                return;
            }

            e.Cancel = true;
            if (askingToExit)
                return;
            if (writingFile)
            {
                ShowNotice("A nest is being saved or exported. Exit again once that finishes.");
                return;
            }

            // As closing each WinForms nest window: ask about every unsaved nest in turn,
            // showing it; Cancel, or a save that does not happen, keeps the app open. The list
            // is read again after each answer: Auto Nest can finish while a question is open.
            askingToExit = true;
            try
            {
                var answered = new HashSet<NestTab>();
                while (tabs.Tabs.FirstOrDefault(t => t.Modified && !answered.Contains(t))
                    is { } tab)
                {
                    tabs.Activate(tabs.Tabs.ToList().IndexOf(tab));
                    if (tab == nestingTab)
                    {
                        await ConfirmDialog.ShowAsync(this, "Exit OpenNest",
                            $"Auto Nest is running on {tab.Name}, which has unsaved changes. "
                            + "Stop Auto Nest (Nest > Stop Auto Nest) or let it finish, then "
                            + "exit again.");
                        return;
                    }
                    if (!await ConfirmUnsavedAsync(tab))
                        return;
                    answered.Add(tab);
                }
            }
            finally
            {
                askingToExit = false;
            }
            closeConfirmed = true;
            Close();
        }

        /// <summary>
        /// For a nest with unsaved changes, asks Save / Don't Save / Cancel. True when it may
        /// close: saved, or the user chose not to save.
        /// </summary>
        private async Task<bool> ConfirmUnsavedAsync(NestTab tab)
        {
            if (!tab.Modified)
                return true;
            var choice = await ConfirmDialog.ChooseAsync(this, "Unsaved Changes",
                $"Save changes to '{tab.Name}' before closing?", "Save", "Don't Save");
            return choice switch
            {
                0 => await SaveAsync(tab, askForFile: false),
                1 => true,
                _ => false,
            };
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            if (tabs.Active != null)
                await SaveAsync(tabs.Active, askForFile: false);
        }

        private async void OnSaveAs(object sender, RoutedEventArgs e)
        {
            if (tabs.Active != null)
                await SaveAsync(tabs.Active, askForFile: true);
        }

        /// <summary>
        /// Saves the nest to the file it was saved to this session, else (or for Save As) to a
        /// file the user picks. True when it was saved.
        /// </summary>
        private async Task<bool> SaveAsync(NestTab tab, bool askForFile)
        {
            if (!CanWrite(tab, "saving"))
                return false;
            writingFile = true;
            try
            {
                var path = askForFile ? null : tab.SavePath;
                path ??= await PickNestFileAsync(tab, "Save Nest As");
                // Auto Nest may have started on it while the file picker was open.
                if (path == null || IsNesting(tab, "saving"))
                    return false;
                try
                {
                    tabs.SaveAs(tab, path);
                }
                catch (SaveTargetRefusedException ex)
                {
                    await ConfirmDialog.ShowAsync(
                        this, "Save Nest", $"The nest was not saved. {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    await ConfirmDialog.ShowAsync(this, "Save Nest",
                        "The nest was not saved. Any copy already at that path was left as it "
                        + $"was.\n\n{path}\n\n{ex.Message}\n\nChoose File > Save As to save "
                        + "it to another folder or name.");
                    return false;
                }
                ShowNotice($"Saved {tab.Name} to {path}");
                return true;
            }
            finally
            {
                writingFile = false;
            }
        }

        private async void OnExportNest(object sender, RoutedEventArgs e)
        {
            var tab = tabs.Active;
            if (tab == null || !CanWrite(tab, "exporting"))
                return;
            writingFile = true;
            try
            {
                var path = await PickNestFileAsync(tab, "Export .nest");
                if (path == null || IsNesting(tab, "exporting"))
                    return;
                try
                {
                    // Export never changes the nest's file, name or unsaved mark.
                    tabs.ExportCopy(tab, path);
                    ShowNotice($"Exported .nest to {path}");
                }
                catch (SaveTargetRefusedException ex)
                {
                    await ConfirmDialog.ShowAsync(
                        this, "Export .nest", $"The nest was not exported. {ex.Message}");
                }
                catch (Exception ex)
                {
                    await ConfirmDialog.ShowAsync(this, "Export .nest",
                        $"The nest was not exported.\n\n{path}\n\n{ex.Message}\n\nChoose "
                        + "File > Export .nest again to pick another folder or name.");
                }
            }
            finally
            {
                writingFile = false;
            }
        }

        // Whether the tab's nest can be written now: not while Auto Nest works on it, and one
        // save or export at a time.
        private bool CanWrite(NestTab tab, string doing)
        {
            if (IsNesting(tab, doing))
                return false;
            if (writingFile)
            {
                ShowNotice("A nest is already being saved or exported. "
                    + $"Finish that before {doing}.");
                return false;
            }
            return true;
        }

        private bool IsNesting(NestTab tab, string doing)
        {
            if (tab != nestingTab)
                return false;
            ShowNotice($"Auto Nest is running on {tab.Name}. "
                + $"Stop it or let it finish before {doing}.");
            return true;
        }

        // A .nest file to write, named after the nest, starting in the folder of its file.
        private async Task<string> PickNestFileAsync(NestTab tab, string title)
        {
            var folder = tab.Path != null ? Path.GetDirectoryName(tab.Path) : null;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = tab.Name,
                DefaultExtension = "nest",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { NestFiles },
                SuggestedStartLocation = folder != null
                    ? await StorageProvider.TryGetFolderFromPathAsync(folder)
                    : null,
            });
            return file?.TryGetLocalPath();
        }

        private static readonly FilePickerFileType NestFiles =
            new("Nest files") { Patterns = new[] { "*.nest" } };

        // Shows the queued load warnings one dialog at a time, as WinForms does after each open.
        private async Task ShowLoadWarningsAsync()
        {
            if (showingWarnings)
                return;
            showingWarnings = true;
            try
            {
                while (pendingWarnings.TryDequeue(out var item))
                    await ConfirmDialog.ShowAsync(this, "Nest Load Warnings",
                        $"{item.Name}:\n\n" + string.Join(Environment.NewLine, item.Warnings));
            }
            finally
            {
                showingWarnings = false;
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
                FileTypeFilter = new[] { NestFiles },
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
