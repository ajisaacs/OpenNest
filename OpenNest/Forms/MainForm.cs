using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Collections;
using OpenNest.Engine.BestFit;
using OpenNest.Gpu;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    public partial class MainForm : Form
    {
        private EditNestForm activeForm;
        private bool clickUpdateLocation;

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

            if (GpuEvaluatorFactory.GpuAvailable)
                BestFitCache.CreateEvaluator = (drawing, spacing) => GpuEvaluatorFactory.Create(drawing, spacing);
        }

        private string GetNestName(DateTime date, int id)
        {
            var month = date.Month.ToString().PadLeft(2, '0');
            var day = date.Day.ToString().PadLeft(2, '0');

            return string.Format("N{0}{1}-{2}", month, day, id.ToString().PadLeft(3, '0'));
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
                btnPreviousPlate.Enabled = false;

                mnuNestNextPlate.Enabled = false;
                btnNextPlate.Enabled = false;

                mnuNestFirstPlate.Enabled = false;
                btnFirstPlate.Enabled = false;

                mnuNestLastPlate.Enabled = false;
                btnLastPlate.Enabled = false;
            }
            else
            {
                mnuNestPreviousPlate.Enabled = !activeForm.IsFirstPlate();
                btnPreviousPlate.Enabled = !activeForm.IsFirstPlate();

                mnuNestNextPlate.Enabled = !activeForm.IsLastPlate();
                btnNextPlate.Enabled = !activeForm.IsLastPlate();

                mnuNestFirstPlate.Enabled = activeForm.PlateCount > 0 && !activeForm.IsFirstPlate();
                btnFirstPlate.Enabled = activeForm.PlateCount > 0 && !activeForm.IsFirstPlate();

                mnuNestLastPlate.Enabled = activeForm.PlateCount > 0 && !activeForm.IsLastPlate();
                btnLastPlate.Enabled = activeForm.PlateCount > 0 && !activeForm.IsLastPlate();
            }
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
        }

        private void UpdateStatus()
        {
            UpdateLocationStatus();
            UpdatePlateStatus();
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
            }

            activeForm = ActiveMdiChild as EditNestForm;

            EnableCheck();
            UpdatePlateStatus();
            UpdateLocationStatus();

            if (activeForm == null)
            {
                statusLabel1.Text = "";
                return;
            }

            UpdateLocationMode();
            activeForm.PlateView.StatusChanged += PlateView_StatusChanged;
            mnuViewDrawRapids.Checked = activeForm.PlateView.DrawRapid;
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
                var reader = new NestReader(Properties.Settings.Default.NestTemplatePath);
                nest = reader.Read();
            }
            else
            {
                nest = new Nest();
                nest.Units = Properties.Settings.Default.DefaultUnit;
                nest.PlateDefaults.EdgeSpacing = new Spacing(0, 0, 0, 0);
                nest.PlateDefaults.PartSpacing = 0;
                nest.PlateDefaults.Size = new OpenNest.Geometry.Size(100, 100);
                nest.PlateDefaults.Quadrant = 1;
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
            dlg.Filter = "Nest Files (*.zip) | *.zip";
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
            var drawing = activeForm.Nest.Drawings.Count > 0
                ? activeForm.Nest.Drawings.First()
                : null;

            if (drawing == null)
            {
                MessageBox.Show("No drawings available.", "Best-Fit Viewer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new BestFitViewerForm(drawing, plate))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.SelectedResult != null)
                {
                    var parts = form.SelectedResult.BuildParts(drawing);
                    activeForm.PlateView.SetAction(typeof(ActionClone), parts);
                }
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

        private void RunAutoNest_Click(object sender, EventArgs e)
        {
            var form = new AutoNestForm(activeForm.Nest);
            form.AllowPlateCreation = true;

            if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            var items = form.GetNestItems();
            var maxPlates = 100;

            for (var plateCount = 0; plateCount < maxPlates; plateCount++)
            {
                var remaining = items.Where(i => i.Quantity > 0).ToList();

                if (remaining.Count == 0)
                    break;

                var plate = activeForm.PlateView.Plate.Parts.Count > 0
                    ? activeForm.Nest.CreatePlate()
                    : activeForm.PlateView.Plate;

                var parts = NestEngine.AutoNest(remaining, plate);

                if (parts.Count == 0)
                    break;

                plate.Parts.AddRange(parts);

                // Deduct placed quantities using Drawing.Name to avoid reference issues.
                foreach (var item in remaining)
                {
                    var placed = parts.Count(p => p.BaseDrawing.Name == item.Drawing.Name);
                    item.Quantity = System.Math.Max(0, item.Quantity - placed);
                }

                activeForm.Nest.UpdateDrawingQuantities();
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

        private void FillPlate_Click(object sender, EventArgs e)
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

            var engine = new NestEngine(activeForm.PlateView.Plate);
            engine.Fill(new NestItem
            {
                Drawing = drawing
            });

            activeForm.PlateView.Invalidate();
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

            activeForm.PlateView.SetAction(typeof(ActionFillArea), drawing);
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
