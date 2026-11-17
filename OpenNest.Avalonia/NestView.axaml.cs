using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenNest.Avalonia.Controls;
using OpenNest.Desktop;
using OpenNest.Desktop.Interaction;

namespace OpenNest.Avalonia
{
    public sealed record DrawingRow(string Name, int Nested, int Required, Drawing Drawing);

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
            // Adding parts from the drawing list, as the WinForms nest window: a press picks a
            // drawing up for the plate view, a drag drops one part on the plate.
            DrawingGrid.AddHandler(PointerPressedEvent, OnDrawingPressed, RoutingStrategies.Tunnel,
                handledEventsToo: true);
            DrawingGrid.AddHandler(PointerMovedEvent, OnDrawingDragged, RoutingStrategies.Tunnel,
                handledEventsToo: true);
            DrawingGrid.AddHandler(PointerReleasedEvent, (_, _) => pressedDrawing = null,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            Canvas.GotFocus += OnCanvasFocused;
            DragDrop.SetAllowDrop(Canvas, true);
            Canvas.AddHandler(DragDrop.DragEnterEvent, OnDrawingDragOver);
            Canvas.AddHandler(DragDrop.DragOverEvent, OnDrawingDragOver);
            Canvas.AddHandler(DragDrop.DropEvent, OnDrawingDrop);
        }

        /// <summary>The drag format carrying a <see cref="Drawing"/> from the list.</summary>
        public const string DrawingFormat = "OpenNest.Drawing";

        private readonly DrawingPickup pickup = new();
        private Drawing pressedDrawing;
        private Point pressPoint;

        private static Drawing DrawingUnder(PointerEventArgs e) =>
            (e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true)
                ?.DataContext is DrawingRow row
                ? row.Drawing
                : null;

        // As the WinForms list: a single left press picks the drawing up (a drag can start from
        // it); a double-click puts it down.
        private void OnDrawingPressed(object sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(DrawingGrid);
            pressPoint = point.Position;
            pressedDrawing = null;
            if (!point.Properties.IsLeftButtonPressed)
                return;
            if (e.ClickCount != 1)
            {
                pickup.PutDown();
                return;
            }
            pressedDrawing = DrawingUnder(e);
            if (pressedDrawing != null)
                pickup.PickUp();
        }

        // A press moved more than 3 pixels drags the drawing; a drop on the plate puts it down.
        private async void OnDrawingDragged(object sender, PointerEventArgs e)
        {
            var drawing = pressedDrawing;
            var point = e.GetCurrentPoint(DrawingGrid);
            if (drawing == null || !point.Properties.IsLeftButtonPressed)
                return;
            var dx = point.Position.X - pressPoint.X;
            var dy = point.Position.Y - pressPoint.Y;
            if (dx * dx + dy * dy <= 9)
                return;
            pressedDrawing = null;
            var data = new DataObject();
            data.Set(DrawingFormat, drawing);
            try
            {
                var result = await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
                if (result != DragDropEffects.None)
                    pickup.PutDown();
            }
            catch (Exception ex)
            {
                // A platform drag failure leaves the drawing picked up, as a cancelled drag does.
                System.Diagnostics.Debug.WriteLine($"Drawing drag failed: {ex}");
            }
        }

        /// <summary>
        /// Puts a picked-up drawing down: Edit &gt; Cut and Copy start their own place tool and
        /// then focus the view, which would otherwise swap in the picked drawing.
        /// </summary>
        public void CancelPickup()
        {
            pickup.PutDown();
            pressedDrawing = null;
        }

        private void OnCanvasFocused(object sender, GotFocusEventArgs e)
        {
            var selected = (DrawingGrid.SelectedItem as DrawingRow)?.Drawing;
            if (pickup.TakeOnPlateEnter(selected) is { } drawing)
                Canvas.PlaceDrawing(drawing);
        }

        // Only this nest's drawings, and none while edits are locked (WinForms refuses the drop).
        private Drawing DroppedDrawing(DragEventArgs e) =>
            e.Data.Get(DrawingFormat) is Drawing drawing
                && Tab.Document.Nest.Drawings.Any(d => ReferenceEquals(d, drawing))
                && !Canvas.EditsLocked
                ? drawing
                : null;

        private void OnDrawingDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.Contains(DrawingFormat))
                return;
            e.DragEffects = DroppedDrawing(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrawingDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.Contains(DrawingFormat))
                return;
            var drawing = DroppedDrawing(e);
            var dropped = drawing != null
                && Canvas.DropDrawing(drawing, e.GetPosition(Canvas)) != null;
            // Put down now, not when the drag returns: on X11 and macOS the pointer can enter the
            // plate (and focus it) before the awaiting drag source resumes.
            if (dropped)
                pickup.PutDown();
            e.DragEffects = dropped ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
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
            pickup.PutDown();
            pressedDrawing = null;
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
            // As the WinForms list: the selected drawing stays selected when the list is rebuilt,
            // matched as WinForms matches it (by name), so it survives an Undo's new objects.
            var selected = (DrawingGrid.SelectedItem as DrawingRow)?.Drawing;
            DrawingGrid.ItemsSource = nest.Drawings
                .Where(d => !d.IsCutOff)
                .Select(d => new DrawingRow(d.Name, d.Quantity.Nested, d.Quantity.Required, d))
                .ToList();
            DrawingGrid.SelectedItem = ((IEnumerable<DrawingRow>)DrawingGrid.ItemsSource)
                .FirstOrDefault(row => Equals(row.Drawing, selected));
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
