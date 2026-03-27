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
        private SimplifierViewerForm simplifierViewer;

        public CadConverterForm()
        {
            InitializeComponent();

            fileList.SelectedIndexChanged += OnFileSelected;
            filterPanel.FilterChanged += OnFilterChanged;
            filterPanel.BendLineSelected += OnBendLineSelected;
            filterPanel.BendLineRemoved += OnBendLineRemoved;
            filterPanel.AddBendLineClicked += OnAddBendLineClicked;
            entityView1.LinePicked += OnLinePicked;
            entityView1.PickCancelled += OnPickCancelled;
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
            if (entityView1.IsPickingBendLine)
            {
                entityView1.IsPickingBendLine = false;
                filterPanel.SetPickMode(false);
            }
            entityView1.OriginalEntities = chkShowOriginal.Checked ? item.OriginalEntities : null;
            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(item.Entities);
            entityView1.Bends = item.Bends ?? new List<Bend>();

            item.Entities.ForEach(e => e.IsVisible = true);
            if (item.Entities.Any(e => e.Layer != null))
                item.Entities.ForEach(e => e.Layer.IsVisible = true);
            ReHidePromotedEntities(item.Bends);

            filterPanel.LoadItem(item.Entities, item.Bends);

            numQuantity.Value = item.Quantity;
            txtCustomer.Text = item.Customer ?? "";

            var bounds = item.Bounds;
            lblDimensions.Text = bounds != null
                ? $"{bounds.Width:0.#} x {bounds.Length:0.#}"
                : "";
            lblEntityCount.Text = $"{item.EntityCount} entities";

            entityView1.ZoomToFit();
            CheckSimplifiable(item);
        }

        private void CheckSimplifiable(FileListItem item)
        {
            ResetSimplifyButton();

            // Only check original (unsimplified) entities
            var entities = item.OriginalEntities ?? item.Entities;
            if (entities == null || entities.Count < 10) return;

            // Quick line count check — need at least MinLines consecutive lines
            var lineCount = entities.Count(e => e is Geometry.Line);
            if (lineCount < 3) return;

            // Run a quick analysis on a background thread
            var capturedEntities = new List<Entity>(entities);
            Task.Run(() =>
            {
                var shapes = ShapeBuilder.GetShapes(capturedEntities);
                var simplifier = new GeometrySimplifier();
                var count = 0;
                foreach (var shape in shapes)
                    count += simplifier.Analyze(shape).Count;
                return count;
            }).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result > 0)
                    HighlightSimplifyButton(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void HighlightSimplifyButton(int candidateCount)
        {
            btnSimplify.Text = $"Simplify ({candidateCount})";
            btnSimplify.BackColor = Color.FromArgb(60, 120, 60);
            btnSimplify.ForeColor = Color.White;
        }

        private void ResetSimplifyButton()
        {
            btnSimplify.Text = "Simplify...";
            btnSimplify.BackColor = SystemColors.Control;
            btnSimplify.ForeColor = SystemColors.ControlText;
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
            ReHidePromotedEntities(item.Bends);
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

            var bend = item.Bends[index];
            if (bend.SourceEntity != null)
                bend.SourceEntity.IsVisible = true;

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
            var originOffset = Vector.Zero;
            if (pgm.Codes.Count > 0 && pgm[0].Type == CodeType.RapidMove)
            {
                var rapid = (RapidMove)pgm[0];
                originOffset = rapid.EndPoint;
                pgm.Offset(-originOffset);
                pgm.Codes.RemoveAt(0);
            }

            var drawing = new Drawing(item.Name, pgm);
            drawing.Bends = item.Bends.Select(b => new Bend
            {
                StartPoint = new Vector(b.StartPoint.X - originOffset.X, b.StartPoint.Y - originOffset.Y),
                EndPoint = new Vector(b.EndPoint.X - originOffset.X, b.EndPoint.Y - originOffset.Y),
                Direction = b.Direction,
                Angle = b.Angle,
                Radius = b.Radius,
                NoteText = b.NoteText,
            }).ToList();

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
            var splitItems = new List<FileListItem>();

            for (var i = 0; i < form.ResultDrawings.Count; i++)
            {
                var splitDrawing = form.ResultDrawings[i];

                var splitName = $"{baseName}-{i + 1}.dxf";
                var splitPath = GetUniquePath(Path.Combine(writableDir, splitName));

                splitWriter.Write(splitPath, splitDrawing);
                newItems.Add(splitPath);

                // Re-import geometry but keep bends from the split drawing
                var importer = new DxfImporter();
                importer.SplinePrecision = Settings.Default.ImportSplinePrecision;
                var result = importer.Import(splitPath);

                var splitItem = new FileListItem
                {
                    Name = Path.GetFileNameWithoutExtension(splitPath),
                    Entities = result.Entities,
                    Path = splitPath,
                    Quantity = item.Quantity,
                    Customer = item.Customer,
                    Bends = splitDrawing.Bends ?? new List<Bend>(),
                    Bounds = result.Entities.GetBoundingBox(),
                    EntityCount = result.Entities.Count
                };
                splitItems.Add(splitItem);
            }

            // Remove original and add split items directly (preserving bend info)
            fileList.RemoveAt(index);
            foreach (var splitItem in splitItems)
                fileList.AddItem(splitItem);

            if (writableDir != sourceDir)
                MessageBox.Show($"Split files written to: {writableDir}", "Split Output",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnAddBendLineClicked(object sender, EventArgs e)
        {
            var active = !entityView1.IsPickingBendLine;
            entityView1.IsPickingBendLine = active;
            filterPanel.SetPickMode(active);
        }

        private void OnLinePicked(object sender, Line line)
        {
            using var dialog = new BendLineDialog();
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var item = CurrentItem;
            if (item == null) return;

            var bend = new Bend
            {
                StartPoint = line.StartPoint,
                EndPoint = line.EndPoint,
                Direction = dialog.Direction,
                Angle = dialog.BendAngle,
                Radius = dialog.BendRadius,
                SourceEntity = line
            };

            line.IsVisible = false;
            item.Bends.Add(bend);
            entityView1.Bends = item.Bends;
            filterPanel.LoadItem(item.Entities, item.Bends);
            entityView1.Invalidate();
        }

        private void OnPickCancelled(object sender, EventArgs e)
        {
            entityView1.IsPickingBendLine = false;
            filterPanel.SetPickMode(false);
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

        private void OnSimplifyClick(object sender, EventArgs e)
        {
            if (entityView1.Entities == null || entityView1.Entities.Count == 0)
                return;

            // Always simplify from original geometry to prevent tolerance creep
            var item = CurrentItem;
            if (item != null && item.OriginalEntities == null)
                item.OriginalEntities = new List<Entity>(item.Entities);

            var sourceEntities = item?.OriginalEntities ?? entityView1.Entities;
            var shapes = ShapeBuilder.GetShapes(sourceEntities);
            if (shapes.Count == 0)
                return;

            if (simplifierViewer == null || simplifierViewer.IsDisposed)
            {
                simplifierViewer = new SimplifierViewerForm();
                simplifierViewer.Owner = this;
                simplifierViewer.Applied += OnSimplifierApplied;

                // Position next to this form
                var screen = Screen.FromControl(this);
                simplifierViewer.Location = new Point(
                    System.Math.Min(Right, screen.WorkingArea.Right - simplifierViewer.Width),
                    Top);
            }

            simplifierViewer.LoadShapes(shapes, entityView1);
        }

        private void OnSimplifierApplied(List<Entity> entities)
        {
            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(entities);
            entityView1.ZoomToFit();
            entityView1.Invalidate();

            var item = CurrentItem;
            if (item != null)
            {
                item.Entities = entities;
                item.EntityCount = entities.Count;
                item.Bounds = entities.GetBoundingBox();
            }

            lblEntityCount.Text = $"{entities.Count} entities";
            ResetSimplifyButton();
        }

        private void OnShowOriginalChanged(object sender, EventArgs e)
        {
            var item = CurrentItem;
            entityView1.OriginalEntities = chkShowOriginal.Checked ? item?.OriginalEntities : null;
            entityView1.Invalidate();
        }

        private void OnExportDxfClick(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item == null) return;

            using var dlg = new SaveFileDialog
            {
                Filter = "DXF Files|*.dxf",
                FileName = Path.ChangeExtension(item.Name, ".dxf"),
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;

            var doc = new ACadSharp.CadDocument();
            foreach (var entity in item.Entities)
            {
                switch (entity)
                {
                    case Geometry.Line line:
                        doc.Entities.Add(new ACadSharp.Entities.Line
                        {
                            StartPoint = new CSMath.XYZ(line.StartPoint.X, line.StartPoint.Y, 0),
                            EndPoint = new CSMath.XYZ(line.EndPoint.X, line.EndPoint.Y, 0),
                        });
                        break;

                    case Geometry.Arc arc:
                        var startAngle = arc.StartAngle;
                        var endAngle = arc.EndAngle;
                        if (arc.IsReversed)
                            OpenNest.Math.Generic.Swap(ref startAngle, ref endAngle);
                        doc.Entities.Add(new ACadSharp.Entities.Arc
                        {
                            Center = new CSMath.XYZ(arc.Center.X, arc.Center.Y, 0),
                            Radius = arc.Radius,
                            StartAngle = startAngle,
                            EndAngle = endAngle,
                        });
                        break;

                    case Geometry.Circle circle:
                        doc.Entities.Add(new ACadSharp.Entities.Circle
                        {
                            Center = new CSMath.XYZ(circle.Center.X, circle.Center.Y, 0),
                            Radius = circle.Radius,
                        });
                        break;
                }
            }

            using var writer = new ACadSharp.IO.DxfWriter(dlg.FileName, doc, false);
            writer.Write();
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

        private static void ReHidePromotedEntities(List<Bend> bends)
        {
            if (bends == null) return;
            foreach (var bend in bends)
            {
                if (bend.SourceEntity != null)
                    bend.SourceEntity.IsVisible = false;
            }
        }

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
