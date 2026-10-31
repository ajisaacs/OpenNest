using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Bending;
using OpenNest.Breakoffs;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Converters;
using OpenNest.Dimensions;
using OpenNest.Geometry;
using OpenNest.Help;
using OpenNest.IO;
using OpenNest.IO.Bending;
using OpenNest.TabbedCutouts;

namespace OpenNest.Forms
{
    public partial class CadConverterForm : Form
    {
        private static readonly Regex MTextFormattingPattern = new(
            @"\\[A-Za-z][^;]*;",
            RegexOptions.None,
            TimeSpan.FromSeconds(1)
        );

        private SimplifierViewerForm simplifierViewer;
        private bool staleProgram = true;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData == Keys.Up || keyData == Keys.Down) && !fileList.Focused)
            {
                fileList.ProcessArrowKey(keyData);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public CadConverterForm()
        {
            InitializeComponent();
            HelpTopics.Attach(this, HelpTopics.ImportDrawings);

            fileList.SelectedIndexChanged += OnFileSelected;
            filterPanel.FilterChanged += OnFilterChanged;
            filterPanel.BendLineSelected += OnBendLineSelected;
            filterPanel.BendLineRemoved += OnBendLineRemoved;
            filterPanel.BendLineEdited += OnBendLineEdited;
            filterPanel.AddBendLineClicked += OnAddBendLineClicked;
            filterPanel.AddBreakoffClicked += OnAddBreakoffClicked;
            filterPanel.BreakoffSelected += OnBreakoffSelected;
            filterPanel.BreakoffEdited += OnBreakoffEdited;
            filterPanel.BreakoffRemoved += OnBreakoffRemoved;
            filterPanel.BreakoffFlipped += OnBreakoffFlipped;
            entityView1.BreakoffEdgePicked += OnBreakoffEdgePicked;
            entityView1.TabDragged += OnTabDragged;
            filterPanel.AddTabbedCutoutClicked += OnAddTabbedCutoutClicked;
            filterPanel.TabbedCutoutSelected += OnTabbedCutoutSelected;
            filterPanel.TabbedCutoutEdited += OnTabbedCutoutEdited;
            filterPanel.TabbedCutoutRemoved += OnTabbedCutoutRemoved;
            entityView1.TabbedCutoutPicked += OnTabbedCutoutPicked;
            entityView1.CutoutTabDragged += OnCutoutTabDragged;
            entityView1.LinePicked += OnLinePicked;
            entityView1.PickCanceled += OnPickCanceled;
            filterPanel.AddDimensionsClicked += OnAddDimensionsClicked;
            filterPanel.DimensionSelected += OnDimensionSelected;
            filterPanel.DimensionRemoved += OnDimensionRemoved;
            entityView1.DimensionDragged += OnDimensionDragged;
            entityView1.TextConvertRequested += OnTextConvertRequested;
            entityView1.EntityActionRequested += OnEntityActionRequested;
            btnSplit.Click += OnSplitClicked;
            numQuantity.ValueChanged += OnQuantityChanged;
            txtCustomer.TextChanged += OnCustomerChanged;
            cboBendDetector.SelectedIndexChanged += OnBendDetectorChanged;

            // Populate bend detector dropdown
            cboBendDetector.Items.Add("Auto");
            foreach (var detector in BendDetectorRegistry.Detectors)
                cboBendDetector.Items.Add(detector.Name);
            cboBendDetector.SelectedIndex = 0;

            viewTabs.SelectedIndexChanged += OnViewTabChanged;

            // Drag & drop
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        private FileListItem CurrentItem => fileList.SelectedItem;

        #region File Import

        /// <summary>The units of the nest receiving the drawings; SVGs import in these.</summary>
        internal Units DrawingUnits { get; set; }

        public void AddFile(string file) => AddFile(file, NewImportOptions(0, null));

        private CadImportOptions NewImportOptions(int detectorIndex, string detectorName) =>
            new CadImportOptions
            {
                BendDetectorName = detectorIndex == 0 ? null : detectorName,
                DrawingUnits = DrawingUnits,
            };

        private void AddFile(string file, CadImportOptions options)
        {
            try
            {
                var result = CadImporter.Import(file, options);
                if (result.Warnings.Count > 0)
                    ReportImportWarnings(file, result.Warnings);
                if (result.Entities.Count == 0)
                    return;

                var item = new FileListItem
                {
                    Name = result.Name,
                    Entities = result.Entities,
                    Path = result.SourcePath,
                    Quantity = 1,
                    Customer = string.Empty,
                    Bends = result.Bends,
                    Bounds = result.Bounds,
                    EntityCount = result.Entities.Count,
                    Texts = ExtractTexts(result.Document),
                };

                if (InvokeRequired)
                    BeginInvoke((Action)(() => fileList.AddItem(item)));
                else
                    fileList.AddItem(item);
            }
            catch (RegexMatchTimeoutException ex)
            {
                ReportImportFailure(file, new InvalidDataException(
                    "CAD text extraction timed out. The file was not imported.", ex));
            }
            catch (Exception ex)
            {
                ReportImportFailure(file, ex);
            }
        }

        internal virtual void ReportImportFailure(string file, Exception error)
        {
            MessageBox.Show($"Error importing \"{file}\": {error.Message}");
        }

        internal virtual void ReportImportWarnings(string file, IReadOnlyList<string> warnings)
        {
            MessageBox.Show($"\"{Path.GetFileName(file)}\" was only partly imported:\n\n{string.Join("\n", warnings)}");
        }

        /// <summary>Asks for any SVG's scale; false skips the file.</summary>
        internal virtual bool ResolveScale(string file, CadImportOptions options) =>
            // Files can arrive before this form is shown, and a hidden owner cannot host a dialog.
            SvgScaleForm.TryResolve(Visible ? this : null, file, options);

        public void AddFiles(IEnumerable<string> files)
        {
            // Capture UI state and ask about scale on the main thread before the parallel loop
            var detectorIndex = cboBendDetector.SelectedIndex;
            var detectorName = cboBendDetector.SelectedItem?.ToString();
            var imports = new List<(string File, CadImportOptions Options)>();
            foreach (var file in files)
            {
                var options = NewImportOptions(detectorIndex, detectorName);
                try
                {
                    if (!ResolveScale(file, options))
                        continue;
                }
                catch (InvalidDataException ex)
                {
                    ReportImportFailure(file, ex);
                    continue;
                }
                imports.Add((file, options));
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                Parallel.ForEach(imports, import => AddFile(import.File, import.Options));
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
            staleProgram = true;
            if (viewTabs.SelectedTab == tabProgram)
                LoadProgramTab();
            else
                programEditor.Clear();
        }

        private void LoadItem(FileListItem item)
        {
            entityView1.ClearPenCache();
            if (entityView1.IsPickingBendLine)
            {
                entityView1.IsPickingBendLine = false;
                filterPanel.SetPickMode(false);
            }
            if (entityView1.IsPickingBreakoff)
            {
                entityView1.IsPickingBreakoff = false;
                filterPanel.SetBreakoffPickMode(false);
            }
            if (entityView1.IsPickingTabbedCutout)
            {
                entityView1.IsPickingTabbedCutout = false;
                filterPanel.SetTabbedCutoutPickMode(false);
            }
            entityView1.SelectedBreakoffIndex = -1;
            entityView1.SelectedTabbedCutoutIndex = -1;
            entityView1.SelectedDimensionIndex = -1;
            entityView1.OriginalEntities = chkShowOriginal.Checked ? item.OriginalEntities : null;
            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(item.Entities);
            entityView1.Bends = item.Bends ?? new List<Bend>();
            entityView1.Texts = item.Texts ?? new List<CadText>();

            item.Entities.ForEach(e => e.IsVisible = true);
            if (item.Entities.Any(e => e.Layer != null))
                item.Entities.ForEach(e => e.Layer.IsVisible = true);
            ReHidePromotedEntities(item.Bends);
            ReHideSuppressedEntities(item);
            ReHideExcludedEntities(item);
            entityView1.ClearSelection();
            entityView1.EntityRoles = item.EntityRoles;
            entityView1.ExcludedIds = item.ExcludedEntityIds;

            filterPanel.LoadItem(item.Entities, item.Bends);
            RefreshBreakoffs(item);
            RefreshTabbedCutouts(item);

            numQuantity.Value = item.Quantity;
            txtCustomer.Text = item.Customer ?? "";

            var bounds = item.Bounds;
            lblDimensions.Text = bounds != null ? $"{bounds.Width:0.#} x {bounds.Length:0.#}" : "";
            lblEntityCount.Text = $"{item.EntityCount} entities";

            entityView1.ZoomToFit();
            CheckSimplifiable(item);
            RefreshHoleTable();
        }

        private void CheckSimplifiable(FileListItem item)
        {
            ResetSimplifyButton();

            // Only check original (unsimplified) entities
            var entities = item.OriginalEntities ?? item.Entities;
            if (entities == null || entities.Count < 10)
                return;

            // Quick line count check — need at least MinLines consecutive lines
            var lineCount = entities.Count(e => e is Geometry.Line);
            if (lineCount < 3)
                return;

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
                })
                .ContinueWith(
                    t =>
                    {
                        if (t.IsCompletedSuccessfully && t.Result > 0)
                            HighlightSimplifyButton(t.Result);
                    },
                    TaskScheduler.FromCurrentSynchronizationContext()
                );
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
            entityView1.DimensionLayouts = Array.Empty<DimensionLayout>();
            entityView1.Invalidate();
            RefreshHoleTable();
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item == null)
                return;

            filterPanel.ApplyFilters(item.Entities);
            ReHideExcludedEntities(item);
            ReHidePromotedEntities(item.Bends);
            SyncSuppressedState(item);
            RefreshBreakoffs(item);
            RefreshTabbedCutouts(item);
            RefreshHoleTable();
            staleProgram = true;
        }

        private void OnViewTabChanged(object sender, EventArgs e)
        {
            if (viewTabs.SelectedTab == tabProgram && staleProgram)
                LoadProgramTab();
        }

        private void LoadProgramTab()
        {
            var item = CurrentItem;
            if (item == null)
            {
                programEditor.Clear();
                staleProgram = false;
                return;
            }

            var entities = VisibleEntities(item);
            if (entities.Count == 0)
            {
                programEditor.Clear();
                staleProgram = false;
                return;
            }

            var cut = TabbedCutoutBuilder.Apply(BreakoffBuilder.Apply(entities, item.Breakoffs, out _),
                item.TabbedCutouts, out _);
            var normalized = ShapeProfile.NormalizeEntities(cut);
            programEditor.LoadEntities(normalized);
            staleProgram = false;
        }

        private void OnBendLineSelected(object sender, int index)
        {
            entityView1.SelectedBendIndex = index;
            entityView1.Invalidate();
        }

        private void OnBendLineRemoved(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null || index < 0 || index >= item.Bends.Count)
                return;

            var bend = item.Bends[index];
            if (bend.SourceEntity != null)
                bend.SourceEntity.IsVisible = true;

            item.Bends.RemoveAt(index);
            Bend.UpdateEtchEntities(item.Entities, item.Bends);
            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(item.Entities);
            entityView1.Bends = item.Bends;
            entityView1.SelectedBendIndex = -1;
            filterPanel.LoadItem(item.Entities, item.Bends);
            RefreshDimensions(item);
            entityView1.Invalidate();
        }

        private void OnBendLineEdited(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null || index < 0 || index >= item.Bends.Count)
                return;

            var bend = item.Bends[index];
            using var dialog = new BendLineDialog();
            dialog.LoadBend(bend);

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            bend.Direction = dialog.Direction;
            bend.Angle = dialog.BendAngle;
            bend.Radius = dialog.BendRadius;

            Bend.UpdateEtchEntities(item.Entities, item.Bends);
            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(item.Entities);
            entityView1.Bends = item.Bends;
            filterPanel.LoadItem(item.Entities, item.Bends);
            entityView1.Invalidate();
        }

        private void OnQuantityChanged(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item == null)
                return;

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
            if (item == null)
                return;

            var entities = VisibleEntities(item);
            if (entities.Count == 0)
                return;

            var normalized = ShapeProfile.NormalizeEntities(entities);
            var pgm = ConvertGeometry.ToProgram(normalized);
            var originOffset = Vector.Zero;
            if (pgm.Codes.Count > 0 && pgm[0].Type == CodeType.RapidMove)
            {
                var rapid = (RapidMove)pgm[0];
                originOffset = rapid.EndPoint;
                pgm.Offset(-originOffset);
            }

            var drawing = new Drawing(item.Name, pgm);
            drawing.Bends = item
                .Bends.Select(b => new Bend
                {
                    StartPoint = new Vector(
                        b.StartPoint.X - originOffset.X,
                        b.StartPoint.Y - originOffset.Y
                    ),
                    EndPoint = new Vector(
                        b.EndPoint.X - originOffset.X,
                        b.EndPoint.Y - originOffset.Y
                    ),
                    Direction = b.Direction,
                    Angle = b.Angle,
                    Radius = b.Radius,
                    NoteText = b.NoteText,
                })
                .ToList();

            using var form = new SplitDrawingForm(drawing);
            if (form.ShowDialog(this) != DialogResult.OK || form.ResultDrawings?.Count <= 1)
                return;

            // Write split DXF files and re-import
            var sourceDir = Path.GetDirectoryName(item.Path);
            var baseName = Path.GetFileNameWithoutExtension(item.Path);
            var writableDir =
                Directory.Exists(sourceDir) && IsDirectoryWritable(sourceDir)
                    ? sourceDir
                    : Path.GetTempPath();

            var index = fileList.SelectedIndex;

            var splitWriter = new SplitDxfWriter();
            var splitItems = new List<FileListItem>();

            for (var i = 0; i < form.ResultDrawings.Count; i++)
            {
                var splitDrawing = form.ResultDrawings[i];

                var splitName = $"{baseName}-{i + 1}.dxf";
                var splitPath = GetUniquePath(Path.Combine(writableDir, splitName));

                splitWriter.Write(splitPath, splitDrawing);

                // Re-import geometry but keep bends from the split drawing
                var result = Dxf.Import(splitPath);

                var splitItem = new FileListItem
                {
                    Name = Path.GetFileNameWithoutExtension(splitPath),
                    Entities = result.Entities,
                    Path = splitPath,
                    Quantity = item.Quantity,
                    Customer = item.Customer,
                    Bends = splitDrawing.Bends ?? new List<Bend>(),
                    Bounds = result.Entities.GetBoundingBox(),
                    EntityCount = result.Entities.Count,
                };
                splitItems.Add(splitItem);
            }

            // Remove original and add split items directly (preserving bend info)
            fileList.RemoveAt(index);
            foreach (var splitItem in splitItems)
                fileList.AddItem(splitItem);

            if (writableDir != sourceDir)
                MessageBox.Show(
                    $"Split files written to: {writableDir}",
                    "Split Output",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
        }

        private void OnAddBendLineClicked(object sender, EventArgs e)
        {
            entityView1.IsPickingBreakoff = false;
            filterPanel.SetBreakoffPickMode(false);
            entityView1.IsPickingTabbedCutout = false;
            filterPanel.SetTabbedCutoutPickMode(false);
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
            if (item == null)
                return;

            var bend = new Bend
            {
                StartPoint = line.StartPoint,
                EndPoint = line.EndPoint,
                Direction = dialog.Direction,
                Angle = dialog.BendAngle,
                Radius = dialog.BendRadius,
                SourceEntity = line,
            };

            line.IsVisible = false;
            item.Bends.Add(bend);
            Bend.UpdateEtchEntities(item.Entities, item.Bends);
            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(item.Entities);
            entityView1.Bends = item.Bends;
            filterPanel.LoadItem(item.Entities, item.Bends);
            RefreshDimensions(item);
            entityView1.Invalidate();
        }

        private void OnPickCanceled(object sender, EventArgs e)
        {
            entityView1.IsPickingBendLine = false;
            filterPanel.SetPickMode(false);
            entityView1.IsPickingBreakoff = false;
            filterPanel.SetBreakoffPickMode(false);
            entityView1.IsPickingTabbedCutout = false;
            filterPanel.SetTabbedCutoutPickMode(false);
        }

        // The entities the program is built from: shown, with role overrides applied and
        // display-only entities left out.
        private static List<Entity> VisibleEntities(FileListItem item) =>
            EntityRoles.ForProgram(item.Entities.Where(en => en.Layer.IsVisible && en.IsVisible), item.EntityRoles);

        private void RefreshBreakoffs(FileListItem item)
        {
            var visible = VisibleEntities(item);
            var allEdges = item.Breakoffs.SelectMany(b => b.EdgeIds).ToList();
            var layouts = item.Breakoffs.Select(b => BreakoffBuilder.Layout(visible, b, allEdges)).ToList();
            entityView1.BreakoffLayouts = layouts;
            entityView1.BreakCutEntries = BreakCutEntries(visible, item.Breakoffs, layouts);
            filterPanel.LoadBreakoffs(layouts
                .Select((l, i) => l.IsValid
                    ? $"Breakoff {i + 1}: {l.TabCenters.Count} tabs"
                    : $"Breakoff {i + 1} (not applied): {l.Error}")
                .ToList(), entityView1.SelectedBreakoffIndex);
            RefreshDimensions(item);
            entityView1.Invalidate();
        }

        // Entered where the cutting strategies will enter them: against the part's contours as
        // they will be cut, with every breakoff applied.
        private static List<(Vector, Vector)> BreakCutEntries(List<Entity> visible, List<Breakoff> breakoffs,
            List<BreakoffLayout> layouts)
        {
            var entries = new List<(Vector, Vector)>();
            if (!layouts.Any(l => l.IsValid))
                return entries;
            var contours = BreakoffBuilder.Apply(visible, breakoffs, out _)
                .Where(en => en.Layer != SpecialLayers.Break && en is Geometry.Line or Arc or Circle
                    && !string.Equals(en.Layer?.Name, "ETCH", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(en.Layer?.Name, SpecialLayers.Scribe.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var shapes = ShapeBuilder.GetShapes(contours);
            foreach (var cut in layouts.Where(l => l.IsValid).SelectMany(l => l.Cuts))
            {
                var (entry, scrap, _) = CNC.CuttingStrategy.ContourCuttingStrategy.BreakCutEntry(
                    new Shape { Entities = cut.ToList() }, shapes);
                entries.Add((entry, scrap));
            }
            return entries;
        }

        private void OnAddBreakoffClicked(object sender, EventArgs e)
        {
            entityView1.IsPickingBendLine = false;
            filterPanel.SetPickMode(false);
            entityView1.IsPickingTabbedCutout = false;
            filterPanel.SetTabbedCutoutPickMode(false);
            var active = !entityView1.IsPickingBreakoff;
            entityView1.IsPickingBreakoff = active;
            filterPanel.SetBreakoffPickMode(active);
        }

        private void OnBreakoffEdgePicked(object sender, (Entity Entity, Vector Point) pick)
        {
            entityView1.IsPickingBreakoff = false;
            filterPanel.SetBreakoffPickMode(false);
            var item = CurrentItem;
            if (item == null)
                return;

            SplitAtJunctions(item);
            var visible = VisibleEntities(item);
            var entity = visible.Where(en => en is Geometry.Line or Arc)
                .OrderBy(en => en.ClosestPointTo(pick.Point).DistanceTo(pick.Point)).FirstOrDefault();
            if (entity == null)
                return;
            var chain = BreakoffBuilder.FindEdgeChain(visible, entity);
            var used = item.Breakoffs.SelectMany(b => b.EdgeIds).ToHashSet();
            if (chain.Any(en => used.Contains(en.Id)))
            {
                ShowBreakoffMessage("That line already belongs to a breakoff.");
                return;
            }

            var breakoff = new Breakoff { EdgeIds = chain.Select(en => en.Id).ToList() };
            var layout = BreakoffBuilder.Layout(visible, breakoff, used);
            if (!layout.IsValid)
            {
                ShowBreakoffMessage("Could not make a breakoff from that line: " + layout.Error
                    + "\n\nClick the line that joins the part and the scrap. Its ends must touch the "
                    + "outline around both.");
                return;
            }

            using var dialog = new BreakoffDialog();
            if (!AskBreakoffSettings(dialog))
                return;
            if (!TryConfigure(item, breakoff, dialog, keepTabs: false))
                return;

            item.Breakoffs.Add(breakoff);
            entityView1.SelectedBreakoffIndex = item.Breakoffs.Count - 1;
            RefreshBreakoffs(item);
            staleProgram = true;
        }

        // A joining line often ends partway along an outline segment; splitting there lets it be
        // picked exactly. Existing breakoffs keep every piece of a split joining line.
        private void SplitAtJunctions(FileListItem item)
        {
            var split = BreakoffBuilder.SplitAtJunctions(item.Entities);
            if (split.Count == 0)
                return;

            foreach (var breakoff in item.Breakoffs)
                foreach (var id in breakoff.EdgeIds.ToList())
                    if (split.TryGetValue(id, out var pieces))
                        breakoff.EdgeIds.AddRange(pieces.Where(p => p != id));
            foreach (var cutout in item.TabbedCutouts)
                foreach (var id in cutout.EdgeIds.ToList())
                    if (split.TryGetValue(id, out var pieces))
                        cutout.EdgeIds.AddRange(pieces.Where(p => p != id));
            foreach (var (id, pieces) in split)
            {
                foreach (var piece in pieces.Where(p => p != id))
                {
                    if (item.ExcludedEntityIds.Contains(id))
                        item.ExcludedEntityIds.Add(piece);
                    if (item.EntityRoles.TryGetValue(id, out var role))
                        item.EntityRoles[piece] = role;
                }
            }

            item.EntityCount = item.Entities.Count;
            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(item.Entities);
            lblEntityCount.Text = $"{item.EntityCount} entities";
        }

        private void OnBreakoffEdited(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null || index < 0 || index >= item.Breakoffs.Count)
                return;

            var current = item.Breakoffs[index];
            using var dialog = new BreakoffDialog();
            dialog.LoadBreakoff(current);
            if (!AskBreakoffSettings(dialog))
                return;

            var edited = current.Clone();
            var keepTabs = dialog.TabCount == current.TabCenters.Count
                && Math.Tolerance.IsEqualTo(dialog.TabWidth, current.TabWidth);
            if (!TryConfigure(item, edited, dialog, keepTabs))
                return;

            item.Breakoffs[index] = edited;
            RefreshBreakoffs(item);
            staleProgram = true;
        }

        // Applies the dialog's settings, spacing the tabs evenly unless asked to keep them, and
        // refuses settings that do not resolve.
        private bool TryConfigure(FileListItem item, Breakoff breakoff, BreakoffDialog dialog, bool keepTabs)
        {
            var visible = VisibleEntities(item);
            var others = item.Breakoffs.Where(b => !b.EdgeIds.SequenceEqual(breakoff.EdgeIds))
                .SelectMany(b => b.EdgeIds).ToList();

            breakoff.TabWidth = dialog.TabWidth;
            breakoff.Flip = dialog.Flip;
            if (!keepTabs)
            {
                var probe = breakoff.Clone();
                probe.TabCenters.Clear();
                var length = BreakoffBuilder.Layout(visible, probe, others).Length;
                breakoff.TabCenters = BreakoffBuilder.EvenTabCenters(length, dialog.TabCount, dialog.TabWidth);
            }

            var layout = BreakoffBuilder.Layout(visible, breakoff, others);
            if (layout.IsValid)
                return true;
            ShowBreakoffMessage("Those settings do not fit this breakoff: " + layout.Error);
            return false;
        }

        private void OnBreakoffRemoved(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null || index < 0 || index >= item.Breakoffs.Count)
                return;

            item.Breakoffs.RemoveAt(index);
            entityView1.SelectedBreakoffIndex = -1;
            RefreshBreakoffs(item);
            staleProgram = true;
        }

        private void OnBreakoffFlipped(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null || index < 0 || index >= item.Breakoffs.Count)
                return;

            var flipped = item.Breakoffs[index].Clone();
            flipped.Flip = !flipped.Flip;
            var others = item.Breakoffs.Where((_, i) => i != index).SelectMany(b => b.EdgeIds).ToList();
            var layout = BreakoffBuilder.Layout(VisibleEntities(item), flipped, others);
            if (!layout.IsValid)
            {
                ShowBreakoffMessage("Cannot flip this breakoff: " + layout.Error);
                return;
            }

            item.Breakoffs[index] = flipped;
            entityView1.SelectedBreakoffIndex = index;
            RefreshBreakoffs(item);
            staleProgram = true;
        }

        private void OnBreakoffSelected(object sender, int index)
        {
            entityView1.SelectedBreakoffIndex = index;
            entityView1.Invalidate();
        }

        private void OnTabDragged(object sender, TabDragEventArgs e)
        {
            var item = CurrentItem;
            if (item == null || e.Breakoff >= item.Breakoffs.Count)
                return;

            var breakoff = item.Breakoffs[e.Breakoff];
            if (e.Tab >= breakoff.TabCenters.Count)
                return;
            breakoff.TabCenters[e.Tab] = e.Center;
            entityView1.SelectedBreakoffIndex = e.Breakoff;
            RefreshBreakoffs(item);
            staleProgram = true;
        }

        // Tabbed cutouts resolve against the entities that will be cut: breakoffs come first.
        private static List<Entity> CutoutEntities(FileListItem item) =>
            BreakoffBuilder.Apply(VisibleEntities(item), item.Breakoffs, out _);

        private void RefreshTabbedCutouts(FileListItem item)
        {
            var entities = CutoutEntities(item);
            var layouts = item.TabbedCutouts.Select(c => TabbedCutoutBuilder.Layout(entities, c)).ToList();
            entityView1.TabbedCutoutLayouts = layouts;
            filterPanel.LoadTabbedCutouts(layouts
                .Select((l, i) => l.IsValid
                    ? $"Cutout {i + 1}: {l.TabCenters.Count} tab{(l.TabCenters.Count == 1 ? "" : "s")}"
                    : $"Cutout {i + 1} (not applied): {l.Error}")
                .ToList(), entityView1.SelectedTabbedCutoutIndex);
            RefreshDimensions(item);
            entityView1.Invalidate();
        }

        private void OnAddTabbedCutoutClicked(object sender, EventArgs e)
        {
            entityView1.IsPickingBendLine = false;
            filterPanel.SetPickMode(false);
            entityView1.IsPickingBreakoff = false;
            filterPanel.SetBreakoffPickMode(false);
            var active = !entityView1.IsPickingTabbedCutout;
            entityView1.IsPickingTabbedCutout = active;
            filterPanel.SetTabbedCutoutPickMode(active);
        }

        private void OnTabbedCutoutPicked(object sender, (Entity Entity, Vector Point) pick)
        {
            entityView1.IsPickingTabbedCutout = false;
            filterPanel.SetTabbedCutoutPickMode(false);
            var item = CurrentItem;
            if (item == null)
                return;

            var visible = VisibleEntities(item);
            var entity = visible.Where(en => en is Geometry.Line or Arc or Circle)
                .OrderBy(en => en.ClosestPointTo(pick.Point).DistanceTo(pick.Point)).FirstOrDefault();
            if (entity == null)
                return;
            var hole = TabbedCutoutBuilder.FindHole(visible, entity);
            if (hole.Count == 0)
            {
                ShowTabbedCutoutMessage("That is not a closed hole. Click a circle, or a line or arc of a "
                    + "cutout that closes on itself.");
                return;
            }
            var used = item.TabbedCutouts.SelectMany(c => c.EdgeIds).ToHashSet();
            if (hole.Any(en => used.Contains(en.Id)))
            {
                ShowTabbedCutoutMessage("That hole is already a tabbed cutout.");
                return;
            }

            var cutout = new TabbedCutout { EdgeIds = hole.Select(en => en.Id).ToList() };
            var layout = TabbedCutoutBuilder.Layout(CutoutEntities(item), cutout);
            if (!layout.IsValid)
            {
                ShowTabbedCutoutMessage("Could not make a tabbed cutout from that hole: " + layout.Error);
                return;
            }

            using var dialog = new TabbedCutoutDialog();
            if (!AskTabbedCutoutSettings(dialog))
                return;
            if (!TryConfigure(item, cutout, dialog, keepTabs: false))
                return;

            item.TabbedCutouts.Add(cutout);
            entityView1.SelectedTabbedCutoutIndex = item.TabbedCutouts.Count - 1;
            RefreshTabbedCutouts(item);
            staleProgram = true;
        }

        private void OnTabbedCutoutEdited(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null || index < 0 || index >= item.TabbedCutouts.Count)
                return;

            var current = item.TabbedCutouts[index];
            using var dialog = new TabbedCutoutDialog();
            dialog.LoadCutout(current);
            if (!AskTabbedCutoutSettings(dialog))
                return;

            var edited = current.Clone();
            var keepTabs = dialog.TabCount == current.TabCenters.Count
                && Math.Tolerance.IsEqualTo(dialog.TabWidth, current.TabWidth);
            if (!TryConfigure(item, edited, dialog, keepTabs))
                return;

            item.TabbedCutouts[index] = edited;
            RefreshTabbedCutouts(item);
            staleProgram = true;
        }

        // Applies the dialog's settings, spacing the tabs evenly unless asked to keep them, and
        // refuses settings that do not resolve.
        private bool TryConfigure(FileListItem item, TabbedCutout cutout, TabbedCutoutDialog dialog, bool keepTabs)
        {
            var entities = CutoutEntities(item);
            cutout.TabWidth = dialog.TabWidth;
            if (!keepTabs)
            {
                var probe = cutout.Clone();
                probe.TabCenters.Clear();
                probe.TabWidth = TabbedCutout.DefaultTabWidth;
                var even = TabbedCutoutBuilder.Layout(entities, probe);
                if (even.IsValid)
                    cutout.TabCenters = even.EvenTabCenters(dialog.TabCount);
            }

            var layout = TabbedCutoutBuilder.Layout(entities, cutout);
            if (layout.IsValid)
                return true;
            ShowTabbedCutoutMessage("Those settings do not fit this cutout: " + layout.Error);
            return false;
        }

        private void OnTabbedCutoutRemoved(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null || index < 0 || index >= item.TabbedCutouts.Count)
                return;

            item.TabbedCutouts.RemoveAt(index);
            entityView1.SelectedTabbedCutoutIndex = -1;
            RefreshTabbedCutouts(item);
            staleProgram = true;
        }

        private void OnTabbedCutoutSelected(object sender, int index)
        {
            entityView1.SelectedTabbedCutoutIndex = index;
            entityView1.Invalidate();
        }

        private void OnCutoutTabDragged(object sender, CutoutTabDragEventArgs e)
        {
            var item = CurrentItem;
            if (item == null || e.Cutout >= item.TabbedCutouts.Count)
                return;

            var cutout = item.TabbedCutouts[e.Cutout];
            if (e.Tab >= cutout.TabCenters.Count)
                return;
            cutout.TabCenters[e.Tab] = e.Center;
            entityView1.SelectedTabbedCutoutIndex = e.Cutout;
            RefreshTabbedCutouts(item);
            staleProgram = true;
        }

        // Dimensions measure the entities as they will be cut: breakoffs and tabbed cutouts applied.
        private static List<Entity> DimensionedEntities(FileListItem item) =>
            TabbedCutoutBuilder.Apply(CutoutEntities(item), item.TabbedCutouts, out _);

        // Radius and diameter find their curve by ID before tabbed cutouts split it into pieces.
        private void RefreshDimensions(FileListItem item)
        {
            var entities = item.Dimensions.Any(d => !d.IsRadial) ? DimensionedEntities(item) : new List<Entity>();
            var curves = item.Dimensions.Any(d => d.IsRadial) ? VisibleEntities(item) : new List<Entity>();
            var layouts = item.Dimensions
                .Select(d => DimensionBuilder.Layout(d.IsRadial ? curves : entities, d))
                .ToList();
            entityView1.DimensionLayouts = layouts;
            filterPanel.LoadDimensions(layouts
                .Select(l => DimensionLabel(l))
                .ToList(), entityView1.SelectedDimensionIndex);
        }

        private static string DimensionLabel(DimensionLayout layout)
        {
            var name = layout.Kind switch
            {
                DimensionKind.OverallHorizontal => "Horizontal",
                DimensionKind.OverallVertical => "Vertical",
                DimensionKind.Radius => "Radius",
                _ => "Diameter",
            };
            return layout.IsValid ? $"{name}: {layout.ValueText}" : $"{name} (not shown): {layout.Error}";
        }

        private void OnAddDimensionsClicked(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item == null)
                return;

            var entities = DimensionedEntities(item);
            if (DimensionBuilder.Extents(entities) == null)
            {
                ShowDimensionMessage("There is no cut geometry to dimension.");
                return;
            }

            item.Dimensions.AddRange(DimensionBuilder.MissingOverall(entities, item.Dimensions));
            RefreshDimensions(item);
            entityView1.ZoomToFit();
        }

        private void AddRadialDimensions(FileListItem item, IEnumerable<Entity> curves, DimensionKind kind)
        {
            var visible = VisibleEntities(item).Select(en => en.Id).ToHashSet();
            var cut = curves.Where(c => visible.Contains(c.Id)).ToList();
            if (cut.Count == 0)
            {
                ShowDimensionMessage("Only a circle or arc that is cut or etched can be dimensioned. Right-click it and choose Cut or Etch first.");
                return;
            }

            var added = DimensionBuilder.MissingRadial(cut, kind, DimensionedEntities(item), item.Dimensions);
            if (added.Count == 0)
                return;
            item.Dimensions.AddRange(added);
            entityView1.SelectedDimensionIndex = item.Dimensions.Count - 1;
            RefreshDimensions(item);
            entityView1.Invalidate();
        }

        private void OnDimensionRemoved(object sender, int index)
        {
            var item = CurrentItem;
            if (item == null || index < 0 || index >= item.Dimensions.Count)
                return;

            item.Dimensions.RemoveAt(index);
            entityView1.SelectedDimensionIndex = -1;
            RefreshDimensions(item);
            entityView1.Invalidate();
        }

        private void OnDimensionSelected(object sender, int index)
        {
            entityView1.SelectedDimensionIndex = index;
            entityView1.Invalidate();
        }

        private void OnDimensionDragged(object sender, DimensionDragEventArgs e)
        {
            var item = CurrentItem;
            if (item == null || e.Dimension >= item.Dimensions.Count)
                return;

            item.Dimensions[e.Dimension].Offset = e.Offset;
            if (e.Angle is { } angle)
                item.Dimensions[e.Dimension].Angle = angle;
            entityView1.SelectedDimensionIndex = e.Dimension;
            RefreshDimensions(item);
            entityView1.Invalidate();
        }

        internal virtual void ShowDimensionMessage(string message) =>
            MessageBox.Show(this, message, "Dimensions", MessageBoxButtons.OK, MessageBoxIcon.Information);

        internal virtual bool AskTabbedCutoutSettings(TabbedCutoutDialog dialog) =>
            dialog.ShowDialog(this) == DialogResult.OK;

        internal virtual void ShowTabbedCutoutMessage(string message) =>
            MessageBox.Show(this, message, "Tabbed Cutout", MessageBoxButtons.OK, MessageBoxIcon.Information);

        internal virtual bool AskBreakoffSettings(BreakoffDialog dialog) =>
            dialog.ShowDialog(this) == DialogResult.OK;

        internal virtual void ShowBreakoffMessage(string message) =>
            MessageBox.Show(this, message, "Breakoff", MessageBoxButtons.OK, MessageBoxIcon.Information);

        private void OnTextConvertRequested(object sender, Controls.CadText text)
        {
            var item = CurrentItem;
            if (item == null)
                return;

            var font = LoadChrFont();
            if (font == null)
                return;

            var layer = new Geometry.Layer("ENGRAVE")
            {
                Color = System.Drawing.Color.Cyan,
                IsVisible = true,
            };

            var entities = font.RenderText(text.Value, text.Height, Geometry.Vector.Zero, layer);
            if (entities.Count > 0)
            {
                var box = entities.GetBoundingBox();
                var shiftX = text.HAlign switch
                {
                    System.Drawing.StringAlignment.Center => text.Position.X
                        - (box.Left + box.Right) / 2,
                    System.Drawing.StringAlignment.Far => text.Position.X - box.Right,
                    _ => text.Position.X - box.Left,
                };
                var shiftY = text.VAlign switch
                {
                    System.Drawing.StringAlignment.Center => text.Position.Y
                        - (box.Top + box.Bottom) / 2,
                    System.Drawing.StringAlignment.Near => text.Position.Y - box.Top,
                    _ => text.Position.Y - box.Bottom,
                };
                var shift = new Geometry.Vector(shiftX, shiftY);
                foreach (var e in entities)
                    e.Offset(shift);
            }
            if (entities.Count == 0)
            {
                MessageBox.Show(
                    $"No geometry produced for \"{text.Value}\".",
                    "Convert Text",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            item.Entities.AddRange(entities);
            item.Texts.Remove(text);
            item.EntityCount = item.Entities.Count;
            item.Bounds = item.Entities.GetBoundingBox();

            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(item.Entities);
            entityView1.Texts = item.Texts;
            filterPanel.LoadItem(item.Entities, item.Bends);
            RefreshDimensions(item);
            entityView1.Invalidate();
            staleProgram = true;
            lblEntityCount.Text = $"{item.EntityCount} entities";
        }

        private ChrFont cachedChrFont;
        private string cachedChrFontPath;

        private ChrFont LoadChrFont()
        {
            if (cachedChrFont != null)
                return cachedChrFont;

            // Look for .CHR files next to the app, then prompt
            var appDir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            var candidates = Directory.GetFiles(appDir, "*.CHR", SearchOption.TopDirectoryOnly);

            string fontPath;
            if (candidates.Length == 1)
            {
                fontPath = candidates[0];
            }
            else if (candidates.Length > 1)
            {
                fontPath = PromptForChrFile(appDir);
            }
            else
            {
                fontPath = PromptForChrFile(null);
            }

            if (fontPath == null)
                return null;

            try
            {
                cachedChrFont = ChrFont.Read(fontPath);
                cachedChrFontPath = fontPath;
                return cachedChrFont;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error loading font: {ex.Message}",
                    "Font Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return null;
            }
        }

        private static string PromptForChrFile(string initialDir)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select Engraving Font (.CHR)",
                Filter = "Gravograph Font (*.CHR)|*.CHR",
                InitialDirectory = initialDir ?? "",
            };
            return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
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
                var cadFiles = files.Where(CadFormats.IsCadFile).ToArray();
                if (cadFiles.Length > 0)
                    AddFiles(cadFiles);
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
                    Top
                );
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
                RefreshDimensions(item);
            }

            lblEntityCount.Text = $"{entities.Count} entities";
            ResetSimplifyButton();
            RefreshHoleTable();
        }

        private HoleTableForm holeTable;

        private void OnHoleTableClick(object sender, EventArgs e)
        {
            if (holeTable == null || holeTable.IsDisposed)
            {
                holeTable = new HoleTableForm();
                var corner = entityView1.PointToScreen(new Point(entityView1.Width, 0));
                holeTable.Location = new Point(corner.X - holeTable.Width - 12, corner.Y + 12);
                holeTable.FormClosed += (_, _) =>
                {
                    holeTable = null;
                    entityView1.HoleColors = null;
                };
                holeTable.Show(this);
            }
            else
            {
                holeTable.Activate();
            }

            RefreshHoleTable();
        }

        // Lists the current drawing's round holes as they will be cut and colors them on the view.
        private void RefreshHoleTable()
        {
            if (holeTable == null || holeTable.IsDisposed)
                return;

            var item = CurrentItem;
            var sizes = item == null ? Array.Empty<HoleSize>() : HoleTable.Build(VisibleEntities(item));
            entityView1.HoleColors = holeTable.ShowSizes(sizes, item?.Name);
        }

        private void OnShowOriginalChanged(object sender, EventArgs e)
        {
            var item = CurrentItem;
            entityView1.OriginalEntities = chkShowOriginal.Checked ? item?.OriginalEntities : null;
            entityView1.Invalidate();
        }

        private void OnLabelsChanged(object sender, EventArgs e)
        {
            entityView1.ShowEntityLabels = chkLabels.Checked;
            entityView1.Invalidate();
        }

        private const int SvgExportFilterIndex = 8;

        private void OnExportDxfClick(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item == null)
                return;

            using var dlg = new SaveFileDialog
            {
                Filter =
                    "DXF 2018 (*.dxf)|*.dxf|"
                    + "DXF 2013 (*.dxf)|*.dxf|"
                    + "DXF 2010 (*.dxf)|*.dxf|"
                    + "DXF 2007 (*.dxf)|*.dxf|"
                    + "DXF 2004 (*.dxf)|*.dxf|"
                    + "DXF 2000 (*.dxf)|*.dxf|"
                    + "DXF R14 (*.dxf)|*.dxf|"
                    + "SVG (*.svg)|*.svg",
                FileName = Path.ChangeExtension(item.Name, ".dxf"),
            };

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            if (dlg.FilterIndex == SvgExportFilterIndex)
            {
                Svg.ExportEntities(item.Entities, Path.ChangeExtension(dlg.FileName, ".svg"), DrawingUnits);
                return;
            }

            var version = dlg.FilterIndex switch
            {
                2 => ACadSharp.ACadVersion.AC1027,
                3 => ACadSharp.ACadVersion.AC1024,
                4 => ACadSharp.ACadVersion.AC1021,
                5 => ACadSharp.ACadVersion.AC1018,
                6 => ACadSharp.ACadVersion.AC1015,
                7 => ACadSharp.ACadVersion.AC1014,
                _ => ACadSharp.ACadVersion.AC1032,
            };

            var doc = new ACadSharp.CadDocument(version);
            foreach (var entity in item.Entities)
            {
                switch (entity)
                {
                    case Geometry.Line line:
                        doc.Entities.Add(
                            new ACadSharp.Entities.Line
                            {
                                StartPoint = new CSMath.XYZ(
                                    line.StartPoint.X,
                                    line.StartPoint.Y,
                                    0
                                ),
                                EndPoint = new CSMath.XYZ(line.EndPoint.X, line.EndPoint.Y, 0),
                            }
                        );
                        break;

                    case Geometry.Arc arc:
                        var startAngle = arc.StartAngle;
                        var endAngle = arc.EndAngle;
                        if (arc.IsReversed)
                            OpenNest.Math.Generic.Swap(ref startAngle, ref endAngle);
                        doc.Entities.Add(
                            new ACadSharp.Entities.Arc
                            {
                                Center = new CSMath.XYZ(arc.Center.X, arc.Center.Y, 0),
                                Radius = arc.Radius,
                                StartAngle = startAngle,
                                EndAngle = endAngle,
                            }
                        );
                        break;

                    case Geometry.Circle circle:
                        doc.Entities.Add(
                            new ACadSharp.Entities.Circle
                            {
                                Center = new CSMath.XYZ(circle.Center.X, circle.Center.Y, 0),
                                Radius = circle.Radius,
                            }
                        );
                        break;
                }
            }

            using var writer = new ACadSharp.IO.DxfWriter(dlg.FileName, doc, false);
            writer.Write();
        }

        #endregion

        #region Load Existing Drawings

        public void LoadDrawings(IEnumerable<Drawing> drawings)
        {
            foreach (var drawing in drawings)
            {
                var entities = CadImporter.EditableEntities(drawing);
                var bounds = entities.GetBoundingBox();

                var item = new FileListItem
                {
                    Name = drawing.Name,
                    Entities = entities,
                    Path = drawing.Source?.Path,
                    Quantity = drawing.Quantity.Required,
                    Customer = drawing.Customer ?? string.Empty,
                    Bends = drawing.Bends?.ToList() ?? new List<Bend>(),
                    Breakoffs = drawing.Breakoffs?.Select(b => b.Clone()).ToList() ?? new List<Breakoff>(),
                    TabbedCutouts = drawing.TabbedCutouts?.Select(c => c.Clone()).ToList() ?? new List<TabbedCutout>(),
                    Dimensions = drawing.Dimensions?.Select(d => d.Clone()).ToList() ?? new List<DrawingDimension>(),
                    SuppressedEntityIds =
                        drawing.SuppressedEntityIds.Count > 0
                            ? new HashSet<Guid>(drawing.SuppressedEntityIds)
                            : null,
                    ExcludedEntityIds = IndividuallySuppressed(entities, drawing.SuppressedEntityIds),
                    EntityRoles = new Dictionary<Guid, EntityRole>(drawing.EntityRoles),
                    Bounds = bounds,
                    EntityCount = entities.Count,
                };

                fileList.AddItem(item);
            }
        }

        #endregion

        #region Output

        public List<Drawing> GetDrawings()
        {
            var drawings = new List<Drawing>();

            foreach (var item in fileList.Items)
            {
                var visible = item.Entities.Where(e => e.Layer.IsVisible && e.IsVisible).ToList();

                if (EntityRoles.ForProgram(visible, item.EntityRoles).Count == 0)
                    continue;

                // Rebuild a CadImportResult from the FileListItem's current state so
                // BuildDrawing sees the user's edits (filters, suppressions, new bends).
                var result = new CadImportResult
                {
                    Entities = item.Entities,
                    Bends = item.Bends ?? new List<Bend>(),
                    Bounds = item.Bounds,
                    SourcePath = item.Path,
                    Name = item.Name,
                };

                var editedProgram =
                    (item == CurrentItem && programEditor.IsDirty && programEditor.Program != null)
                        ? programEditor.Program
                        : null;

                var drawing = CadImporter.BuildDrawing(
                    result,
                    visible,
                    result.Bends,
                    item.Quantity,
                    item.Customer,
                    editedProgram,
                    item.Breakoffs,
                    item.TabbedCutouts,
                    item.EntityRoles,
                    item.Dimensions
                );

                drawings.Add(drawing);

                Thread.Sleep(20);
            }

            return drawings;
        }

        #endregion

        #region Helpers

        private static void ReHidePromotedEntities(List<Bend> bends)
        {
            if (bends == null)
                return;
            foreach (var bend in bends)
            {
                if (bend.SourceEntity != null)
                    bend.SourceEntity.IsVisible = false;
            }
        }

        private static void ReHideSuppressedEntities(FileListItem item)
        {
            if (item.SuppressedEntityIds == null || item.SuppressedEntityIds.Count == 0)
                return;

            foreach (var entity in item.Entities)
            {
                if (item.SuppressedEntityIds.Contains(entity.Id))
                    entity.IsVisible = false;
            }

            // If all entities on a layer are suppressed, uncheck the layer too
            var layerGroups = item.Entities.Where(e => e.Layer != null).GroupBy(e => e.Layer);

            foreach (var group in layerGroups)
            {
                if (group.All(e => !e.IsVisible))
                    group.Key.IsVisible = false;
            }
        }

        private static void ReHideExcludedEntities(FileListItem item)
        {
            foreach (var entity in item.Entities)
            {
                if (item.ExcludedEntityIds.Contains(entity.Id))
                    entity.IsVisible = false;
            }
        }

        // Suppressed entities that are not explained by a wholly hidden layer were excluded one by
        // one (or by a color/line type filter, which is not restored); keep them excluded so a
        // filter change does not bring them back.
        private static HashSet<Guid> IndividuallySuppressed(List<Entity> entities, HashSet<Guid> suppressed)
        {
            var result = new HashSet<Guid>();
            if (suppressed.Count == 0)
                return result;
            foreach (var layer in entities.GroupBy(e => e.Layer?.Name))
            {
                if (layer.All(e => suppressed.Contains(e.Id)))
                    continue;
                result.UnionWith(layer.Where(e => suppressed.Contains(e.Id)).Select(e => e.Id));
            }
            return result;
        }

        private void OnEntityActionRequested(object sender, EntityActionEventArgs e)
        {
            var item = CurrentItem;
            if (item == null || e.Entities.Count == 0)
                return;

            switch (e.Action)
            {
                case EntityAction.AddRadiusDimension:
                case EntityAction.AddDiameterDimension:
                    AddRadialDimensions(item, e.Entities,
                        e.Action == EntityAction.AddRadiusDimension ? DimensionKind.Radius : DimensionKind.Diameter);
                    return;

                case EntityAction.Exclude:
                    foreach (var entity in e.Entities)
                        item.ExcludedEntityIds.Add(entity.Id);
                    entityView1.ClearSelection();
                    break;

                case EntityAction.Include:
                    foreach (var entity in e.Entities)
                        item.ExcludedEntityIds.Remove(entity.Id);
                    break;

                default:
                    var role = e.Action switch
                    {
                        EntityAction.Etch => EntityRole.Etch,
                        EntityAction.Display => EntityRole.Display,
                        _ => EntityRole.Cut,
                    };
                    foreach (var entity in e.Entities)
                    {
                        item.ExcludedEntityIds.Remove(entity.Id);
                        if (role == EntityRoles.Default(entity))
                            item.EntityRoles.Remove(entity.Id);
                        else
                            item.EntityRoles[entity.Id] = role;
                    }
                    break;
            }

            OnFilterChanged(this, EventArgs.Empty);
            entityView1.Invalidate();
        }

        private static void SyncSuppressedState(FileListItem item)
        {
            var bendSources = new HashSet<Entity>(
                (item.Bends ?? new List<Bend>())
                    .Where(b => b.SourceEntity != null)
                    .Select(b => b.SourceEntity)
            );

            var suppressed = item
                .Entities.Where(e => !(e.Layer.IsVisible && e.IsVisible))
                .Where(e => !bendSources.Contains(e))
                .Select(e => e.Id);

            item.SuppressedEntityIds = new HashSet<Guid>(suppressed);

            if (item.SuppressedEntityIds.Count == 0)
                item.SuppressedEntityIds = null;
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
            catch
            {
                return false;
            }
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

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

        internal List<CadText> ExtractTexts(ACadSharp.CadDocument doc)
        {
            var texts = new List<CadText>();
            if (doc == null)
                return texts;

            foreach (var entity in doc.Entities)
            {
                switch (entity)
                {
                    case ACadSharp.Entities.MText mtext:
                        var (mh, mv) = MapAttachmentPoint(mtext.AttachmentPoint);
                        texts.Add(
                            new CadText
                            {
                                SourceHandle = mtext.Handle,
                                Position = new Vector(mtext.InsertPoint.X, mtext.InsertPoint.Y),
                                Value = ReplaceControlCodes(StripMTextFormatting(mtext.Value)),
                                Height = mtext.Height,
                                Rotation = mtext.Rotation,
                                LayerName = mtext.Layer?.Name,
                                HAlign = mh,
                                VAlign = mv,
                            }
                        );
                        break;

                    case ACadSharp.Entities.TextEntity text:
                        var useAlignment =
                            text.HorizontalAlignment != 0 || text.VerticalAlignment != 0;
                        var pt = useAlignment ? text.AlignmentPoint : text.InsertPoint;
                        var ha = text.HorizontalAlignment switch
                        {
                            ACadSharp.Entities.TextHorizontalAlignment.Center => System
                                .Drawing
                                .StringAlignment
                                .Center,
                            ACadSharp.Entities.TextHorizontalAlignment.Right => System
                                .Drawing
                                .StringAlignment
                                .Far,
                            _ => System.Drawing.StringAlignment.Near,
                        };
                        var va = text.VerticalAlignment switch
                        {
                            ACadSharp.Entities.TextVerticalAlignmentType.Middle => System
                                .Drawing
                                .StringAlignment
                                .Center,
                            ACadSharp.Entities.TextVerticalAlignmentType.Top => System
                                .Drawing
                                .StringAlignment
                                .Near,
                            ACadSharp.Entities.TextVerticalAlignmentType.Bottom => System
                                .Drawing
                                .StringAlignment
                                .Far,
                            _ => System.Drawing.StringAlignment.Far,
                        };
                        texts.Add(
                            new CadText
                            {
                                SourceHandle = text.Handle,
                                Position = new Vector(pt.X, pt.Y),
                                Value = ReplaceControlCodes(text.Value),
                                Height = text.Height,
                                Rotation = text.Rotation,
                                LayerName = text.Layer?.Name,
                                HAlign = ha,
                                VAlign = va,
                            }
                        );
                        break;
                }
            }

            return texts;
        }

        private static (
            System.Drawing.StringAlignment h,
            System.Drawing.StringAlignment v
        ) MapAttachmentPoint(ACadSharp.Entities.AttachmentPointType apt)
        {
            var h = apt switch
            {
                ACadSharp.Entities.AttachmentPointType.TopCenter
                or ACadSharp.Entities.AttachmentPointType.MiddleCenter
                or ACadSharp.Entities.AttachmentPointType.BottomCenter => System
                    .Drawing
                    .StringAlignment
                    .Center,
                ACadSharp.Entities.AttachmentPointType.TopRight
                or ACadSharp.Entities.AttachmentPointType.MiddleRight
                or ACadSharp.Entities.AttachmentPointType.BottomRight => System
                    .Drawing
                    .StringAlignment
                    .Far,
                _ => System.Drawing.StringAlignment.Near,
            };
            var v = apt switch
            {
                ACadSharp.Entities.AttachmentPointType.MiddleLeft
                or ACadSharp.Entities.AttachmentPointType.MiddleCenter
                or ACadSharp.Entities.AttachmentPointType.MiddleRight => System
                    .Drawing
                    .StringAlignment
                    .Center,
                ACadSharp.Entities.AttachmentPointType.BottomLeft
                or ACadSharp.Entities.AttachmentPointType.BottomCenter
                or ACadSharp.Entities.AttachmentPointType.BottomRight => System
                    .Drawing
                    .StringAlignment
                    .Far,
                _ => System.Drawing.StringAlignment.Near,
            };
            return (h, v);
        }

        internal virtual string StripMTextFormatting(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var result = MTextFormattingPattern.Replace(text, "");
            result = result.Replace("{", "").Replace("}", "");
            return result.Trim();
        }

        private static string ReplaceControlCodes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return text.Replace("%%p", "±")
                .Replace("%%P", "±")
                .Replace("%%d", "°")
                .Replace("%%D", "°")
                .Replace("%%c", "⌀")
                .Replace("%%C", "⌀")
                .Replace("%%%", "%");
        }

        private void filterPanel_Paint(object sender, PaintEventArgs e) { }
    }
}
