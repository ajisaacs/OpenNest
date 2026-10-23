using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Jobs;
using OpenNest.Forms.Commands;
using OpenNest.Help;
using OpenNest.IO;
using OpenNest.Properties;

namespace OpenNest.Forms
{
    public partial class MainForm : Form
    {
        private EditNestForm activeForm;

        private readonly MainStatusBar statusBar;
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

        /// <summary>Runs <paramref name="action"/> on the active nest document, if there is one.</summary>
        private void WithActive(Action<EditNestForm> action)
        {
            if (activeForm != null)
                action(activeForm);
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
            statusBar = new MainStatusBar(statusLabel1, locationStatusLabel, selectionStatusLabel,
                machineStatusLabel, materialStatusLabel, plateUtilStatusLabel);
            toolStrip1.Items.Add(CreateActionButton("btnCutPlanner", "Plan Cutting",
                Resources.cut_planner, PlatePlanCutting_Click));
            toolStrip1.Items.Add(CreateActionButton("btnSimulateCutting", "Simulate Cutting",
                Resources.cut_simulate, SimulateCutting_Click));
            toolStrip1.Items.Add(CreateActionButton("btnSequenceByPath", "Sequence by Path",
                Resources.manual_sequence, SequenceByPath_Click));
            toolStrip1.Items.Add(postButton);
            storageModeMenu.Click += StorageMode_Click;
            exportNestMenu.Click += ExportNest_Click;
            mnuFile.DropDownItems.Insert(mnuFile.DropDownItems.IndexOf(mnuFileExport), exportNestMenu);
            mnuFile.DropDownItems.Insert(mnuFile.DropDownItems.IndexOf(mnuFileExit), storageModeMenu);
            InitializeAlignMenu();
            InitializeHelpMenu();
            UpdateOverlapMenu();
            MainWindowSettings.Load(this);

            var renderer = new ToolStripRenderer(ToolbarTheme.Toolbar);
            menuStrip1.Renderer = renderer;
            toolStrip1.Renderer = renderer;

            mnuViewZoomIn.ShortcutKeyDisplayString = "Ctrl+Plus";
            mnuViewZoomOut.ShortcutKeyDisplayString = "Ctrl+Minus";

            this.SetBevel(false);
            AllowDrop = true;

            LoadPosts(PostProcessorLoader.Load(PostProcessorLoader.DefaultDirectory));
            EnableCheck();
            statusBar.UpdateAll();

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

            updaterStatus = new UpdaterStatus(this, statusStrip1, statusLabel1.Font);
            updaterStatus.Start();
        }

        private UpdaterStatus updaterStatus;

        private void InitializeHelpMenu()
        {
            var contents = new ToolStripMenuItem("OpenNest &Help") { ShortcutKeys = Keys.F1 };
            contents.Click += (_, _) => HelpTopics.Show(this, HelpTopics.Home);
            var firstNest = new ToolStripMenuItem("&Your First Nest");
            firstNest.Click += (_, _) => HelpTopics.Show(this, HelpTopics.FirstNest);
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.AddRange(new ToolStripItem[] { contents, firstNest });
            menuStrip1.Items.Add(help);
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

        private void LoadPosts(PostProcessorLoader.Result posts)
        {
            foreach (var postProcessor in posts.PostProcessors)
            {
                var postProcessorMenuItem = new ToolStripMenuItem(postProcessor.Name);
                postProcessorMenuItem.Tag = postProcessor;
                postProcessorMenuItem.Click += PostProcessor_Click;
                mnuNestPost.DropDownItems.Add(postProcessorMenuItem);

                var toolbarPost = new ToolStripMenuItem(postProcessor.Name) { Tag = postProcessor };
                toolbarPost.Click += PostProcessor_Click;
                postButton.DropDownItems.Add(toolbarPost);
            }

            mnuNestPost.Visible = mnuNestPost.DropDownItems.Count > 0;
            postButton.Visible = postButton.DropDownItems.Count > 0;

            if (posts.Failures.Count > 0)
                Shown += (_, _) => MessageBox.Show(this,
                    "These post processors could not be loaded:\n\n" + string.Join("\n", posts.Failures),
                    "Post Processors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            statusBar.Unbind();
            if (activeForm != null)
                activeForm.PlateView.OverlapStateChanged -= OverlapStateChanged;

            // If nesting is in progress and the active form changed, cancel nesting
            if (nestingInProgress && nestingCts != null)
            {
                nestingCts.Cancel();
            }

            activeForm = ActiveMdiChild as EditNestForm;
            UpdateOverlapMenu();

            EnableCheck();
            statusBar.Bind(activeForm);

            if (activeForm == null)
                return;

            activeForm.PlateView.OverlapStateChanged += OverlapStateChanged;
            var activeNest = activeForm.Nest;
            activeForm.PlateView.CanEditClamps = () => !ClampsInUse(activeNest);
            mnuViewDrawRapids.Checked = activeForm.PlateView.DrawRapid;
            mnuViewDrawPiercePoints.Checked = activeForm.PlateView.DrawPiercePoints;
            mnuViewDrawBounds.Checked = activeForm.PlateView.DrawBounds;
            mnuViewDrawCutDirection.Checked = activeForm.PlateView.DrawCutDirection;
            mnuViewDrawSupport.Checked = activeForm.PlateView.DrawSupport;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            MainWindowSettings.MigrateNestTemplate();

            if (Settings.Default.CreateNewNestOnOpen)
                New_Click(this, new EventArgs());
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
            MainWindowSettings.Save(this);
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
