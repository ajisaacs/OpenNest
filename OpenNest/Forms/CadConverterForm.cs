using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.IO.Bending;
using OpenNest.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class CadConverterForm : Form
    {
        private static int colorIndex;

        public CadConverterForm()
        {
            InitializeComponent();

            fileList.SelectedIndexChanged += OnFileSelected;
            filterPanel.FilterChanged += OnFilterChanged;
            filterPanel.BendLineSelected += OnBendLineSelected;
            filterPanel.BendLineRemoved += OnBendLineRemoved;
            btnSplit.Click += OnSplitClicked;
            numQuantity.ValueChanged += OnQuantityChanged;
            txtCustomer.TextChanged += OnCustomerChanged;
            cboBendDetector.SelectedIndexChanged += OnBendDetectorChanged;

            // Populate bend detector dropdown
            cboBendDetector.Items.Add("Auto");
            foreach (var detector in BendDetectorRegistry.Detectors)
                cboBendDetector.Items.Add(detector.Name);
            cboBendDetector.SelectedIndex = 0;

            // Drag & drop
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        private FileListItem CurrentItem => fileList.SelectedItem;

        #region File Import

        public void AddFile(string file) => AddFile(file, 0, null);

        private void AddFile(string file, int detectorIndex, string detectorName)
        {
            try
            {
                var importer = new DxfImporter();
                importer.SplinePrecision = Settings.Default.ImportSplinePrecision;
                var result = importer.Import(file);

                if (result.Entities.Count == 0)
                    return;

                // Compute bounds
                var bounds = result.Entities.GetBoundingBox();

                // Detect bends (detectorIndex/Name captured on UI thread)
                var bends = new List<Bend>();
                if (result.Document != null)
                {
                    bends = detectorIndex == 0
                        ? BendDetectorRegistry.AutoDetect(result.Document)
                        : BendDetectorRegistry.GetByName(detectorName)
                            ?.DetectBends(result.Document)
                          ?? new List<Bend>();
                }

                var item = new FileListItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Entities = result.Entities,
                    Path = file,
                    Quantity = 1,
                    Customer = string.Empty,
                    Bends = bends,
                    Bounds = bounds,
                    EntityCount = result.Entities.Count
                };

                if (InvokeRequired)
                    BeginInvoke((Action)(() => fileList.AddItem(item)));
                else
                    fileList.AddItem(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing \"{file}\": {ex.Message}");
            }
        }

        public void AddFiles(IEnumerable<string> files)
        {
            var fileArray = files.ToArray();
            // Capture UI state on main thread before entering parallel loop
            var detectorIndex = cboBendDetector.SelectedIndex;
            var detectorName = cboBendDetector.SelectedItem?.ToString();

            System.Threading.Tasks.Task.Run(() =>
            {
                Parallel.ForEach(fileArray, file => AddFile(file, detectorIndex, detectorName));
            });
        }

        #endregion

        #region Event Handlers

        private void OnFileSelected(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null)
            {
                ClearDetailBar();
                return;
            }

            LoadItem(item);
        }

        private void LoadItem(FileListItem item)
        {
            entityView1.ClearPenCache();
            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(item.Entities);
            entityView1.Bends = item.Bends ?? new List<Bend>();

            item.Entities.ForEach(e => e.IsVisible = true);
            if (item.Entities.Any(e => e.Layer != null))
                item.Entities.ForEach(e => e.Layer.IsVisible = true);

            filterPanel.LoadItem(item.Entities, item.Bends);

            numQuantity.Value = item.Quantity;
            txtCustomer.Text = item.Customer ?? "";

            var bounds = item.Bounds;
            lblDimensions.Text = bounds != null
                ? $"{bounds.Width:0.#} x {bounds.Length:0.#}"
                : "";
            lblEntityCount.Text = $"{item.EntityCount} entities";

            entityView1.ZoomToFit();
        }

        private void ClearDetailBar()
        {
            numQuantity.Value = 1;
            txtCustomer.Text = "";
            lblDimensions.Text = "";
            lblEntityCount.Text = "";
            entityView1.Entities.Clear();
            entityView1.Invalidate();
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item == null) return;

            filterPanel.ApplyFilters(item.Entities);
            entityView1.Invalidate();
        }

        private void OnBendLineSelected(object sender, int index)
        {
            entityView1.SelectedBendIndex = index;
            entityView1.Invalidate();
        }

        private void OnBendLineRemoved(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null || index < 0 || index >= item.Bends.Count) return;

            item.Bends.RemoveAt(index);
            entityView1.Bends = item.Bends;
            entityView1.SelectedBendIndex = -1;
            filterPanel.LoadItem(item.Entities, item.Bends);
            entityView1.Invalidate();
        }

        private void OnQuantityChanged(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item == null) return;

            item.Quantity = (int)numQuantity.Value;
            fileList.Invalidate();
        }

        private void OnCustomerChanged(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item != null)
                item.Customer = txtCustomer.Text;
        }

        private void OnBendDetectorChanged(object sender, EventArgs e)
        {
            // Re-run bend detection on current item if it has a document
            // For now, bend detection only runs at import time
        }

        private void OnSplitClicked(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item == null) return;

            var entities = item.Entities.Where(en => en.Layer.IsVisible && en.IsVisible).ToList();
            if (entities.Count == 0) return;

            var shape = new ShapeProfile(entities);
            SetRotation(shape.Perimeter, RotationType.CW);
            foreach (var cutout in shape.Cutouts)
                SetRotation(cutout, RotationType.CCW);

            var drawEntities = new List<Entity>();
            drawEntities.AddRange(shape.Perimeter.Entities);
            shape.Cutouts.ForEach(c => drawEntities.AddRange(c.Entities));

            var pgm = ConvertGeometry.ToProgram(drawEntities);
            if (pgm.Codes.Count > 0 && pgm[0].Type == CodeType.RapidMove)
            {
                var rapid = (RapidMove)pgm[0];
                pgm.Offset(-rapid.EndPoint);
                pgm.Codes.RemoveAt(0);
            }

            var drawing = new Drawing(item.Name, pgm);

            using var form = new SplitDrawingForm(drawing);
            if (form.ShowDialog(this) != DialogResult.OK || form.ResultDrawings?.Count <= 1)
                return;

            // Write split DXF files and re-import
            var sourceDir = Path.GetDirectoryName(item.Path);
            var baseName = Path.GetFileNameWithoutExtension(item.Path);
            var writableDir = Directory.Exists(sourceDir) && IsDirectoryWritable(sourceDir)
                ? sourceDir
                : Path.GetTempPath();

            var index = fileList.SelectedIndex;
            var newItems = new List<string>();

            var splitWriter = new SplitDxfWriter();

            for (var i = 0; i < form.ResultDrawings.Count; i++)
            {
                var splitDrawing = form.ResultDrawings[i];

                // Assign bends from the source item — spatial filtering is a future enhancement
                splitDrawing.Bends.AddRange(item.Bends);

                var splitName = $"{baseName}_split{i + 1}.dxf";
                var splitPath = GetUniquePath(Path.Combine(writableDir, splitName));

                splitWriter.Write(splitPath, splitDrawing);
                newItems.Add(splitPath);
            }

            // Remove original and add split files
            fileList.RemoveAt(index);
            foreach (var path in newItems)
                AddFile(path);

            if (writableDir != sourceDir)
                MessageBox.Show($"Split files written to: {writableDir}", "Split Output",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var dxfFiles = files.Where(f =>
                    f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (dxfFiles.Length > 0)
                    AddFiles(dxfFiles);
            }
        }

        #endregion

        #region Output

        public List<Drawing> GetDrawings()
        {
            var drawings = new List<Drawing>();

            foreach (var item in fileList.Items)
            {
                var entities = item.Entities.Where(e => e.Layer.IsVisible && e.IsVisible).ToList();

                if (entities.Count == 0)
                    continue;

                var drawing = new Drawing(item.Name);
                drawing.Color = GetNextColor();
                drawing.Customer = item.Customer;
                drawing.Source.Path = item.Path;
                drawing.Quantity.Required = item.Quantity;

                // Copy bends
                if (item.Bends != null)
                    drawing.Bends.AddRange(item.Bends);

                var shape = new ShapeProfile(entities);

                SetRotation(shape.Perimeter, RotationType.CW);

                foreach (var cutout in shape.Cutouts)
                    SetRotation(cutout, RotationType.CCW);

                entities = new List<Entity>();
                entities.AddRange(shape.Perimeter.Entities);
                shape.Cutouts.ForEach(cutout => entities.AddRange(cutout.Entities));

                var pgm = ConvertGeometry.ToProgram(entities);
                var firstCode = pgm[0];

                if (firstCode.Type == CodeType.RapidMove)
                {
                    var rapid = (RapidMove)firstCode;
                    drawing.Source.Offset = rapid.EndPoint;
                    pgm.Offset(-rapid.EndPoint);
                    pgm.Codes.RemoveAt(0);
                }

                drawing.Program = pgm;
                drawings.Add(drawing);

                Thread.Sleep(20);
            }

            return drawings;
        }

        #endregion

        #region Helpers

        private static void SetRotation(Shape shape, RotationType rotation)
        {
            try
            {
                var dir = shape.ToPolygon(3).RotationDirection();
                if (dir != rotation) shape.Reverse();
            }
            catch { }
        }

        private static Color GetNextColor()
        {
            var color = ColorScheme.PartColors[colorIndex % ColorScheme.PartColors.Length];
            colorIndex++;
            return color;
        }

        private static bool IsDirectoryWritable(string path)
        {
            try
            {
                var testFile = Path.Combine(path, $".writetest_{Guid.NewGuid()}");
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
                return true;
            }
            catch { return false; }
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var counter = 2;

            while (File.Exists(path))
            {
                path = Path.Combine(dir, $"{name}_{counter}{ext}");
                counter++;
            }

            return path;
        }

        #endregion
    }
}
