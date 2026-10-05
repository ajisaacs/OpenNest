using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Collections;
using OpenNest.Data;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using OpenNest.Gpu;
using OpenNest.IO;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    public partial class MainForm : Form
    {
        private EditNestForm activeForm;
        private bool clickUpdateLocation;
        private bool nestingInProgress;
        private CancellationTokenSource nestingCts;
        private bool databaseSaveInProgress;
        private bool databaseOpenInProgress;
        private readonly ToolStripMenuItem storageModeMenu = new("Storage Mode...");
        private readonly ToolStripMenuItem exportNestMenu = new("Export .nest...");

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
            storageModeMenu.Click += StorageMode_Click;
            exportNestMenu.Click += ExportNest_Click;
            mnuFile.DropDownItems.Insert(mnuFile.DropDownItems.IndexOf(mnuFileExport), exportNestMenu);
            mnuFile.DropDownItems.Insert(mnuFile.DropDownItems.IndexOf(mnuFileExit), storageModeMenu);
            UpdateOverlapMenu();
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

            // Jobs-side plug-in discovery: INestingEngine implementations are registered per
            // assembly/type with per-DLL isolation. Existing plug-ins must implement the jobs
            // contract and expose a public parameterless constructor.
            var enginesDir = Path.Combine(Application.StartupPath, "Engines");
            NestingEngineRegistry.LoadPlugins(enginesDir);
            var engineWarning = EngineSelection.LoadSavedSelection();
            if (!string.IsNullOrEmpty(engineWarning))
                Shown += (_, _) => statusLabel1.Text = engineWarning;

            OptionsForm.ApplyDisabledStrategies();
            ColorSchemeRegistry.ApplyActiveFromSettings();

            foreach (var name in EngineSelection.UiEngineNames)
                engineComboBox.Items.Add(name);

            engineComboBox.SelectedItem = EngineSelection.EngineName;
            engineComboBox.SelectedIndexChanged += EngineComboBox_SelectedIndexChanged;
        }

        private Nest CreateDefaultNest()
        {
            var nest = new Nest();
            LoadNestDefaults().ApplyTo(nest);
            return nest;
        }

        /// <summary>
        /// Loads the persisted nest defaults (see <see cref="LoadSavedNestDefaults"/>);
        /// a corrupt or unreadable file warns once per session.
        /// </summary>
        private NestDefaults LoadNestDefaults()
        {
            var defaults = LoadSavedNestDefaults(out var status);

            if (status == NestDefaultsStatus.Invalid && !defaultsWarned)
            {
                defaultsWarned = true;
                MessageBox.Show(
                    $"The nest defaults file could not be read:\n{NestDefaults.DefaultPath}\n\nBuilt-in defaults will be used. Re-save your defaults from Tools > Nest Defaults.",
                    "Nest Defaults",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            return defaults;
        }

        private bool defaultsWarned;

        /// <summary>
        /// The single desktop entry point for persisted nest defaults. Units
        /// fall back to the legacy DefaultUnit setting whenever the file is
        /// missing or unusable or has no valid unit, so New, BOM import and
        /// the defaults dialog always agree.
        /// </summary>
        internal static NestDefaults LoadSavedNestDefaults(out NestDefaultsStatus status) =>
            NestDefaults.Load(
                NestDefaults.DefaultPath,
                Properties.Settings.Default.DefaultUnit,
                out status
            );

        private string GetNestName(DateTime date, int id)
        {
            var year = (date.Year % 100).ToString("D2");
            var seq = ToBase36(id).PadLeft(3, '2');

            return $"N{year}-{seq}";
        }

        private static string ToBase36(int value)
        {
            const string chars = "2345679ACDEFGHJKLMNPQRSTUVWXYZ";
            if (value == 0)
                return chars[0].ToString();

            var result = "";
            while (value > 0)
            {
                result = chars[value % chars.Length] + result;
                value /= chars.Length;
            }
            return result;
        }

        private void LoadNest(Nest nest, FormWindowState windowState = FormWindowState.Maximized,
            Guid remoteId = default, string serverUrl = null)
        {
            var editForm = new EditNestForm(nest);
            if (remoteId != Guid.Empty)
                editForm.Document.BindRemote(remoteId, serverUrl);
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

            if (
                Settings.Default.MainWindowSize.Width <= screen.Width
                && Settings.Default.MainWindowSize.Height <= screen.Height
            )
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
            mnuFileSave.Enabled = hasValue && !databaseSaveInProgress;
            btnSave.Enabled = hasValue && !databaseSaveInProgress;
            btnSaveAs.Enabled = hasValue && !databaseSaveInProgress;
            mnuFileSaveAs.Enabled = hasValue && !databaseSaveInProgress;
            exportNestMenu.Enabled = hasValue;
            mnuFileExport.Enabled = hasValue;
            mnuFileExportAll.Enabled = hasValue;
            mnuFileExportNestReport.Enabled = hasValue && !databaseSaveInProgress;
            btnZoomOut.Enabled = hasValue;
            btnZoomIn.Enabled = hasValue;
            mnuEdit.Visible = hasValue;
            mnuView.Visible = hasValue;
            mnuNest.Visible = hasValue;
            mnuPlate.Visible = hasValue;
            mnuWindow.Visible = hasValue;
            mnuToolsAlign.Visible = hasValue;
            mnuToolsMeasureArea.Visible = hasValue;
            mnuToolsExpandSpacing.Visible = hasValue;
            mnuToolsSaveCurrentAsDefaults.Visible = hasValue;

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
                mnuNestFirstPlate.Enabled =
                    activeForm.PlateManager.Count > 0 && !activeForm.PlateManager.IsFirst;
                mnuNestLastPlate.Enabled =
                    activeForm.PlateManager.Count > 0 && !activeForm.PlateManager.IsLast;
            }
        }

        private void SetNestingLockout(bool locked)
        {
            nestingInProgress = locked;

            // Disable nesting-related menus while running
            mnuNest.Enabled = !locked;
            mnuPlate.Enabled = !locked;

            // Lock plate navigation
            mnuNestPreviousPlate.Enabled =
                !locked && activeForm != null && !activeForm.PlateManager.IsFirst;
            mnuNestNextPlate.Enabled =
                !locked && activeForm != null && !activeForm.PlateManager.IsLast;
            mnuNestFirstPlate.Enabled =
                !locked
                && activeForm != null
                && activeForm.PlateManager.Count > 0
                && !activeForm.PlateManager.IsFirst;
            mnuNestLastPlate.Enabled =
                !locked
                && activeForm != null
                && activeForm.PlateManager.Count > 0
                && !activeForm.PlateManager.IsLast;
        }

        private void UpdateLocationStatus()
        {
            if (activeForm == null)
            {
                locationStatusLabel.Text = string.Empty;
                return;
            }

            locationStatusLabel.Text = string.Format(
                "Location: [{0}, {1}]",
                activeForm.PlateView.CurrentPoint.X.ToString("n4"),
                activeForm.PlateView.CurrentPoint.Y.ToString("n4")
            );
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
                activeForm.PlateManager.Count
            );

            plateSizeStatusLabel.Text = string.Format("Size: {0}", activeForm.PlateView.Plate.Size);

            plateQtyStatusLabel.Text = string.Format(
                "Qty: {0}",
                activeForm.PlateView.Plate.Quantity
            );

            plateUtilStatusLabel.Text = string.Format(
                "Util: {0:P1}",
                activeForm.PlateView.Plate.Utilization()
            );
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
                selectionStatusLabel.Text = string.Format(
                    "Selected: [{0}, {1}] {2} x {3}",
                    box.X.ToString("n4"),
                    box.Y.ToString("n4"),
                    box.Width.ToString("n4"),
                    box.Length.ToString("n4")
                );
            }
            else
            {
                var bounds = selected.Select(p => p.BasePart).ToList().GetBoundingBox();
                selectionStatusLabel.Text = string.Format(
                    "Selected ({0}): [{1}, {2}] {3} x {4}",
                    selected.Count,
                    bounds.X.ToString("n4"),
                    bounds.Y.ToString("n4"),
                    bounds.Width.ToString("n4"),
                    bounds.Length.ToString("n4")
                );
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
                EngineSelection.EngineName = name;
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

                        if (postProcessor is IMaterialProvidingPostProcessor materialProvider)
                            PostProcessorMaterials.AddFrom(materialProvider);
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
                activeForm.PlateView.OverlapStateChanged -= OverlapStateChanged;
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
            UpdateOverlapMenu();

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
            activeForm.PlateView.OverlapStateChanged += OverlapStateChanged;
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

            MigrateNestTemplate();

            if (Settings.Default.CreateNewNestOnOpen)
                New_Click(this, new EventArgs());
        }

        /// <summary>
        /// One-time upgrade: converts a legacy .nstdot nest template
        /// (NestTemplatePath setting) into defaults.json, then clears the
        /// setting so the template mechanism is never consulted again.
        /// </summary>
        private void MigrateNestTemplate()
        {
            var templatePath = Settings.Default.NestTemplatePath;
            if (string.IsNullOrWhiteSpace(templatePath))
                return;

            // A failed conversion keeps the setting populated so the user
            // can still find their template file.
            var converted = true;
            if (File.Exists(templatePath) && !File.Exists(NestDefaults.DefaultPath))
            {
                try
                {
                    var nest = new NestReader(templatePath).Read();
                    NestDefaults.FromNest(nest).Save(NestDefaults.DefaultPath);
                }
                catch (Exception ex)
                {
                    converted = false;
                    MessageBox.Show(
                        $"The nest template could not be converted to the new defaults file:\n{templatePath}\n\n{ex.Message}\n\nIt will no longer be loaded automatically. Set defaults under Tools > Nest Defaults.",
                        "Nest Template",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }

            if (!converted)
                return;

            Settings.Default.NestTemplatePath = "";
            Settings.Default.Save();
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
            var windowState =
                ActiveMdiChild != null ? ActiveMdiChild.WindowState : FormWindowState.Maximized;

            var nest = CreateDefaultNest();

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

        private async void Open_Click(object sender, EventArgs e)
        {
            if (NestStorage.Settings.Mode == NestStorageMode.File)
            {
                using var dlg = new OpenFileDialog { Filter = NestFormat.FileFilter, Multiselect = true };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var reader = new NestReader(dlg.FileName);
                    var nest = reader.Read();
                    LoadNest(nest);
                    ShowNestWarnings(reader);
                }
                return;
            }

            try
            {
                var repository = NestStorage.CreateRepository();
                using var dlg = new SavedNestsForm(repository, NestStorage.Settings.ServerUrl);
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                var serverUrl = NestStorage.Settings.ServerUrl;
                databaseOpenInProgress = true;
                storageModeMenu.Enabled = false;
                var file = await repository.GetFileAsync(dlg.SelectedId);
                if (file == null)
                    throw new FileNotFoundException("This nest no longer exists on the server.");
                using var stream = new MemoryStream(file);
                var reader = new NestReader(stream);
                var nest = reader.Read();
                LoadNest(nest, remoteId: dlg.SelectedId, serverUrl: serverUrl);
                ShowNestWarnings(reader);
            }
            catch (Exception ex)
            {
                ShowStorageError("open", ex);
            }
            finally
            {
                databaseOpenInProgress = false;
                if (!IsDisposed)
                    storageModeMenu.Enabled = !databaseSaveInProgress;
            }
        }

        private void ShowNestWarnings(NestReader reader)
        {
            if (reader.Warnings.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, reader.Warnings),
                    "Nest Load Warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ImportBom_Click(object sender, EventArgs e)
        {
            var form = new BomImportForm();
            form.MdiParentForm = this;
            form.ShowDialog(this);
        }

        private async void Save_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null || databaseSaveInProgress)
                return;
            if (NestStorage.Settings.Mode == NestStorageMode.File)
                form.Save();
            else
                await SaveDatabaseAsync(form, saveCopy: false);
        }

        private async void SaveAs_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null || databaseSaveInProgress)
                return;
            if (NestStorage.Settings.Mode == NestStorageMode.File)
                form.SaveAs();
            else
                await SaveDatabaseAsync(form, saveCopy: true);
        }

        private async Task SaveDatabaseAsync(EditNestForm form, bool saveCopy)
        {
            databaseSaveInProgress = true;
            storageModeMenu.Enabled = false;
            EnableCheck();
            try
            {
                var serverUrl = NestStorage.Settings.ServerUrl;
                var saved = await form.Document.SaveToDatabaseAsync(
                    NestStorage.CreateRepository(), serverUrl, saveCopy);
                if (!form.IsDisposed)
                    form.Text = form.Document.Name;
                if (!IsDisposed)
                    statusLabel1.Text = $"Saved {saved.Name} to nest database";
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    ShowStorageError("save", ex);
            }
            finally
            {
                databaseSaveInProgress = false;
                if (!IsDisposed)
                {
                    storageModeMenu.Enabled = !databaseOpenInProgress;
                    EnableCheck();
                }
            }
        }

        private void StorageMode_Click(object sender, EventArgs e)
        {
            using var dlg = new StorageModeForm();
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                NestStorage.Save(dlg.SelectedMode, dlg.ServerUrl);
                statusLabel1.Text = $"Nest storage: {dlg.SelectedMode}";
            }
            catch (Exception ex)
            {
                ShowStorageError("change storage mode", ex);
            }
        }

        private void ExportNest_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null)
                return;
            using var dlg = new SaveFileDialog { Filter = NestFormat.FileFilter, FileName = form.Nest.Name };
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                // Export never changes the document's save path, remote id, or nest name.
                new NestWriter(form.Nest).Write(dlg.FileName);
                statusLabel1.Text = $"Exported .nest to {dlg.FileName}";
            }
            catch (Exception ex)
            {
                ShowStorageError("export", ex);
            }
        }

        private void ShowStorageError(string action, Exception ex) =>
            MessageBox.Show(this, $"Could not {action} nest: {ex.Message}",
                "Nest Storage", MessageBoxButtons.OK, MessageBoxIcon.Error);

        private void Export_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Export();
        }

        private void ExportAll_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ExportAll();
        }

        private void ExportNestReport_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form == null)
                return;

            // The report never depends on a selected post or NC output; it only needs a
            // quiescent nest so the snapshot is coherent.
            Func<bool> isJobBusy = IsNestJobBusy;
            EditNestForm.NestReportTargets targets;
            try
            {
                targets = form.CaptureReportTargets(isJobBusy);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                ReportFailure(null, ex);
                return;
            }

            var destination = ShowReportSaveDialog(targets.SuggestedFileName);
            if (destination == null)
                return; // Cancel: nothing written, job untouched.

            try
            {
                form.WriteNestReport(targets, destination, isJobBusy);
            }
            catch (Exception ex)
                when (ex is InvalidOperationException
                    or NotSupportedException
                    or InvalidDataException
                    or IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                // Failures describe the captured target; an active-document switch is irrelevant.
                ReportFailure(targets, ex);
                return;
            }
            statusLabel1.Text = $"Saved nest report to {destination}";
            ReportSuccess(destination);
        }

        /// <summary>
        /// True while any operation could mutate nests or hold their plate views: whole-job
        /// nesting, an open progress window (a closed one can still await its commit), or a
        /// background database save serializing the live nest.
        /// </summary>
        private bool IsNestJobBusy() =>
            nestingInProgress
            || Application.OpenForms.OfType<NestProgressForm>().Any()
            || databaseSaveInProgress;

        /// <summary>Returns the chosen path, or null on cancel. Overwrite consent is the dialog prompt.</summary>
        internal virtual string ShowReportSaveDialog(string suggestedFileName)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "PDF report (*.pdf)|*.pdf",
                FileName = suggestedFileName,
                AddExtension = true,
                DefaultExt = ".pdf",
                OverwritePrompt = true,
            };
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
        }

        /// <summary>Failure notice naming the fixed target; internal so tests observe it without dialogs.</summary>
        internal virtual void ReportFailure(
            EditNestForm.NestReportTargets targets,
            Exception error) =>
            MessageBox.Show(this,
                $"Could not export the nest report{(targets == null ? "" : $" for '{targets.Nest.Name}'")}: {error.Message}",
                "Export Nest Report", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        /// <summary>Success notice; internal so tests observe completion without dialogs.</summary>
        internal virtual void ReportSuccess(string destination) =>
            MessageBox.Show(this, $"Nest report saved to:\n\n{destination}", "Export Nest Report",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

        private void Exit_Click(object sender, EventArgs e)
        {
            Close();
        }

        #endregion File Menu Events

        #region Edit Menu Events

        private void EditCut_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var selectedParts = activeForm.PlateView.SelectedParts;

            if (selectedParts.Count > 0)
            {
                var partsToClone = selectedParts.Select(p => p.BasePart).ToList();
                activeForm.PlateView.SetAction(typeof(ActionClone), partsToClone);

                foreach (var part in partsToClone)
                    activeForm.PlateView.Plate.Parts.Remove(part);
            }
        }

        private void EditPaste_Click(object sender, EventArgs e) { }

        private void EditCopy_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var selectedParts = activeForm.PlateView.SelectedParts;

            if (selectedParts.Count > 0)
            {
                var partsToClone = selectedParts.Select(p => p.BasePart).ToList();
                activeForm.PlateView.SetAction(typeof(ActionClone), partsToClone);
            }
        }

        private void EditSelectAll_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.SelectAllParts();
        }

        #endregion Edit Menu Events

        #region View Menu Events

        private void OverlapStateChanged(object sender, EventArgs e) => UpdateOverlapMenu();
        private void OverlapMenu_Opening(object sender, EventArgs e) => UpdateOverlapMenu();

        private void UpdateOverlapMenu()
        {
            var hasPlate = activeForm != null && !activeForm.IsDisposed && activeForm.PlateView.Plate != null;
            mnuViewOverlapCheck.Enabled = hasPlate;
            mnuOverlapCheckActive.Enabled = hasPlate;
            mnuOverlapCancel.Enabled = hasPlate && activeForm.PlateView.IsOverlapCheckRunning;
            mnuOverlapDisplay.Enabled = hasPlate;
            mnuOverlapOff.Enabled = hasPlate;
            mnuOverlapAreas.Enabled = hasPlate;
            mnuOverlapCentroids.Enabled = hasPlate;
            mnuOverlapBoth.Enabled = hasPlate;
            mnuOverlapOff.Checked = hasPlate && activeForm.OverlapDisplay == OverlapDisplayMode.Off;
            mnuOverlapAreas.Checked = hasPlate && activeForm.OverlapDisplay == OverlapDisplayMode.Areas;
            mnuOverlapCentroids.Checked = hasPlate && activeForm.OverlapDisplay == OverlapDisplayMode.Centroids;
            mnuOverlapBoth.Checked = hasPlate && activeForm.OverlapDisplay == OverlapDisplayMode.Both;
        }

        private async void CheckOverlaps_Click(object sender, EventArgs e)
        {
            var form = activeForm;
            if (form != null)
                await form.CheckOverlapsAsync();
        }

        private void CancelOverlapCheck_Click(object sender, EventArgs e) => activeForm?.CancelOverlapCheck();

        private void OverlapOff_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.OverlapDisplay = OverlapDisplayMode.Off;
        }

        private void OverlapAreas_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.OverlapDisplay = OverlapDisplayMode.Areas;
        }

        private void OverlapCentroids_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.OverlapDisplay = OverlapDisplayMode.Centroids;
        }

        private void OverlapBoth_Click(object sender, EventArgs e)
        {
            if (activeForm != null)
                activeForm.OverlapDisplay = OverlapDisplayMode.Both;
        }

        private void ToggleDrawRapids_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ToggleRapid();
            mnuViewDrawRapids.Checked = activeForm.PlateView.DrawRapid;
        }

        private void ToggleDrawPiercePoints_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.TogglePiercePoints();
            mnuViewDrawPiercePoints.Checked = activeForm.PlateView.DrawPiercePoints;
        }

        private void ToggleDrawBounds_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ToggleDrawBounds();
            mnuViewDrawBounds.Checked = activeForm.PlateView.DrawBounds;
        }

        private void ToggleDrawOffset_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ToggleDrawOffset();
            mnuViewDrawOffset.Checked = activeForm.PlateView.DrawOffset;
        }

        private void ToggleDrawCutDirection_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ToggleCutDirection();
            mnuViewDrawCutDirection.Checked = activeForm.PlateView.DrawCutDirection;
        }

        private void ZoomToArea_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.SetAction(typeof(ActionZoomWindow));
        }

        private void ZoomToFit_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.ZoomToFit();
        }

        private void ZoomToPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.ZoomToPlate();
        }

        private void ZoomToSelected_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.ZoomToSelected();
        }

        private void ZoomIn_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var pt = new Point(activeForm.PlateView.Width / 2, activeForm.PlateView.Height / 2);

            activeForm.PlateView.ZoomToControlPoint(pt, ZoomInFactor);
        }

        private void ZoomOut_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var pt = new Point(activeForm.PlateView.Width / 2, activeForm.PlateView.Height / 2);

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
                MessageBox.Show(
                    "No drawings available.",
                    "Best-Fit Viewer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            using (var form = new BestFitViewerForm(drawings, plate, activeForm.Nest.Units))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.SelectedResult != null)
                {
                    var parts =
                        form.SelectedParts
                        ?? form.SelectedResult.BuildSourceParts(form.SelectedDrawing);
                    activeForm.PlateView.SetAction(typeof(ActionClone), parts);
                }
            }
        }

        private void ExpandSpacing_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            if (!activeForm.PlateView.ExpandSelected())
                MessageBox.Show(
                    "Select at least two parts on the plate to expand.",
                    "Expand Spacing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
        }

        private void PatternTile_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            if (activeForm.Nest.Drawings.Count == 0)
            {
                MessageBox.Show(
                    "No drawings available.",
                    "Pattern Tile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
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
            if (activeForm == null)
                return;
            var form = new SetValueForm();
            form.Text = "Set Offset Increment";
            form.Value = activeForm.PlateView.OffsetIncrementDistance;

            if (form.ShowDialog() == DialogResult.OK)
                activeForm.PlateView.OffsetIncrementDistance = form.Value;
        }

        private void SetRotationIncrement_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
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

        private void NestDefaults_Click(object sender, EventArgs e)
        {
            using (var form = new NestDefaultsForm(LoadSavedNestDefaults(out _)))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                    form.GetDefaults().Save(NestDefaults.DefaultPath);
            }
        }

        private void SaveCurrentAsDefaults_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            NestDefaults
                .FromPlate(activeForm.Nest.Units, activeForm.PlateView.Plate)
                .Save(NestDefaults.DefaultPath);
        }

        private void MachineConfig_Click(object sender, EventArgs e)
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenNest",
                "Machines"
            );
            var provider = new LocalJsonProvider(appDataPath);
            provider.EnsureDefaults();
            using (var form = new MachineConfigForm(provider))
            {
                form.ShowDialog(this);
            }
        }

        private void AlignLeft_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Left);
        }

        private void AlignRight_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Right);
        }

        private void AlignTop_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Top);
        }

        private void AlignBottom_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Bottom);
        }

        private void AlignVertical_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Vertically);
        }

        private void AlignHorizontal_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.Horizontally);
        }

        private void EvenlySpaceHorizontally_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.EvenlySpaceHorizontally);
        }

        private void EvenlySpaceVertically_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateView.AlignSelected(AlignType.EvenlySpaceVertically);
        }

        #endregion Tools Menu Events

        #region Nest Menu Events

        private void Import_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Import();
        }

        private void ShapeLibrary_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            var form = new ShapeLibraryForm(activeForm.Nest.Drawings.Select(d => d.Name));
            form.ShowDialog();

            var drawings = form.GetDrawings();
            if (drawings.Count == 0)
                return;

            drawings.ForEach(d => activeForm.Nest.Drawings.Add(d));
            activeForm.UpdateDrawingList();
        }

        private void EditNest_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ShowNestInfoEditor();
        }

        private void RemoveEmptyPlates_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Nest.Plates.RemoveEmptyPlates();
        }

        private void LoadFirstPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateManager.LoadFirst();
        }

        private void LoadLastPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateManager.LoadLast();
        }

        private void LoadPreviousPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlateManager.LoadPrevious();
        }

        private void LoadNextPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
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
                    Top
                );
            }

            remnantViewer.LoadRemnants(finder, minDim, activeForm.PlateView);
            remnantViewer.Show();
            remnantViewer.BringToFront();
        }

        private async void RunAutoNest_Click(object sender, EventArgs e)
        {
            var target = activeForm;
            if (target == null || target.IsDisposed || target.PlateView.Plate == null)
                return;
            var views = MdiChildren.OfType<EditNestForm>()
                .Where(f => ReferenceEquals(f.Nest, target.Nest)).Select(f => f.PlateView).ToArray();
            if (nestingInProgress || Application.OpenForms.OfType<NestProgressForm>().Any()
                || views.Any(v => v.IsFillInProgress || v.Actions.CurrentAction?.IsBusy() == true))
            {
                MessageBox.Show(this, "Finish or cancel the current plate operation before Auto Nest.",
                    "Auto Nest", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (var view in views)
                view.SetAction(typeof(ActionSelect));

            using var form = new AutoNestForm(target.Nest);
            if (target.Nest.PlateOptions.Count > 0)
                form.LoadPlateOptions(target.Nest.PlateOptions, target.Nest.SalvageRate);
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            var items = form.GetNestItems();
            if (!items.Any(it => it.Quantity > 0))
                return;
            var plateOptions = default(List<PlateOption>);
            if (form.OptimizePlateSize && !form.TryGetPlateOptions(out plateOptions, out var stockError))
            {
                MessageBox.Show(this, stockError, "Auto Nest", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var engineName = form.EngineName ?? EngineSelection.EngineName;
            EngineSelection.EngineName = engineName;
            engineComboBox.SelectedItem = engineName;
            using var cts = new CancellationTokenSource();
            nestingCts = cts;
            using var progressForm = new NestProgressForm(cts, showPlateRow: true)
            {
                AllowAccept = false,
                HoldOpenUntilCompleted = true,
            };
            var receivingProgress = true;
            var progress = new Progress<NestProgress>(p =>
            {
                if (!receivingProgress || target.IsDisposed || cts.IsCancellationRequested)
                    return;
                progressForm.UpdateProgress(p);
                if (p.IsOverallBest)
                    target.PlateView.SetActiveParts(p.BestParts);
                target.PlateView.ActiveWorkArea = p.ActiveWorkArea;
            });
            var jobProgress = JobEngineNest.CreateProgress(engineName, progress);
            SetNestingLockout(true);
            try
            {
                var request = new NestPipelineRequest(engineName, items,
                    NestStockBuilder.FromTemplate(target.PlateView.Plate, plateOptions),
                    new NestJobOptions(maxPlates: 100,
                        salvageRate: plateOptions?.Count > 0 ? form.SalvageRate : 0,
                        minimumSalvageDimension: form.MinRemnantSize));

                async Task<NestPipelineResult> SolveAsync()
                {
                    try
                    {
                        return await Task.Run(() => NestPipeline.Run(request, jobProgress, cts.Token));
                    }
                    finally
                    {
                        receivingProgress = false;
                        progressForm.ShowCompleted();
                        progressForm.Close();
                    }
                }

                var solve = SolveAsync();
                // Owned modal progress holds drawings/plates stable through solve and cancellation.
                if (!solve.IsCompleted)
                    progressForm.ShowDialog(this);
                var result = await solve;
                cts.Token.ThrowIfCancellationRequested();
                target.PlateView.ClearPreviewParts();
                target.PlateView.ActiveWorkArea = null;

                var allowInvalid = false;
                if (!result.IsValid)
                {
                    using var review = new NestValidationForm(result.Violations, result.CanKeep);
                    if (review.ShowDialog(this) != DialogResult.OK)
                        return;
                    allowInvalid = true;
                }

                var applied = NestPipelineCommit.ApplyToEmptyPlates(result, target.PlateManager,
                    allowInvalid, cts.Token);
                if (applied.Count > 0)
                    target.PlateManager.LoadAt(target.Nest.Plates.IndexOf(applied[0]));
                if (plateOptions?.Count > 0)
                {
                    target.Nest.PlateOptions = plateOptions;
                    target.Nest.SalvageRate = form.SalvageRate;
                }
                target.PlateView.Invalidate();
                if (allowInvalid)
                {
                    target.OverlapDisplay = OverlapDisplayMode.Both;
                    await target.CheckOverlapsAsync();
                }
                if (result.Status != NestJobStatus.Complete)
                    MessageBox.Show(this, $"{engineName} could not place every part ({result.StopReason}).",
                        "Auto Nest", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Stop discards the entire proposal, including engines that ignore cancellation.
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Nesting error: {ex.Message}", "Auto Nest",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                receivingProgress = false;
                if (!target.IsDisposed)
                {
                    target.PlateView.ClearPreviewParts();
                    target.PlateView.ActiveWorkArea = null;
                }
                SetNestingLockout(false);
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

            var editForm = activeForm;
            if (postProcessor == null || editForm == null)
                return;

            // Closed progress windows can still have a fill awaiting its final commit.
            // Keep the nest stable throughout verification and all owned modal dialogs.
            var views = MdiChildren.OfType<EditNestForm>()
                .Where(form => ReferenceEquals(form.Nest, editForm.Nest))
                .Select(form => form.PlateView).ToArray();
            if (nestingInProgress || Application.OpenForms.OfType<NestProgressForm>().Any()
                || views.Any(view => view.IsFillInProgress || view.Actions.CurrentAction?.IsBusy() == true))
            {
                MessageBox.Show(this, "Finish or cancel the current nesting or plate action before posting.",
                    "Verify Nest Before Posting", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            foreach (var view in views)
                view.SetAction(typeof(ActionSelect));
            var nest = editForm.Nest;

            if (postProcessor is IPostProcessorNestAware nestAware)
                nestAware.PrepareForNest(nest);

            if (postProcessor is IConfigurablePostProcessor configurable)
            {
                using var configForm = new PostProcessorConfigForm(configurable);
                if (configForm.ShowDialog(this) != DialogResult.OK)
                    return;
            }

            using (var verification = new PostVerificationForm(nest, postProcessor))
            {
                if (verification.ShowDialog(this) != DialogResult.OK)
                    return;
            }

            using var dialog = new SaveFileDialog();
            dialog.Filter = "CNC File (*.cnc) | *.cnc";
            dialog.FileName = nest.Name;

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                var path = dialog.FileName;

                if (postProcessor is IMultiFilePostProcessor multiFile)
                {
                    var files = multiFile.GetOutputFiles(nest, path);
                    if (!ConfirmOverwrite(files, path))
                        return;

                    try
                    {
                        postProcessor.Post(nest, path);
                    }
                    catch (Exception ex)
                        when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                    {
                        MessageBox.Show(
                            this,
                            ex.Message,
                            postProcessor.Name,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                        return;
                    }

                    if (files.Count > 1)
                        MessageBox.Show(
                            this,
                            $"Saved {files.Count} programs, one per sheet:\n\n"
                                + string.Join("\n", files.Select(Path.GetFileName))
                                + $"\n\nin {Path.GetDirectoryName(path)}",
                            postProcessor.Name,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    return;
                }

                postProcessor.Post(nest, path);
            }
        }

        /// <summary>
        /// The save dialog only checks the chosen name; ask before replacing any
        /// other existing file a multi-file post will write.
        /// </summary>
        private bool ConfirmOverwrite(IReadOnlyList<string> files, string chosenPath)
        {
            var existing = files
                .Where(f => File.Exists(f) && !string.Equals(f, chosenPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (existing.Count == 0)
                return true;

            var answer = MessageBox.Show(
                this,
                "These files already exist and will be replaced:\n\n"
                    + string.Join("\n", existing.Select(Path.GetFileName))
                    + "\n\nReplace them?",
                "Confirm Save",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );
            return answer == DialogResult.Yes;
        }

        private void CalculateNestCutTime_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;

            activeForm.CalculateNestCutTime();
        }

        private void NestAssignLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.AssignLeadInsAllPlates();
        }

        private void NestRemoveLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.RemoveLeadInsAllPlates();
        }

        #endregion Nest Menu Events

        #region Plate Menu Events

        private void SetAsNestDefault_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.SetCurrentPlateAsNestDefault();
        }

        private void AddPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Nest.CreatePlate();
            NavigationEnableCheck();
        }

        private void EditPlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.EditPlate();
        }

        private void RemovePlate_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.RemoveCurrentPlate();
        }

        private void ResizeToFitParts_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.ResizePlateToFitParts();
            UpdatePlateStatus();
        }

        private void RotateCw_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.RotateCw();
        }

        private void RotateCcw_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.RotateCcw();
        }

        private void Rotate180_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.Rotate180();
        }

        private void OpenInExternalCad_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
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

        private void AutomaticCutOff_Click(object sender, EventArgs e) => ShowAutomaticCutOff(allPlates: false);

        private void NestAutomaticCutOff_Click(object sender, EventArgs e) => ShowAutomaticCutOff(allPlates: true);

        private void ShowAutomaticCutOff(bool allPlates)
        {
            var editForm = activeForm;
            var view = editForm?.PlateView;
            if (view?.Plate == null)
                return;

            var nest = editForm.Nest;
            var views = MdiChildren.OfType<EditNestForm>()
                .Where(form => ReferenceEquals(form.Nest, nest))
                .Select(form => form.PlateView).ToArray();
            // A closed progress window can still have a fill awaiting completion/commit.
            bool IsBusy() => nestingInProgress || Application.OpenForms.OfType<NestProgressForm>().Any()
                || views.Any(v => v.IsFillInProgress || v.Actions.CurrentAction?.IsBusy() == true);
            if (IsBusy())
            {
                MessageBox.Show(this, "Finish or cancel the current nesting or plate action first.",
                    "Automatic Scrap Cutoffs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                foreach (var plateView in views)
                    plateView.SetAction(typeof(ActionSelect));
                using var form = allPlates
                    ? new AutomaticCutOffForm(view, nest, IsBusy)
                    : new AutomaticCutOffForm(view, nest.Units, IsBusy);
                form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Automatic Scrap Cutoffs",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!view.IsDisposed)
                    view.ClearPreviewParts();
            }
        }

        private void PlateAssignLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.AssignLeadIns_Click(sender, e);
        }

        private void PlatePlaceLeadIn_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
            activeForm.PlaceLeadIn_Click(sender, e);
        }

        private void PlateRemoveLeadIns_Click(object sender, EventArgs e)
        {
            if (activeForm == null)
                return;
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

        private void PlateView_PartAdded(object sender, ItemAddedEventArgs<Part> e) =>
            UpdatePlateStatus();

        private void PlateView_PartRemoved(object sender, ItemRemovedEventArgs<Part> e) =>
            UpdatePlateStatus();

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
