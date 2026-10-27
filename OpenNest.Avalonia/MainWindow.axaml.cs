using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenNest.IO;

namespace OpenNest.Avalonia
{
    public sealed record DrawingRow(string Name, int Count, string Size);

    public partial class MainWindow : Window
    {
        private Nest nest;
        private string fileName;

        public MainWindow()
        {
            InitializeComponent();
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
            try
            {
                nest = new NestReader(path).Read();
            }
            catch (Exception ex)
            {
                Status.Text = $"Could not open {Path.GetFileName(path)}: {ex.Message}";
                return;
            }

            fileName = Path.GetFileName(path);
            Title = $"{fileName} - OpenNest (Avalonia prototype)";
            Canvas.ClearCache();
            PlateList.ItemsSource = nest.Plates
                .Select((plate, i) => $"Plate {i + 1}   {plate.Size.Length:0.###} x {plate.Size.Width:0.###}   ({plate.Parts.Count} parts)")
                .ToList();
            PlateList.SelectedIndex = nest.Plates.Count > 0 ? 0 : -1;
        }

        public void SaveSnapshotAndClose(string path, int plateNumber)
        {
            Opened += async (_, _) =>
            {
                if (nest?.Plates.Count > 0)
                    PlateList.SelectedIndex = System.Math.Clamp(plateNumber - 1, 0, nest.Plates.Count - 1);
                await System.Threading.Tasks.Task.Delay(500);
                var size = new global::Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height);
                using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(size);
                bitmap.Render(this);
                bitmap.Save(path);
                Close();
            };
        }

        private async void OnOpen(object sender, RoutedEventArgs e)
        {
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
