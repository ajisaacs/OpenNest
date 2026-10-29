using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenNest.Desktop;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;
using OpenNest.IO;

namespace OpenNest.Avalonia
{
    public sealed record DrawingRow(string Name, int Count, string Size);

    public partial class MainWindow : Window
    {
        private Document document;
        private string fileName;
        private string notice;
        private CancellationTokenSource nesting;
        private Task autoNest = Task.CompletedTask;

        private Nest nest => document?.Nest;

        public MainWindow()
        {
            InitializeComponent();
            // As the desktop app: plug-in engines first, then the saved engine selection.
            NestingEngineRegistry.LoadPlugins(Path.Combine(AppContext.BaseDirectory, "Engines"));
            var engineWarning = EngineSelection.LoadSavedSelection();
            if (!string.IsNullOrEmpty(engineWarning))
                notice = engineWarning;
            Canvas.ViewChanged += (_, _) => UpdateStatus();
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.O, KeyModifiers.Control),
                Command = new Command(() => OnOpen(null, null)),
            });
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.PageUp), Command = new Command(() => StepPlate(-1)) });
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.PageDown), Command = new Command(() => StepPlate(1)) });
        }

        public void OpenNest(string path)
        {
            if (nesting != null)
                return;
            try
            {
                document = new Document { Nest = new NestReader(path).Read() };
            }
            catch (Exception ex)
            {
                Status.Text = $"Could not open {Path.GetFileName(path)}: {ex.Message}";
                return;
            }

            fileName = Path.GetFileName(path);
            Title = $"{fileName} - OpenNest (Avalonia prototype)";
            notice = null;
            Canvas.ClearCache();
            RefreshPlateList(0);
        }

        private void RefreshPlateList(int selectIndex)
        {
            PlateList.ItemsSource = nest.Plates
                .Select((plate, i) => $"Plate {i + 1}   {plate.Size.Length:0.###} x {plate.Size.Width:0.###}   ({plate.Parts.Count} parts)")
                .ToList();
            PlateList.SelectedIndex = nest.Plates.Count > 0 ? System.Math.Clamp(selectIndex, 0, nest.Plates.Count - 1) : -1;
        }

        /// <summary>Runs Auto Nest once the window opens, for --auto-nest.</summary>
        public void AutoNestOnOpen() => Opened += (_, _) => autoNest = RunAutoNestAsync();

        public void SaveSnapshotAndClose(string path, int plateNumber)
        {
            Opened += async (_, _) =>
            {
                if (nest?.Plates.Count > 0)
                    PlateList.SelectedIndex = System.Math.Clamp(plateNumber - 1, 0, nest.Plates.Count - 1);
                await autoNest;
                await System.Threading.Tasks.Task.Delay(500);
                var size = new global::Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height);
                using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(size);
                bitmap.Render(this);
                bitmap.Save(path);
                // The status line beside the image, so headless checks can read it.
                File.WriteAllText(path + ".txt", Status.Text);
                Close();
            };
        }

        private async void OnOpen(object sender, RoutedEventArgs e)
        {
            if (nesting != null)
                return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Nest",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Nest files") { Patterns = new[] { "*.nest" } } },
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path != null)
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
        /// Nests every drawing's remaining quantity on sheets like the shown plate through the
        /// shared pipeline, off the UI thread. Open is refused until it finishes, so the nest
        /// and its drawings stay stable from the request to the commit.
        /// </summary>
        private async Task RunAutoNestAsync()
        {
            if (nest == null || nesting != null)
                return;
            var engineName = EngineSelection.EngineName;
            var template = Canvas.Plate ?? nest.Plates.FirstOrDefault() ?? nest.PlateDefaults.CreateNew();
            var request = AutoNestJob.ForRemaining(nest, template, engineName);
            if (request == null)
            {
                ShowNotice("Auto Nest: nothing has a remaining quantity to nest.");
                return;
            }

            nesting = new CancellationTokenSource();
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
                    RefreshPlateList(firstNew);
                ShowNotice(kept.Nested ? $"{engineName}: {kept.Note}" : AutoNestJob.RefusalReason(result));
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
                SetNesting(false);
            }
        }

        private void SetNesting(bool running)
        {
            AutoNestButton.Content = running ? "Stop" : "Auto Nest";
            AutoNestMenuItem.Header = running ? "_Stop Auto Nest" : "_Auto Nest";
            OpenButton.IsEnabled = !running;
            OpenMenuItem.IsEnabled = !running;
        }

        private void ShowNotice(string text)
        {
            notice = text;
            UpdateStatus();
            if (Canvas.Plate == null)
                Status.Text = text;
        }

        private void OnFit(object sender, RoutedEventArgs e) => Canvas.ZoomToFit();

        private void OnPreviousPlate(object sender, RoutedEventArgs e) => StepPlate(-1);

        private void OnNextPlate(object sender, RoutedEventArgs e) => StepPlate(1);

        private void StepPlate(int step)
        {
            if (nest == null || nest.Plates.Count == 0)
                return;
            PlateList.SelectedIndex = System.Math.Clamp(PlateList.SelectedIndex + step, 0, nest.Plates.Count - 1);
        }

        private void OnPlateSelected(object sender, SelectionChangedEventArgs e)
        {
            var index = PlateList.SelectedIndex;
            var plate = nest != null && index >= 0 ? nest.Plates[index] : null;
            Canvas.Plate = plate;
            DrawingGrid.ItemsSource = plate?.Parts
                .Where(part => !part.BaseDrawing.IsCutOff)
                .GroupBy(part => part.BaseDrawing)
                .Select(group => new DrawingRow(group.Key.Name, group.Count(), SizeOf(group.First())))
                .OrderByDescending(row => row.Count)
                .ToList();
            UpdateStatus();
        }

        private static string SizeOf(Part part) =>
            $"{part.BoundingBox.Length:0.###} x {part.BoundingBox.Width:0.###}";

        private void UpdateStatus()
        {
            if (Canvas.Plate == null)
                return;
            var text = $"{fileName}   Plate {PlateList.SelectedIndex + 1} of {nest.Plates.Count}   Zoom {Canvas.Zoom:0.##} px/unit";
            if (Canvas.CursorWorld is { } at)
                text += $"   X {at.X:0.000}  Y {at.Y:0.000}";
            var part = Canvas.HoveredPart ?? Canvas.SelectedPart;
            if (part != null)
                text += $"   {(ReferenceEquals(part, Canvas.SelectedPart) ? "Selected" : "Over")}: {part.BaseDrawing.Name}";
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
