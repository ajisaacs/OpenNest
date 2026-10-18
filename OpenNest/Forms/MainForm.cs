using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Forms.Commands;
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
        private readonly NestStorageController storage;
        private readonly ToolStripMenuItem storageModeMenu = new("Storage Mode...");
        private readonly ToolStripMenuItem exportNestMenu = new("Export .nest...");
        private readonly ToolStripDropDownButton postButton = new()
        {
            Name = "btnPost",
            Text = "Post",
            ToolTipText = "Post CNC program",
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = Resources.post,
            ImageScaling = ToolStripItemImageScaling.None,
            Padding = new Padding(5, 0, 5, 0),
            Visible = false,
        };

        private const float ZoomInFactor = 1.5f;
        private const float ZoomOutFactor = 1.0f / ZoomInFactor;

        private bool disposeAfterCuttingInspection;

        internal bool DeferCuttingDisposal()
        {
            var panel = MdiChildren.OfType<EditNestForm>()
                .Select(form => form.CuttingInspection).FirstOrDefault(value => value != null);
            if (panel == null)
                return false;
            panel.RequestClose();
            if (panel.IsDisposed)
                return false;
            disposeAfterCuttingInspection = true;
            return true;
        }

        internal void ResumeDeferredCuttingDisposal()
        {
            if (!disposeAfterCuttingInspection)
                return;
            disposeAfterCuttingInspection = false;
            if (IsHandleCreated)
                BeginInvoke(new System.Action(Dispose));
            else
                Dispose();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var inspection = MdiChildren.OfType<EditNestForm>()
                .Select(form => form.CuttingInspection).FirstOrDefault(panel => panel != null);
            if (inspection != null)
            {
                if (keyData == Keys.Escape)
                {
                    inspection.RequestClose();
                    return true;
                }
                // Standard inspection navigation reaches the canvas/panel; menu shortcuts cannot mutate.
                // The zoom shortcuts stay live with the header zoom buttons.
                if (keyData == mnuViewZoomIn.ShortcutKeys)
                    ZoomIn_Click(this, EventArgs.Empty);
                else if (keyData == mnuViewZoomOut.ShortcutKeys)
                    ZoomOut_Click(this, EventArgs.Empty);
                if ((keyData & (Keys.Control | Keys.Alt)) != 0)
                    return true;
            }
            if (keyData == Keys.Escape && activeForm?.PlateView != null)
            {
                activeForm.PlateView.ProcessEscapeKey();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public MainForm()
        {
            storage = new NestStorageController(this, text => statusLabel1.Text = text);
            storage.StateChanged += Storage_StateChanged;
            InitializeComponent();
            toolStrip1.Items.Add(CreateActionButton("btnCutPlanner", "Plan Cutting",
                Resources.cut_planner, PlatePlanCutting_Click));
            toolStrip1.Items.Add(CreateActionButton("btnManualSequence", "Manual Sequence",
                Resources.manual_sequence, ManualSequenceParts_Click));
            toolStrip1.Items.Add(postButton);
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
            AllowDrop = true;

            LoadPosts();
            EnableCheck();
            UpdateStatus();

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

            InitializeUpdater();
        }

        private global::OpenNest.UpdaterService updater;
        private ToolStripStatusLabel updateStatusLabel;

        /// <summary>
        /// Starts background update checks against the per-machine feed URL
        /// (Options dialog; blank disables checks). Never restarts on its
        /// own -- shows a clickable status item once an update is ready and
        /// restarts only when the user clicks it.
        /// </summary>
        private void InitializeUpdater()
        {
            updateStatusLabel = new ToolStripStatusLabel
            {
                Visible = false,
                ForeColor = Color.DarkGreen,
                Font = new Font(statusLabel1.Font, FontStyle.Bold),
            };
            updateStatusLabel.Click += (_, _) =>
            {
                if (!updater.HasPendingUpdate)
                    return;

                var result = MessageBox.Show(
                    this,
                    "An OpenNest update has been downloaded. Restart now to apply it? Any unsaved nest will be lost.",
                    "Update ready",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );
                if (result == DialogResult.Yes)
                    updater.ApplyAndRestart();
            };
            statusStrip1.Items.Add(updateStatusLabel);

            updater = new global::OpenNest.UpdaterService();
            updater.UpdateReady += version =>
            {
                if (IsHandleCreated)
                    BeginInvoke(new System.Action(() => ShowUpdateReady(version)));
            };
            updater.Start();
        }

        private void ShowUpdateReady(string version)
        {
            updateStatusLabel.Text = $"Update {version} ready - click to restart";
            updateStatusLabel.Visible = true;
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

        private static ToolStripButton CreateActionButton(string name, string text, Image image,
            EventHandler click)
        {
            var button = new ToolStripButton
            {
                Name = name,
                Text = text,
                ToolTipText = text,
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = image,
                ImageScaling = ToolStripItemImageScaling.None,
                Padding = new Padding(5, 0, 5, 0),
            };
            button.Click += click;
            return button;
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

                        var toolbarPost = new ToolStripMenuItem(postProcessor.Name) { Tag = postProcessor };
                        toolbarPost.Click += PostProcessor_Click;
                        postButton.DropDownItems.Add(toolbarPost);

                        if (postProcessor is IMaterialProvidingPostProcessor materialProvider)
                            PostProcessorMaterials.AddFrom(materialProvider);
                    }
                }
            }

            mnuNestPost.Visible = mnuNestPost.DropDownItems.Count > 0;
            postButton.Visible = postButton.DropDownItems.Count > 0;
        }

        #region Overrides

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            this.Refresh();
        }

        protected override void OnMdiChildActivate(EventArgs e)
        {
            foreach (var editor in MdiChildren.OfType<EditNestForm>()
                .Where(editor => editor.CuttingInspection != null && !ReferenceEquals(editor, ActiveMdiChild)))
                editor.CuttingInspection.RequestClose();
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
            var activeNest = activeForm.Nest;
            activeForm.PlateView.CanEditClamps = () => !ClampsInUse(activeNest);
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
                    var nest = new NestReader(templatePath) { BestFitStore = BestFitCacheStore.Shared }.Read();
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

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = HasDroppedNestFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                OpenDroppedNestFiles(paths);
        }

        private static bool HasDroppedNestFile(IDataObject data) =>
            data.GetData(DataFormats.FileDrop) is string[] paths
            && paths.Any(path => string.Equals(
                Path.GetExtension(path), NestFormat.FileExtension, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Opens dropped .nest files locally regardless of storage mode. Loaded documents are
        /// not bound to a server record, so Save/Save As follow the normal storage-mode rules
        /// (File mode writes to disk; Database mode creates a new record on first save).
        /// </summary>
        private void OpenDroppedNestFiles(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (!string.Equals(Path.GetExtension(path), NestFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
                    continue;

                var reader = new NestReader(path) { BestFitStore = BestFitCacheStore.Shared };
                var nest = reader.Read();
                LoadNest(nest);
                ShowNestWarnings(reader);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            SaveSettings();
        }

        #endregion

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
    }
}
