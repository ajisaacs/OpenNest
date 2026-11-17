using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenNest.Avalonia.Controls;
using OpenNest.Desktop;

namespace OpenNest.Avalonia
{
    public sealed record DrawingRow(string Name, int Nested, int Required);

    /// <summary>
    /// One open nest: Plates and Drawings tabs on the left, the plate header and plate view on
    /// the right. Each tab keeps its own view, so switching nests keeps each one's plate and zoom.
    /// </summary>
    public partial class NestView : UserControl
    {
        public NestView()
        {
            InitializeComponent();
        }

        public NestView(NestTab tab)
            : this()
        {
            Tab = tab;
            Canvas.ViewChanged += (_, _) => ViewChanged?.Invoke(this, EventArgs.Empty);
            Canvas.BeginChangeHandler = (label, key) => tab.Undo.BeginChange(label, key);
            tab.Undo.SwapIn = SwapIn;
            // Deleting parts changes the plate and drawing counts the lists show.
            tab.Undo.Changed += (_, _) => RefreshCounts();
            Canvas.CutOffSettings = CutOffSettingsFor(tab.Document.Nest);
            plates = new PlateManager(tab.Document.Nest);
            // Starts watching the last two plates (the tab already put the empty plate there).
            plates.EnsureSentinel();
            Refresh(0);
        }

        // Undo and Redo: puts the copy read back from the history in the document and shows it
        // on the same plate, keeping the zoom, pan and selection (by part index while the part
        // count is unchanged). Every list is rebuilt: each held the old nest's objects.
        private void SwapIn(Nest restored)
        {
            var plateIndex = System.Math.Max(0, PlateList.SelectedIndex);
            var shown = Canvas.Plate;
            keptSelection = shown != null
                ? (Canvas.Selection.PartIndices, shown.Parts.Count)
                : (Array.Empty<int>(), -1);
            // Read first: a throw here leaves the live nest in place, as the recorder expects.
            var cutOffSettings = CutOffSettingsFor(restored);
            // As WinForms (EditNestForm EndAction): a tool holds the old nest's parts, so it ends
            // and Escape cannot bring it back.
            Canvas.Tools.EndTool();
            Tab.Document.Nest = restored;
            Canvas.CutOffSettings = cutOffSettings;
            plates.Dispose();
            plates = new PlateManager(restored);
            plates.EnsureSentinel();
            // Reading recolours drawings from the file; the view keeps the application's scheme.
            // Cut-offs stay as reading regenerated them, as when the nest was opened.
            ColorSchemeRegistry.RecolorDrawings(restored);
            keepView = true;
            try
            {
                Refresh(plateIndex);
            }
            finally
            {
                keepView = false;
            }
        }

        // While an Undo or Redo swaps its copy in: the plate is shown without zooming to fit,
        // and the parts selected before (their indices, the plate's part count then) are selected again.
        private bool keepView;

        // Keeps one empty plate at the end of the nest as edits add or remove parts on the last
        // two plates, inside the edit's own undo step, as the WinForms nest window's manager does.
        private PlateManager plates;
        private (IReadOnlyList<int> Indices, int PartCount) keptSelection = (Array.Empty<int>(), -1);

        /// <summary>Raised when the shown plate, zoom, hover or selection changes.</summary>
        public event EventHandler ViewChanged;

        public NestTab Tab { get; }

        public PlateCanvas PlateView => Canvas;

        public int PlateIndex => PlateList.SelectedIndex;

        public int PlateCount => Tab.Document.Nest.Plates.Count;

        /// <summary>Rebuilds both lists and shows plate <paramref name="selectIndex"/>.</summary>
        public void Refresh(int selectIndex)
        {
            var nest = Tab.Document.Nest;
            // As the WinForms nest window: one empty plate stays at the end to nest onto, so a
            // new nest shows Plate 1. Files leave empty plates out, so saving is unchanged. The
            // tab put it there on opening and the manager keeps it, so this only reads.
            Canvas.ClearCache();
            PlateList.ItemsSource = nest.Plates
                .Select((plate, i) =>
                    $"Plate {i + 1}   {SizeText(plate)}   ({plate.Parts.Count} parts)")
                .ToList();
            PlateList.SelectedIndex = nest.Plates.Count > 0
                ? System.Math.Clamp(selectIndex, 0, nest.Plates.Count - 1)
                : -1;
            DrawingGrid.ItemsSource = nest.Drawings
                .Where(d => !d.IsCutOff)
                .Select(d => new DrawingRow(d.Name, d.Quantity.Nested, d.Quantity.Required))
                .ToList();
            shownCounts = CountsText(nest);
            ShowPlate();
        }

        // As the WinForms nest window binding a nest: the saved head-lock defaults in the nest's
        // units, then the machine's cut-off defaults for its material and thickness.
        private static CutOffs.CutOffSettings CutOffSettingsFor(Nest nest)
        {
            var settings = new CutOffs.CutOffSettings();
            SavedCutOffHeadLock.Read(Data.UserSettingsFile.Read(Data.UserSettingsFile.DefaultPath))
                .ApplyTo(settings, nest.Units);
            DesktopSettings.Machines.ApplyCutOffDefaults(settings, nest);
            return settings;
        }

        // After an edit: rebuilds the lists only when their counts changed, keeping the shown
        // plate, its zoom and the selection, as an Undo does.
        private void RefreshCounts()
        {
            var nest = Tab.Document.Nest;
            var counts = CountsText(nest);
            if (counts == shownCounts)
                return;
            var shown = Canvas.Plate;
            keptSelection = shown != null
                ? (Canvas.Selection.PartIndices, shown.Parts.Count)
                : (Array.Empty<int>(), -1);
            keepView = true;
            try
            {
                Refresh(System.Math.Max(0, PlateList.SelectedIndex));
            }
            finally
            {
                keepView = false;
            }
        }

        private string shownCounts;

        private static string CountsText(Nest nest) =>
            string.Join(",", nest.Plates.Select(p => p.Parts.Count)) + "|"
            + string.Join(",", nest.Drawings.Select(d => d.Quantity.Nested));

        public void ShowPlate(int index)
        {
            if (PlateCount > 0)
                PlateList.SelectedIndex = System.Math.Clamp(index, 0, PlateCount - 1);
        }

        public void StepPlate(int step) => ShowPlate(PlateList.SelectedIndex + step);

        private void OnPlateSelected(object sender, SelectionChangedEventArgs e) => ShowPlate();

        private void ShowPlate()
        {
            var nest = Tab.Document.Nest;
            var index = PlateList.SelectedIndex;
            var plate = index >= 0 && index < nest.Plates.Count ? nest.Plates[index] : null;
            if (keepView)
                Canvas.ReplacePlate(plate,
                    plate?.Parts.Count == keptSelection.PartCount ? keptSelection.Indices : Array.Empty<int>());
            else
                Canvas.Plate = plate;
            PlateLabel.Text = plate == null
                ? "No plates"
                : $"Plate {index + 1} of {nest.Plates.Count}    "
                    + $"{SizeText(plate)}    qty {plate.Quantity}";
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string SizeText(Plate plate) =>
            $"{plate.Size.Length:0.###} x {plate.Size.Width:0.###}";

        private void OnFirstPlate(object sender, RoutedEventArgs e) => ShowPlate(0);

        private void OnPreviousPlate(object sender, RoutedEventArgs e) => StepPlate(-1);

        private void OnNextPlate(object sender, RoutedEventArgs e) => StepPlate(1);

        private void OnLastPlate(object sender, RoutedEventArgs e) => ShowPlate(PlateCount - 1);
    }
}
