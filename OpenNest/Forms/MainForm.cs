using OpenNest.Actions;
using OpenNest.Collections;
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

            if (GpuEvaluatorFactory.GpuAvailable)
                BestFitCache.CreateSlideComputer = () => GpuEvaluatorFactory.CreateSlideComputer();

            var enginesDir = Path.Combine(Application.StartupPath, "Engines");
            NestEngineRegistry.LoadPlugins(enginesDir);

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
            var seq = ToBase36(id).PadLeft(3, '0');

            return $"N{year}-{seq}";
        }

        private static string ToBase36(int value)
        {
            const string chars = "2345679ACDEFGHJKLMNPQRSTUVWXYZ";
            if (value == 0) return "0";

            var result = "";
            while (value > 0)
            {
                result = chars[value % 36] + result;
                value /= 36;
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
                mnuNestPreviousPlate.Enabled = !activeForm.IsFirstPlate();
                mnuNestNextPlate.Enabled = !activeForm.IsLastPlate();
                mnuNestFirstPlate.Enabled = activeForm.PlateCount > 0 && !activeForm.IsFirstPlate();
                mnuNestLastPlate.Enabled = activeForm.PlateCount > 0 && !activeForm.IsLastPlate();
            }
        }

        private void SetNestingLockout(bool locked)
        {
            nestingInProgress = locked;

            // Disable nesting-related menus while running
            mnuNest.Enabled = !locked;
            mnuPlate.Enabled = !locked;

            // Lock plate navigation
            mnuNestPreviousPlate.Enabled = !locked && activeForm != null && !activeForm.IsFirstPlate();
            mnuNestNextPlate.Enabled = !locked && activeForm != null && !activeForm.IsLastPlate();
            mnuNestFirstPlate.Enabled = !locked && activeForm != null && activeForm.PlateCount > 0 && !activeForm.IsFirstPlate();
            mnuNestLastPlate.Enabled = !locked && activeForm != null && activeForm.PlateCount > 0 && !activeForm.IsLastPlate();
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
                activeForm.CurrentPlateIndex + 1,
                activeForm.PlateCount);

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

                    activeForm.LoadLastPlate();
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
            activeForm.LoadFirstPlate();
        }

        private void LoadLastPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.LoadLastPlate();
        }

        private void LoadPreviousPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.LoadPreviousPlate();
        }

        private void LoadNextPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.LoadNextPlate();
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
            var tiered = finder.FindTieredRemnants(minDim);

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

            remnantViewer.LoadRemnants(tiered, activeForm.PlateView);
            remnantViewer.Show();
            remnantViewer.BringToFront();
        }

        private async void RunAutoNest_Click(object sender, EventArgs e)
        {
            var form = new AutoNestForm(activeForm.Nest);
            form.AllowPlateCreation = true;

            if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            var items = form.GetNestItems();

            if (!items.Any(it => it.Quantity > 0))
                return;

            nestingCts = new CancellationTokenSource();
            var token = nestingCts.Token;

            var progressForm = new NestProgressForm(nestingCts, showPlateRow: true);

            var progress = new Progress<NestProgress>(p =>
            {
                progressForm.UpdateProgress(p);

                if (p.IsOverallBest)
                    activeForm.PlateView.SetStationaryParts(p.BestParts);
                else
                    activeForm.PlateView.SetActiveParts(p.BestParts);

                activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
            });

            progressForm.Show(this);
            SetNestingLockout(true);

            try
            {
                var maxPlates = 100;

                for (var plateCount = 0; plateCount < maxPlates; plateCount++)
                {
                    var remaining = items.Where(i => i.Quantity > 0).ToList();

                    if (remaining.Count == 0)
                        break;

                    if (token.IsCancellationRequested)
                        break;

                    var plate = activeForm.PlateView.Plate.Parts.Count > 0
                        ? activeForm.Nest.CreatePlate()
                        : activeForm.PlateView.Plate;

                    if (plate != activeForm.PlateView.Plate)
                        activeForm.LoadLastPlate();

                    var anyPlaced = false;

                    var engine = NestEngineRegistry.Create(plate);
                    engine.PlateNumber = plateCount;

                    var nestParts = await Task.Run(() =>
                        engine.Nest(remaining, progress, token));

                    activeForm.PlateView.ClearPreviewParts();

                    if (nestParts.Count > 0 && (!token.IsCancellationRequested || progressForm.Accepted))
                    {
                        plate.Parts.AddRange(nestParts);
                        activeForm.PlateView.Invalidate();
                        anyPlaced = true;
                    }

                    if (!anyPlaced)
                        break;
                }

                activeForm.Nest.UpdateDrawingQuantities();
                progressForm.ShowCompleted();
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

        #endregion Nest Menu Events

        #region Plate Menu Events

        private void SetAsNestDefault_Click(object sender, EventArgs e)
        {
            if (activeForm == null) return;
            activeForm.SetCurrentPlateAsNestDefault();
        }

        private async void FillPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            if (activeForm.Nest.Drawings.Count == 0)
                return;

            var form = new FillPlateForm(activeForm.Nest.Drawings);
            form.ShowDialog();

            var drawing = form.SelectedDrawing;

            if (drawing == null)
                return;

            nestingCts = new CancellationTokenSource();
            var token = nestingCts.Token;

            var progressForm = new NestProgressForm(nestingCts, showPlateRow: false);

            var progress = new Progress<NestProgress>(p =>
            {
                progressForm.UpdateProgress(p);

                if (p.IsOverallBest)
                    activeForm.PlateView.SetStationaryParts(p.BestParts);
                else
                    activeForm.PlateView.SetActiveParts(p.BestParts);

                activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
            });

            progressForm.Show(this);
            SetNestingLockout(true);

            try
            {
                var plate = activeForm.PlateView.Plate;
                var engine = NestEngineRegistry.Create(plate);

                var parts = await Task.Run(() =>
                    engine.Fill(new NestItem { Drawing = drawing },
                        plate.WorkArea(), progress, token));

                if (parts.Count > 0)
                    activeForm.PlateView.AcceptPreviewParts(parts);
                else
                    activeForm.PlateView.ClearPreviewParts();

                progressForm.ShowCompleted();
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

        private void FillArea_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            if (activeForm.Nest.Drawings.Count == 0)
                return;

            var form = new FillPlateForm(activeForm.Nest.Drawings);
            form.ShowDialog();

            var drawing = form.SelectedDrawing;

            if (drawing == null)
                return;

            nestingCts = new CancellationTokenSource();

            var progressForm = new NestProgressForm(nestingCts, showPlateRow: false);

            var progress = new Progress<NestProgress>(p =>
            {
                progressForm.UpdateProgress(p);

                if (p.IsOverallBest)
                    activeForm.PlateView.SetStationaryParts(p.BestParts);
                else
                    activeForm.PlateView.SetActiveParts(p.BestParts);

                activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
            });

            Action<List<Part>> onComplete = parts =>
            {
                if (parts != null && parts.Count > 0)
                    activeForm.PlateView.AcceptPreviewParts(parts);
                else
                    activeForm.PlateView.ClearPreviewParts();

                activeForm.PlateView.ActiveWorkArea = null;
                progressForm.Close();
                SetNestingLockout(false);
                nestingCts.Dispose();
                nestingCts = null;
            };

            progressForm.Show(this);
            SetNestingLockout(true);

            activeForm.PlateView.SetAction(typeof(ActionFillArea),
                drawing, progress, nestingCts, onComplete);
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
