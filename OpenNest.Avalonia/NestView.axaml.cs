using System;
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
            Refresh(0);
        }

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
            ShowPlate();
        }

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
