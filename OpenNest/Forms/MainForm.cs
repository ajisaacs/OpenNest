using OpenNest.Actions;
using OpenNest.Collections;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Gpu;
using OpenNest.IO;
using OpenNest.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class MainForm : Form
    {
        private EditNestForm activeForm;
        private bool clickUpdateLocation;
        private bool nestingInProgress;
        private CancellationTokenSource nestingCts;

        private const float ZoomInFactor = 1.5f;
        private const float ZoomOutFactor = 1.0f / ZoomInFactor;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && activeForm?.PlateView != null)
            {
                activeForm.PlateView.ProcessEscapeKey();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public MainForm()
        {
            InitializeComponent();
            LoadSettings();

            var renderer = new ToolStripRenderer(ToolbarTheme.Toolbar);
            menuStrip1.Renderer = renderer;
            toolStrip1.Renderer = renderer;

            mnuViewZoomIn.ShortcutKeyDisplayString = "Ctrl+Plus";
            mnuViewZoomOut.ShortcutKeyDisplayString = "Ctrl+Minus";

            clickUpdateLocation = true;

            this.SetBevel(false);

            LoadPosts();
            EnableCheck();
            UpdateStatus();
            UpdateGpuStatus();

            //if (GpuEvaluatorFactory.GpuAvailable)
            //    BestFitCache.CreateEvaluator = (drawing, spacing) => GpuEvaluatorFactory.Create(drawing, spacing);

            //if (GpuEvaluatorFactory.GpuAvailable)
            //    BestFitCache.CreateSlideComputer = () => GpuEvaluatorFactory.CreateSlideComputer();

            var enginesDir = Path.Combine(Application.StartupPath, "Engines");
            NestEngineRegistry.LoadPlugins(enginesDir);

            OptionsForm.ApplyDisabledStrategies();

            foreach (var engine in NestEngineRegistry.AvailableEngines)
                engineComboBox.Items.Add(engine.Name);

            engineComboBox.SelectedItem = NestEngineRegistry.ActiveEngineName;
            engineComboBox.SelectedIndexChanged += EngineComboBox_SelectedIndexChanged;
        }

        private Nest CreateDefaultNest()
        {
            var nest = new Nest();
            nest.Units = Properties.Settings.Default.DefaultUnit;
            nest.PlateDefaults.EdgeSpacing = new Spacing(1, 1, 1, 1);
            nest.PlateDefaults.PartSpacing = 1;
            nest.PlateDefaults.Size = new OpenNest.Geometry.Size(100, 100);
            nest.PlateDefaults.Quadrant = 1;
            return nest;
        }

        private string GetNestName(DateTime date, int id)
        {
            var year = (date.Year % 100).ToString("D2");
            var seq = ToBase36(id).PadLeft(3, '2');

            return $"N{year}-{seq}";
        }

        private static string ToBase36(int value)
        {
            const string chars = "2345679ACDEFGHJKLMNPQRSTUVWXYZ";
            if (value == 0) return chars[0].ToString();

            var result = "";
            while (value > 0)
            {
                result = chars[value % chars.Length] + result;
                value /= chars.Length;
            }
            return result;
        }

        private void LoadNest(Nest nest, FormWindowState windowState = FormWindowState.Maximized)
        {
            var editForm = new EditNestForm(nest);
            editForm.MdiParent = this;
            editForm.PlateChanged += (sender, e) =>
            {
                NavigationEnableCheck();
                UpdatePlateStatus();
                mnuPlateRemove.Enabled = activeForm?.PlateManager.CanRemoveCurrent ?? false;
            };
            editForm.WindowState = windowState;
            editForm.Show();
            editForm.PlateView.ZoomToFit();
        }

        private void LoadSettings()
        {
            var screen = Screen.PrimaryScreen.Bounds;

            if (screen.Contains(Settings.Default.MainWindowLocation))
                Location = Settings.Default.MainWindowLocation;

            if (Settings.Default.MainWindowSize.Width <= screen.Width &&
                Settings.Default.MainWindowSize.Height <= screen.Height)
            {
                Size = Settings.Default.MainWindowSize;
            }

            WindowState = Settings.Default.MainWindowState;
        }

        private void SaveSettings()
        {
            Settings.Default.MainWindowLocation = Location;
            Settings.Default.MainWindowSize = Size;
            Settings.Default.MainWindowState = WindowState;
            Settings.Default.Save();
        }

        private void EnableCheck()
        {
            NavigationEnableCheck();

            var hasValue = activeForm != null;

            btnZoomToFit.Enabled = hasValue;
            mnuFileSave.Enabled = hasValue;
            btnSave.Enabled = hasValue;
            btnSaveAs.Enabled = hasValue;
            mnuFileSaveAs.Enabled = hasValue;
            mnuFileExport.Enabled = hasValue;
            mnuFileExportAll.Enabled = hasValue;
            btnZoomOut.Enabled = hasValue;
            btnZoomIn.Enabled = hasValue;
            mnuEdit.Visible = hasValue;
            mnuView.Visible = hasValue;
            mnuNest.Visible = hasValue;
            mnuPlate.Visible = hasValue;
            mnuWindow.Visible = hasValue;
            mnuToolsAlign.Visible = hasValue;
            mnuToolsMeasureArea.Visible = hasValue;

            toolStripMenuItem14.Visible = hasValue;
            mnuSetOffsetIncrement.Visible = hasValue;
            mnuSetRotationIncrement.Visible = hasValue;
            toolStripMenuItem15.Visible = hasValue;
        }

        private void NavigationEnableCheck()
        {
            if (activeForm == null)
            {
                mnuNestPreviousPlate.Enabled = false;
                mnuNestNextPlate.Enabled = false;
                mnuNestFirstPlate.Enabled = false;
                mnuNestLastPlate.Enabled = false;
            }
            else
            {
                mnuNestPreviousPlate.Enabled = !activeForm.PlateManager.IsFirst;
                mnuNestNextPlate.Enabled = !activeForm.PlateManager.IsLast;
                mnuNestFirstPlate.Enabled = activeForm.PlateManager.Count > 0 && !activeForm.PlateManager.IsFirst;
                mnuNestLastPlate.Enabled = activeForm.PlateManager.Count > 0 && !activeForm.PlateManager.IsLast;
            }
        }

        private void SetNestingLockout(bool locked)
        {
            nestingInProgress = locked;

            // Disable nesting-related menus while running
            mnuNest.Enabled = !locked;
            mnuPlate.Enabled = !locked;

            // Lock plate navigation
            mnuNestPreviousPlate.Enabled = !locked && activeForm != null && !activeForm.PlateManager.IsFirst;
            mnuNestNextPlate.Enabled = !locked && activeForm != null && !activeForm.PlateManager.IsLast;
            mnuNestFirstPlate.Enabled = !locked && activeForm != null && activeForm.PlateManager.Count > 0 && !activeForm.PlateManager.IsFirst;
            mnuNestLastPlate.Enabled = !locked && activeForm != null && activeForm.PlateManager.Count > 0 && !activeForm.PlateManager.IsLast;
        }

        private void UpdateLocationStatus()
        {
            if (activeForm == null)
            {
                locationStatusLabel.Text = string.Empty;
                return;
            }

            locationStatusLabel.Text = string.Format("Location: [{0}, {1}]",
                activeForm.PlateView.CurrentPoint.X.ToString("n4"),
                activeForm.PlateView.CurrentPoint.Y.ToString("n4"));
        }

        private void UpdatePlateStatus()
        {
            if (activeForm == null)
            {
                plateIndexStatusLabel.Text = string.Empty;
                plateSizeStatusLabel.Text = string.Empty;
                plateQtyStatusLabel.Text = string.Empty;
                plateUtilStatusLabel.Text = string.Empty;
                return;
            }

            plateIndexStatusLabel.Text = string.Format(
                "Plate: {0} of {1}",
                activeForm.PlateManager.CurrentIndex + 1,
                activeForm.PlateManager.Count);

            plateSizeStatusLabel.Text = string.Format(
                "Size: {0}",
                activeForm.PlateView.Plate.Size);

            plateQtyStatusLabel.Text = string.Format(
                "Qty: {0}",
                activeForm.PlateView.Plate.Quantity);

            plateUtilStatusLabel.Text = string.Format(
                "Util: {0:P1}",
                activeForm.PlateView.Plate.Utilization());
        }

        private void UpdateSelectionStatus()
        {
            if (activeForm == null || activeForm.PlateView.SelectedParts.Count == 0)
            {
                selectionStatusLabel.Text = string.Empty;
                return;
            }

            var selected = activeForm.PlateView.SelectedParts;

            if (selected.Count == 1)
            {
                var box = selected[0].BoundingBox;
                selectionStatusLabel.Text = string.Format("Selected: [{0}, {1}] {2} x {3}",
                    box.X.ToString("n4"), box.Y.ToString("n4"),
                    box.Width.ToString("n4"), box.Length.ToString("n4"));
            }
            else
            {
                var bounds = selected.Select(p => p.BasePart).ToList().GetBoundingBox();
                selectionStatusLabel.Text = string.Format("Selected ({0}): [{1}, {2}] {3} x {4}",
                    selected.Count,
                    bounds.X.ToString("n4"), bounds.Y.ToString("n4"),
                    bounds.Width.ToString("n4"), bounds.Length.ToString("n4"));
            }
        }

        private void UpdateStatus()
        {
            UpdateLocationStatus();
            UpdatePlateStatus();
            UpdateSelectionStatus();
        }

        private void UpdateGpuStatus()
        {
            if (GpuEvaluatorFactory.GpuAvailable)
            {
                gpuStatusLabel.Text = $"GPU : {GpuEvaluatorFactory.DeviceName}";
                gpuStatusLabel.ForeColor = Color.DarkGreen;
            }
            else
            {
                gpuStatusLabel.Text = "GPU : None (CPU)";
                gpuStatusLabel.ForeColor = Color.Gray;
            }
        }

        private void EngineComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (engineComboBox.SelectedItem is string name)
                NestEngineRegistry.ActiveEngineName = name;
        }

        private void UpdateLocationMode()
        {
            if (activeForm == null)
                return;

            if (clickUpdateLocation)
            {
                clickUpdateLocation = true;
                locationStatusLabel.ForeColor = Color.Gray;
                activeForm.PlateView.MouseMove -= PlateView_MouseMove;
                activeForm.PlateView.MouseClick += PlateView_MouseClick;
            }
            else
            {
                clickUpdateLocation = false;
                locationStatusLabel.ForeColor = Color.Black;
                activeForm.PlateView.MouseMove += PlateView_MouseMove;
                activeForm.PlateView.MouseClick -= PlateView_MouseClick;
            }
        }

        private void LoadPosts()
        {
            var exepath = Assembly.GetEntryAssembly().Location;
            var postprocessordir = Path.Combine(Path.GetDirectoryName(exepath), "Posts");

            if (Directory.Exists(postprocessordir))
            {
                foreach (var file in Directory.GetFiles(postprocessordir, "*.dll"))
                {
                    var types = Assembly.LoadFile(file).GetTypes();

                    foreach (var type in types)
                    {
                        if (type.GetInterfaces().Contains(typeof(IPostProcessor)) == false)
                            continue;

                        var postProcessor = Activator.CreateInstance(type) as IPostProcessor;
                        var postProcessorMenuItem = new ToolStripMenuItem(postProcessor.Name);
                        postProcessorMenuItem.Tag = postProcessor;
                        postProcessorMenuItem.Click += PostProcessor_Click;
                        mnuNestPost.DropDownItems.Add(postProcessorMenuItem);
                    }
                }
            }

            mnuNestPost.Visible = mnuNestPost.DropDownItems.Count > 0;
        }

        #region Overrides

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            this.Refresh();
        }

        protected override void OnMdiChildActivate(EventArgs e)
        {
            base.OnMdiChildActivate(e);

            if (activeForm != null)
            {
                activeForm.PlateView.MouseMove -= PlateView_MouseMove;
                activeForm.PlateView.MouseClick -= PlateView_MouseClick;
                activeForm.PlateView.StatusChanged -= PlateView_StatusChanged;
                activeForm.PlateView.SelectionChanged -= PlateView_SelectionChanged;
                activeForm.PlateView.PartAdded -= PlateView_PartAdded;
                activeForm.PlateView.PartRemoved -= PlateView_PartRemoved;
            }

            // If nesting is in progress and the active form changed, cancel nesting
            if (nestingInProgress && nestingCts != null)
            {
                nestingCts.Cancel();
            }

            activeForm = ActiveMdiChild as EditNestForm;

            EnableCheck();
            UpdatePlateStatus();
            UpdateLocationStatus();

            if (activeForm == null)
            {
                statusLabel1.Text = "";
                selectionStatusLabel.Text = "";
                return;
            }

            UpdateLocationMode();
            UpdateSelectionStatus();
            activeForm.PlateView.StatusChanged += PlateView_StatusChanged;
            activeForm.PlateView.SelectionChanged += PlateView_SelectionChanged;
            activeForm.PlateView.PartAdded += PlateView_PartAdded;
            activeForm.PlateView.PartRemoved += PlateView_PartRemoved;
            mnuViewDrawRapids.Checked = activeForm.PlateView.DrawRapid;
            mnuViewDrawPiercePoints.Checked = activeForm.PlateView.DrawPiercePoints;
            mnuViewDrawBounds.Checked = activeForm.PlateView.DrawBounds;
            mnuViewDrawCutDirection.Checked = activeForm.PlateView.DrawCutDirection;
            statusLabel1.Text = activeForm.PlateView.Status;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (Settings.Default.CreateNewNestOnOpen)
                New_Click(this, new EventArgs());
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            SaveSettings();
        }

        #endregion

        #region File Menu Events

        private void New_Click(object sender, EventArgs e)
        {
            var windowState = ActiveMdiChild != null
                ? ActiveMdiChild.WindowState
                : FormWindowState.Maximized;

            Nest nest;

            if (File.Exists(Properties.Settings.Default.NestTemplatePath))
            {
                try
                {
                    var reader = new NestReader(Properties.Settings.Default.NestTemplatePath);
                    nest = reader.Read();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to load nest template:\n{ex.Message}\n\nA default nest will be created instead.",
                        "Template Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    nest = CreateDefaultNest();
                }
            }
            else
            {
                nest = CreateDefaultNest();
            }

            nest.DateCreated = DateTime.Now;
            nest.DateLastModified = DateTime.Now;

            if (DateTime.Now.Date != Settings.Default.LastNestCreatedDate.Date)
            {
                Settings.Default.NestNumber = 1;
                Settings.Default.LastNestCreatedDate = DateTime.Now.Date;
            }

            nest.Name = GetNestName(DateTime.Now, Settings.Default.NestNumber++);
            LoadNest(nest, windowState);
        }

        private void Open_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = NestFormat.FileFilter;
            dlg.Multiselect = true;

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var reader = new NestReader(dlg.FileName);
                var nest = reader.Read();
                LoadNest(nest);
            }
        }

        private void ImportBom_Click(object sender, EventArgs e)
        {
            var form = new BomImportForm();
            form.MdiParentForm = this;
            form.ShowDialog(this);
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.Save();
        }

        private void SaveAs_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.SaveAs();
        }

        private void Export_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.Export();
        }

        private void ExportAll_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.ExportAll();
        }

        private void Exit_Click(object sender, EventArgs e)
        {
            Close();
        }

        #endregion File Menu Events

        #region Edit Menu Events

        private void EditCut_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;

            var selectedParts = activeForm.PlateView.SelectedParts;

            if (selectedParts.Count > 0)
            {
                var partsToClone = selectedParts.Select(p => p.BasePart).ToList();
                activeForm.PlateView.SetAction(typeof(ActionClone), partsToClone);

                foreach (var part in partsToClone)
                    activeForm.PlateView.Plate.Parts.Remove(part);
            }
        }

        private void EditPaste_Click(object sender, EventArgs e)
        {
        }

        private void EditCopy_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;

            var selectedParts = activeForm.PlateView.SelectedParts;

            if (selectedParts.Count > 0)
            {
                var partsToClone = selectedParts.Select(p => p.BasePart).ToList();
                activeForm.PlateView.SetAction(typeof(ActionClone), partsToClone);
            }
        }

        private void EditSelectAll_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.SelectAllParts();
        }

        #endregion Edit Menu Events

        #region View Menu Events

        private void ToggleDrawRapids_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.ToggleRapid();
            mnuViewDrawRapids.Checked = activeForm.PlateView.DrawRapid;
        }

        private void ToggleDrawPiercePoints_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.TogglePiercePoints();
            mnuViewDrawPiercePoints.Checked = activeForm.PlateView.DrawPiercePoints;
        }

        private void ToggleDrawBounds_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.ToggleDrawBounds();
            mnuViewDrawBounds.Checked = activeForm.PlateView.DrawBounds;
        }

        private void ToggleDrawOffset_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.ToggleDrawOffset();
            mnuViewDrawOffset.Checked = activeForm.PlateView.DrawOffset;
        }

        private void ToggleDrawCutDirection_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.ToggleCutDirection();
            mnuViewDrawCutDirection.Checked = activeForm.PlateView.DrawCutDirection;
        }

        private void ZoomToArea_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.SetAction(typeof(ActionZoomWindow));
        }

        private void ZoomToFit_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.ZoomToFit();
        }

        private void ZoomToPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.ZoomToPlate();
        }

        private void ZoomToSelected_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.ZoomToSelected();
        }

        private void ZoomIn_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;

            var pt = new Point(
                activeForm.PlateView.Width / 2,
                activeForm.PlateView.Height / 2);

            activeForm.PlateView.ZoomToControlPoint(pt, ZoomInFactor);
        }

        private void ZoomOut_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;

            var pt = new Point(
                activeForm.PlateView.Width / 2,
                activeForm.PlateView.Height / 2);

            activeForm.PlateView.ZoomToControlPoint(pt, ZoomOutFactor);
        }

        #endregion View Menu Events

        #region Tools Menu Events

        private void MeasureArea_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.PlateView.SetAction(typeof(ActionSelectArea));
        }

        private void BestFitViewer_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var plate = activeForm.PlateView.Plate;
            var drawings = activeForm.Nest.Drawings;

            if (drawings.Count == 0)
            {
                MessageBox.Show("No drawings available.", "Best-Fit Viewer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new BestFitViewerForm(drawings, plate, activeForm.Nest.Units))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.SelectedResult != null)
                {
                    var parts = form.SelectedResult.BuildParts(form.SelectedDrawing);
                    activeForm.PlateView.SetAction(typeof(ActionClone), parts);
                }
            }
        }

        private void PatternTile_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            if (activeForm.Nest.Drawings.Count == 0)
            {
                MessageBox.Show("No drawings available.", "Pattern Tile",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new PatternTileForm(activeForm.Nest))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.Result == null)
                    return;

                var result = form.Result;

                if (result.Target == PatternTileTarget.CurrentPlate)
                {
                    activeForm.PlateView.Plate.Parts.Clear();

                    foreach (var part in result.Parts)
                        activeForm.PlateView.Plate.Parts.Add(part);

                    activeForm.PlateView.ZoomToFit();
                }
                else
                {
                    var plate = activeForm.Nest.CreatePlate();
                    plate.Size = result.PlateSize;

                    foreach (var part in result.Parts)
                        plate.Parts.Add(part);

                    activeForm.PlateManager.LoadLast();
                }

                activeForm.Nest.UpdateDrawingQuantities();
            }
        }

        private void SetOffsetIncrement_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            var form = new SetValueForm();
            form.Text = "Set Offset Increment";
            form.Value = activeForm.PlateView.OffsetIncrementDistance;

            if (form.ShowDialog() == DialogResult.OK)
                activeForm.PlateView.OffsetIncrementDistance = form.Value;
        }

        private void SetRotationIncrement_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            var form = new SetValueForm();
            form.Text = "Set Rotation Increment";
            form.Value = activeForm.PlateView.RotateIncrementAngle;
            form.Maximum = 360;

            if (form.ShowDialog() == DialogResult.OK)
                activeForm.PlateView.RotateIncrementAngle = form.Value;
        }

        private void Options_Click(object sender, EventArgs e)
        {
            var form = new OptionsForm();
            form.ShowDialog();
        }

        private void MachineConfig_Click(object sender, EventArgs e)
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenNest", "Machines");
            var provider = new LocalJsonProvider(appDataPath);
            provider.EnsureDefaults();
            using (var form = new MachineConfigForm(provider))
            {
                form.ShowDialog(this);
            }
        }

        private void AlignLeft_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.AlignSelected(AlignType.Left);
        }

        private void AlignRight_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.AlignSelected(AlignType.Right);
        }

        private void AlignTop_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.AlignSelected(AlignType.Top);
        }

        private void AlignBottom_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.AlignSelected(AlignType.Bottom);
        }

        private void AlignVertical_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.AlignSelected(AlignType.Vertically);
        }

        private void AlignHorizontal_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.AlignSelected(AlignType.Horizontally);
        }

        private void EvenlySpaceHorizontally_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.AlignSelected(AlignType.EvenlySpaceHorizontally);
        }

        private void EvenlySpaceVertically_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateView.AlignSelected(AlignType.EvenlySpaceVertically);
        }

        #endregion Tools Menu Events

        #region Nest Menu Events

        private void Import_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.Import();
        }

        private void ShapeLibrary_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;

            var form = new ShapeLibraryForm();
            form.ShowDialog();

            var drawings = form.GetDrawings();
            if (drawings.Count == 0) return;

            drawings.ForEach(d => activeForm.Nest.Drawings.Add(d));
            activeForm.UpdateDrawingList();
        }

        private void EditNest_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.ShowNestInfoEditor();
        }

        private void RemoveEmptyPlates_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.Nest.Plates.RemoveEmptyPlates();
        }

        private void LoadFirstPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateManager.LoadFirst();
        }

        private void LoadLastPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateManager.LoadLast();
        }

        private void LoadPreviousPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateManager.LoadPrevious();
        }

        private void LoadNextPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlateManager.LoadNext();
        }

        private RemnantViewerForm remnantViewer;

        private void ShowRemnants_Click(object sender, EventArgs e)
        {
            if (activeForm?.PlateView?.Plate == null)
                return;

            var plate = activeForm.PlateView.Plate;

            // Minimum remnant dimension = smallest part bbox dimension on the plate.
            var minDim = 0.0;
            var nest = activeForm.Nest;
            if (nest != null)
            {
                foreach (var drawing in nest.Drawings)
                {
                    var bbox = drawing.Program.BoundingBox();
                    var dim = System.Math.Min(bbox.Width, bbox.Length);
                    if (minDim == 0 || dim < minDim)
                        minDim = dim;
                }
            }

            var finder = RemnantFinder.FromPlate(plate);

            if (remnantViewer == null || remnantViewer.IsDisposed)
            {
                remnantViewer = new RemnantViewerForm();
                remnantViewer.Owner = this;

                // Position next to the main form's right edge.
                var screen = Screen.FromControl(this);
                remnantViewer.Location = new Point(
                    System.Math.Min(Right, screen.WorkingArea.Right - remnantViewer.Width),
                    Top);
            }

            remnantViewer.LoadRemnants(finder, minDim, activeForm.PlateView);
            remnantViewer.Show();
            remnantViewer.BringToFront();
        }

        private async void RunAutoNest_Click(object sender, EventArgs e)
        {
            var form = new AutoNestForm(activeForm.Nest);
            form.AllowPlateCreation = true;

            if (activeForm.Nest.PlateOptions.Count > 0)
                form.LoadPlateOptions(activeForm.Nest.PlateOptions, activeForm.Nest.SalvageRate);

            if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            if (form.EngineName != null)
            {
                NestEngineRegistry.ActiveEngineName = form.EngineName;
                engineComboBox.SelectedItem = form.EngineName;
            }

            var items = form.GetNestItems();

            if (!items.Any(it => it.Quantity > 0))
                return;

            var optimizePlateSize = form.OptimizePlateSize;
            var plateOptions = optimizePlateSize ? form.GetPlateOptions() : null;
            var salvageRate = form.SalvageRate;
            var partFirstMode = form.PartFirstMode;
            var sortOrder = form.SortOrder;
            var minRemnantSize = form.MinRemnantSize;
            var allowPlateCreation = form.AllowPlateCreation;

            if (optimizePlateSize)
            {
                activeForm.Nest.PlateOptions = plateOptions;
                activeForm.Nest.SalvageRate = salvageRate;
            }

            nestingCts = new CancellationTokenSource();
            var progressForm = new NestProgressForm(nestingCts, showPlateRow: true);
            progressForm.PreviewPlate = CreatePreviewPlate(activeForm.PlateView.Plate);

            var progress = new Progress<NestProgress>(p =>
            {
                progressForm.UpdateProgress(p);

                if (p.IsOverallBest)
                    progressForm.UpdatePreview(p.BestParts);

                activeForm.PlateView.SetActiveParts(p.BestParts);
                activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
            });

            progressForm.Show(this);
            SetNestingLockout(true);

            try
            {
                await RunAutoNestAsync(items, progressForm, progress, nestingCts.Token,
                    plateOptions, salvageRate, partFirstMode, sortOrder, minRemnantSize, allowPlateCreation);
            }
            catch (Exception ex)
            {
                activeForm.PlateView.ClearPreviewParts();
                MessageBox.Show($"Nesting error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                activeForm.PlateView.ActiveWorkArea = null;
                progressForm.Close();
                SetNestingLockout(false);
                nestingCts.Dispose();
                nestingCts = null;
            }
        }

        private async Task RunAutoNestAsync(
            List<NestItem> items,
            NestProgressForm progressForm,
            IProgress<NestProgress> progress,
            CancellationToken token,
            List<PlateOption> plateOptions = null,
            double salvageRate = 0.5,
            bool partFirstMode = false,
            PartSortOrder sortOrder = PartSortOrder.BoundingBoxArea,
            double minRemnantSize = 12.0,
            bool allowPlateCreation = true)
        {
            if (partFirstMode)
            {
                var existingPlates = new List<Plate>();
                for (var i = 0; i < activeForm.Nest.Plates.Count; i++)
                {
                    var p = activeForm.Nest.Plates[i];
                    if (p.Parts.Count > 0)
                        existingPlates.Add(p);
                }

                var template = activeForm.PlateView.Plate;

                var nestOptions = new MultiPlateNestOptions
                {
                    Template = template,
                    PlateOptions = plateOptions,
                    SalvageRate = salvageRate,
                    SortOrder = sortOrder,
                    MinRemnantSize = minRemnantSize,
                    AllowPlateCreation = allowPlateCreation,
                };

                var result = await Task.Run(() =>
                    MultiPlateNester.Nest(items, nestOptions, existingPlates, progress, token));

                foreach (var pr in result.Plates)
                {
                    if (pr.IsNew)
                    {
                        var plate = GetOrCreatePlate(progressForm);
                        plate.Size = pr.Plate.Size;
                        plate.Parts.AddRange(pr.Parts);
                    }
                }

                activeForm.Nest.UpdateDrawingQuantities();
                progressForm.ShowCompleted();
                return;
            }

            const int maxPlates = 100;

            for (var plateIndex = 0; plateIndex < maxPlates; plateIndex++)
            {
                var remaining = items.Where(i => i.Quantity > 0).ToList();

                if (remaining.Count == 0 || token.IsCancellationRequested)
                    break;

                var plate = GetOrCreatePlate(progressForm);

                var placed = await NestSinglePlateAsync(
                    plate, plateIndex, remaining, progressForm, progress, token,
                    plateOptions, salvageRate);

                if (!placed)
                    break;
            }

            activeForm.Nest.UpdateDrawingQuantities();
            progressForm.ShowCompleted();
        }

        private Plate GetOrCreatePlate(NestProgressForm progressForm)
        {
            var plate = activeForm.PlateManager.GetOrCreateEmpty();
            activeForm.PlateManager.LoadLast();
            progressForm.PreviewPlate = CreatePreviewPlate(plate);
            return plate;
        }

        private async Task<bool> NestSinglePlateAsync(
            Plate plate,
            int plateIndex,
            List<NestItem> items,
            NestProgressForm progressForm,
            IProgress<NestProgress> progress,
            CancellationToken token,
            List<PlateOption> plateOptions = null,
            double salvageRate = 0.5)
        {
            List<Part> nestParts;

            if (plateOptions != null && plateOptions.Count > 0)
            {
                var result = await Task.Run(() =>
                    PlateOptimizer.Optimize(items, plateOptions, salvageRate, plate, progress, token));

                if (result == null || result.Parts.Count == 0 ||
                    (token.IsCancellationRequested && !progressForm.Accepted))
                    return false;

                plate.Size = new Geometry.Size(result.ChosenSize.Width, result.ChosenSize.Length);
                nestParts = result.Parts;

                // Deduct placed quantities — the optimizer clones items internally
                // so the originals are untouched after dry runs.
                foreach (var item in items)
                {
                    var placed = nestParts.Count(p => p.BaseDrawing.Name == item.Drawing.Name);
                    item.Quantity = System.Math.Max(0, item.Quantity - placed);
                }
            }
            else
            {
                var engine = NestEngineRegistry.Create(plate);
                engine.PlateNumber = plateIndex;

                nestParts = await Task.Run(() =>
                    engine.Nest(items, progress, token));
            }

            activeForm.PlateView.ClearPreviewParts();

            if (nestParts.Count == 0 || (token.IsCancellationRequested && !progressForm.Accepted))
                return false;

            plate.Parts.AddRange(nestParts);
            activeForm.PlateView.Invalidate();
            return true;
        }

        private static Plate CreatePreviewPlate(Plate source)
        {
            var plate = new Plate(source.Size)
            {
                Quadrant = source.Quadrant,
                PartSpacing = source.PartSpacing,
            };
            plate.EdgeSpacing = source.EdgeSpacing;
            return plate;
        }

        private void SequenceAllPlates_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.AutoSequenceAllPlates();
        }

        private void PostProcessor_Click(object sender, EventArgs e)
        {
            var menuItem = (ToolStripMenuItem)sender;
            var postProcessor = menuItem.Tag as IPostProcessor;

            if (postProcessor == null)
                return;

            if (postProcessor is IConfigurablePostProcessor configurable)
            {
                using var configForm = new PostProcessorConfigForm(configurable);
                if (configForm.ShowDialog() != DialogResult.OK)
                    return;
            }

            var dialog = new SaveFileDialog();
            dialog.Filter = "CNC File (*.cnc) | *.cnc";
            dialog.FileName = activeForm.Nest.Name;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var path = dialog.FileName;
                postProcessor.Post(activeForm.Nest, path);
            }
        }

        private void CalculateNestCutTime_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.CalculateNestCutTime();
        }

        private void NestAssignLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.AssignLeadInsAllPlates();
        }

        private void NestRemoveLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.RemoveLeadInsAllPlates();
        }

        #endregion Nest Menu Events

        #region Plate Menu Events

        private void SetAsNestDefault_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.SetCurrentPlateAsNestDefault();
        }

        private void AddPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.Nest.CreatePlate();
            NavigationEnableCheck();
        }

        private void EditPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.EditPlate();
        }

        private void RemovePlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.RemoveCurrentPlate();
        }

        private void ResizeToFitParts_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.ResizePlateToFitParts();
            UpdatePlateStatus();
        }

        private void RotateCw_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.RotateCw();
        }

        private void RotateCcw_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.RotateCcw();
        }

        private void Rotate180_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.Rotate180();
        }

        private void OpenInExternalCad_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.OpenCurrentPlate();
        }

        private void CalculatePlateCutTime_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.CalculateCurrentPlateCutTime();
        }

        private void AutoSequenceCurrentPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.AutoSequenceCurrentPlate();
        }

        private void ManualSequenceParts_Click(object sender, EventArgs e)
        {
            if (activeForm == null || activeForm.PlateView.Plate.Parts.Count < 2)
                return;

            activeForm.PlateView.SetAction(typeof(ActionSetSequence));
        }

        private void CutOff_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.PlateView.SetAction(typeof(ActionCutOff));
        }

        private void PlateAssignLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.AssignLeadIns_Click(sender, e);
        }

        private void PlatePlaceLeadIn_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.PlaceLeadIn_Click(sender, e);
        }

        private void PlateRemoveLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.RemoveLeadIns_Click(sender, e);
        }

        #endregion Plate Menu Events

        #region Window Menu Events

        private void TileVertical_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.TileVertical);
        }

        private void TileHorizontal_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.TileHorizontal);
        }

        private void CascadeWindows_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.Cascade);
        }

        private void Close_Click(object sender, EventArgs e)
        {
            if (ActiveMdiChild != null)
                ActiveMdiChild.Close();
        }

        private void CloseAll_Click(object sender, EventArgs e)
        {
            foreach (var mdiChild in MdiChildren)
                mdiChild.Close();
        }

        #endregion Window Menu Events

        #region Statusbar Events

        private void LocationStatusLabel_Click(object sender, EventArgs e)
        {
            clickUpdateLocation = !clickUpdateLocation;
            UpdateLocationMode();
        }

        #endregion Statusbar Events

        #region PlateView Events

        private void PlateView_PartAdded(object sender, ItemAddedEventArgs<Part> e) => UpdatePlateStatus();
        private void PlateView_PartRemoved(object sender, ItemRemovedEventArgs<Part> e) => UpdatePlateStatus();

        private void PlateView_MouseMove(object sender, MouseEventArgs e)
        {
            UpdateLocationStatus();
        }

        private void PlateView_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                UpdateLocationStatus();
        }

        private void PlateView_StatusChanged(object sender, EventArgs e)
        {
            statusLabel1.Text = activeForm.PlateView.Status;
        }

        private void PlateView_SelectionChanged(object sender, EventArgs e)
        {
            UpdateSelectionStatus();
        }

        #endregion PlateView Events

        private void centerPartsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var plateCenter = this.activeForm.PlateView.Plate.BoundingBox(false).Center;
            var partsCenter = this.activeForm.PlateView.Parts.GetBoundingBox().Center;

            var offset = plateCenter - partsCenter;

            foreach (var part in this.activeForm.PlateView.Parts)
            {
                part.Offset(offset);
            }

            activeForm.PlateView.Invalidate();
        }
    }
}
