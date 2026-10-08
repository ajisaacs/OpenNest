using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Api;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Collections;
using OpenNest.Controls;
using OpenNest.Diagnostics;
using OpenNest.Engine;
using OpenNest.Engine.Sequencing;
using OpenNest.IO;
using OpenNest.Math;
using OpenNest.Properties;
using OpenNest.Reporting;
using OpenNest.Shapes;
using Timer = System.Timers.Timer;

namespace OpenNest.Forms
{
    public partial class EditNestForm : Form
    {
        public event EventHandler PlateChanged;

        public readonly Document Document;
        public readonly PlateView PlateView;

        public System.Threading.Tasks.Task CheckOverlapsAsync() => PlateView.CheckOverlapsAsync(Nest.Units);
        public void CancelOverlapCheck() => PlateView.CancelOverlapCheck();
        public OverlapDisplayMode OverlapDisplay
        {
            get => PlateView.OverlapDisplay;
            set => PlateView.OverlapDisplay = value;
        }

        public readonly PlateManager PlateManager;

        public Nest Nest => Document.Nest;

        private readonly Timer updateDrawingListTimer;
        private bool updatingPlateList;

        private Panel plateHeaderPanel;
        private Label plateInfoLabel;
        private Button btnFirstPlate;

        private Button btnPreviousPlate;
        private Button btnNextPlate;
        private Button btnLastPlate;

        private SplitContainer viewSplitContainer;
        private Panel sidePanel;
        private Label sidePanelTitle;

        /// <summary>
        /// Used to distinguish between single/double click on drawing within drawinglistbox.
        /// If double click, this is set to false so the single click action won't be triggered.
        /// </summary>
        private bool addPart;

        private EditNestForm()
        {
            PlateView = new PlateView();
            PlateView.MouseEnter += PlateView_MouseEnter;
            PlateView.Enter += PlateView_Enter;
            PlateView.PartAdded += PlateView_PartAdded;
            PlateView.PartRemoved += PlateView_PartRemoved;
            PlateView.PartsReordered += PlateView_PartsReordered;
            PlateView.Dock = DockStyle.Fill;

            InitializeComponent();
            CreatePlateHeader();
            CreateSidePanel();

            splitContainer.Panel2.Controls.Add(viewSplitContainer);
            splitContainer.Panel2.Controls.Add(plateHeaderPanel);

            var renderer = new ToolStripRenderer(ToolbarTheme.Toolbar);
            toolStrip1.Renderer = renderer;
            toolStrip2.Renderer = renderer;

            platesListView.SelectedIndexChanged += (sender, e) =>
            {
                if (updatingPlateList || platesListView.SelectedIndices.Count == 0)
                    return;

                PlateManager.LoadAt(platesListView.SelectedIndices[0]);
            };
        }

        private void CreatePlateHeader()
        {
            plateHeaderPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.FromArgb(240, 240, 240),
                Padding = new Padding(4, 0, 4, 0),
            };

            plateInfoLabel = new Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Dock = DockStyle.Left,
                Padding = new Padding(4, 4, 4, 4),
            };

            var btnSize = new System.Drawing.Size(28, 28);

            btnFirstPlate = CreateNavButton(Resources.move_first);
            btnFirstPlate.Click += (s, e) => PlateManager.LoadFirst();

            btnPreviousPlate = CreateNavButton(Resources.move_previous);
            btnPreviousPlate.Click += (s, e) => PlateManager.LoadPrevious();

            btnNextPlate = CreateNavButton(Resources.move_next);
            btnNextPlate.Click += (s, e) => PlateManager.LoadNext();

            btnLastPlate = CreateNavButton(Resources.move_last);
            btnLastPlate.Click += (s, e) => PlateManager.LoadLast();

            // Panel that holds the nav buttons and centers itself in the header
            var navPanel = new Panel
            {
                Width = btnSize.Width * 4,
                Height = btnSize.Height,
                Anchor = AnchorStyles.None,
            };

            btnFirstPlate.Location = new Point(0, 0);
            btnPreviousPlate.Location = new Point(btnSize.Width, 0);
            btnNextPlate.Location = new Point(btnSize.Width * 2, 0);
            btnLastPlate.Location = new Point(btnSize.Width * 3, 0);

            navPanel.Controls.AddRange(
                new Control[] { btnFirstPlate, btnPreviousPlate, btnNextPlate, btnLastPlate }
            );

            plateHeaderPanel.Controls.Add(navPanel);
            plateHeaderPanel.Controls.Add(plateInfoLabel);

            // Center the nav panel on resize
            CenterNavPanel(navPanel);
            plateHeaderPanel.Resize += (s, e) => CenterNavPanel(navPanel);
        }

        private void CenterNavPanel(Panel navPanel)
        {
            navPanel.Left = (plateHeaderPanel.Width - navPanel.Width) / 2;
            navPanel.Top = (plateHeaderPanel.Height - navPanel.Height) / 2;
        }

        private void CreateSidePanel()
        {
            sidePanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.White,
            };

            // The header sits outside the scrolling content so Close stays visible.
            sidePanelTitle = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            };

            var closeButton = new Button
            {
                Name = "sidePanelCloseButton",
                Text = "\u00D7",
                AccessibleName = "Close panel",
                Dock = DockStyle.Right,
                Width = 30,
                Font = new Font("Segoe UI", 12f),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Cursor = Cursors.Hand,
            };
            closeButton.Click += (s, e) => CloseSidePanel();
            new ToolTip(components).SetToolTip(closeButton, "Close (Esc)");

            var sidePanelHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.FromArgb(240, 240, 240),
                Padding = new Padding(8, 0, 0, 0),
            };
            sidePanelHeader.Controls.Add(sidePanelTitle);
            sidePanelHeader.Controls.Add(closeButton);

            viewSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2,
                Panel2MinSize = 0,
            };

            viewSplitContainer.Panel1.Controls.Add(PlateView);
            viewSplitContainer.Panel2.Controls.Add(sidePanel);
            viewSplitContainer.Panel2.Controls.Add(sidePanelHeader);
            viewSplitContainer.Panel2Collapsed = true;
        }

        public bool IsSidePanelVisible => !viewSplitContainer.Panel2Collapsed;

        public void ShowSidePanel(Control content, string title, int width = 390)
        {
            sidePanel.Controls.Clear();
            content.Dock = DockStyle.Fill;
            sidePanel.Controls.Add(content);
            sidePanelTitle.Text = title;
            viewSplitContainer.SplitterDistance = viewSplitContainer.Width - width;
            viewSplitContainer.Panel2Collapsed = false;
        }

        public void HideSidePanel()
        {
            viewSplitContainer.Panel2Collapsed = true;
            sidePanel.Controls.Clear();
            sidePanelTitle.Text = string.Empty;
        }

        private void CloseSidePanel()
        {
            // Side panels belong to the plate action that opened them; ending the
            // action hides its panel. Hide directly as well so Close always closes.
            PlateView.EndAction();
            if (IsSidePanelVisible)
                HideSidePanel();
            PlateView.Focus();
        }

        private static Button CreateNavButton(System.Drawing.Image image)
        {
            return new Button
            {
                Image = image,
                Size = new System.Drawing.Size(28, 28),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Cursor = Cursors.Hand,
            };
        }

        public EditNestForm(Nest nest)
            : this()
        {
            updateDrawingListTimer = new Timer()
            {
                AutoReset = false,
                Enabled = true,
                Interval = 50,
            };
            updateDrawingListTimer.Elapsed += drawingListUpdateTimer_Elapsed;

            Document = new Document { Nest = nest };
            // Units are read when each check starts, matching the manual command.
            PlateView.SetOverlapAutoCheck(() => Nest.Units);

            PlateManager = new PlateManager(nest);
            PlateManager.CurrentPlateChanged += PlateManager_CurrentPlateChanged;
            PlateManager.PlateListChanged += PlateManager_PlateListChanged;

            PlateManager.EnsureSentinel();

            UpdatePlateList();
            UpdateDrawingList();
            UpdateRemovePlateButton();

            PlateManager.LoadFirst();

            Text = Nest.Name;
            drawingListBox1.Units = Nest.Units;
            drawingListBox1.DeleteRequested += drawingListBox1_DeleteRequested;
        }

        public void UpdatePlateList()
        {
            updatingPlateList = true;
            var focused = ContainsFocus ? GetFocusedControl() : null;

            platesListView.BeginUpdate();
            platesListView.Items.Clear();

            var items = new ListViewItem[Nest.Plates.Count];

            for (int i = 0; i < items.Length; ++i)
            {
                var plate = Nest.Plates[i];
                var item = GetListViewItem(plate, i + 1);
                items[i] = item;
            }

            platesListView.Items.AddRange(items);

            if (PlateManager.CurrentIndex < platesListView.Items.Count)
                platesListView.Items[PlateManager.CurrentIndex].Selected = true;

            platesListView.EndUpdate();
            updatingPlateList = false;

            if (focused != null && focused != platesListView)
                focused.Focus();
        }

        private Control GetFocusedControl()
        {
            Control ctrl = this;
            while (ctrl is ContainerControl container && container.ActiveControl != null)
                ctrl = container.ActiveControl;
            return ctrl;
        }

        public void UpdateDrawingList()
        {
            var topIndex = drawingListBox1.TopIndex;
            var selected = drawingListBox1.SelectedItem;

            drawingListBox1.BeginUpdate();

            drawingListBox1.Items.Clear();

            foreach (var dwg in Nest.Drawings.OrderBy(d => d.Name).ToList())
            {
                if (
                    hideNestedButton.Checked
                    && dwg.Quantity.Required > 0
                    && dwg.Quantity.Remaining == 0
                )
                    continue;

                drawingListBox1.Items.Add(dwg);
            }

            if (selected != null && drawingListBox1.Items.Contains(selected))
                drawingListBox1.SelectedItem = selected;

            if (topIndex < drawingListBox1.Items.Count)
                drawingListBox1.TopIndex = topIndex;

            drawingListBox1.EndUpdate();
        }

        public void Save()
        {
            if (Document.HasSavePath)
                SaveAs(Document.LastSavePath);
            else
                SaveAs();
        }

        public void SaveAs()
        {
            var dlg = new SaveFileDialog();
            dlg.Filter = NestFormat.FileFilter;
            dlg.FileName = Nest.Name;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SaveAs(dlg.FileName);
        }

        public void SaveAs(string path)
        {
            Document.SaveAs(path);
            Text = Document.Name;
        }

        public void Import()
        {
            var dlg = new OpenFileDialog();
            dlg.Multiselect = true;
            dlg.Filter =
                "CAD Files (*.dxf;*.dwg)|*.dxf;*.dwg|DXF Files (*.dxf)|*.dxf|DWG Files (*.dwg)|*.dwg";

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            var converter = new CadConverterForm();
            converter.AddFiles(dlg.FileNames);

            var result = converter.ShowDialog();

            if (result != DialogResult.OK)
                return;

            var drawings = converter.GetDrawings();
            drawings.ForEach(d => Nest.Drawings.Add(d));

            UpdateDrawingList();
        }

        public bool Export()
        {
            var suggestedFileName = string.Format("{0}-P{1}", Nest.Name, PlateManager.CurrentIndex + 1);
            var target = ShowPlateExportDialog(suggestedFileName);
            if (target == null)
                return false;

            return TryExportPlate(PlateView.Plate, target.Value.Destination, target.Value.FilterIndex);
        }

        internal virtual (string Destination, int FilterIndex)? ShowPlateExportDialog(string suggestedFileName)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "DXF file (*.dxf)|*.dxf|"
                    + "Image as displayed (*.jpg)|*.jpg|"
                    + "Locations and rotations (*.txt)|*.txt",
                FileName = suggestedFileName,
                AddExtension = true,
                DefaultExt = ".",
            };
            return dlg.ShowDialog(this) == DialogResult.OK ? (dlg.FileName, dlg.FilterIndex) : null;
        }

        internal bool TryExportPlate(Plate plate, string destination, int filterIndex)
        {
            try
            {
                WritePlateExport(plate, destination, filterIndex);
                return true;
            }
            catch (Exception ex)
            {
                ReportPlateExportFailure(destination, ex);
                return false;
            }
        }

        internal virtual void WritePlateExport(Plate plate, string destination, int filterIndex)
        {
            switch (filterIndex)
            {
                case 1:
                    Dxf.ExportPlate(plate, destination);
                    break;
                case 2:
                    using (var img = new Bitmap(PlateView.Width, PlateView.Height))
                    {
                        PlateView.DrawToBitmap(
                            img,
                            new Rectangle(0, 0, PlateView.Width, PlateView.Height)
                        );
                        img.Save(destination, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                    break;
                case 3:
                    using (var writer = new StreamWriter(destination))
                    {
                        foreach (var part in plate.Parts)
                        {
                            var pt = part.BaseDrawing.Source.Offset.Rotate(part.Rotation);

                            writer.WriteLine(
                                "{0}|{1},{2}|{3}",
                                part.BaseDrawing.Source.Path,
                                System.Math.Round(part.Location.X - pt.X, 8),
                                System.Math.Round(part.Location.Y - pt.Y, 8),
                                Angle.ToDegrees(part.Rotation)
                            );
                        }
                    }
                    break;
                default:
                    throw new ArgumentException("Unsupported plate export format.", nameof(filterIndex));
            }
        }

        internal virtual void ReportPlateExportFailure(string destination, Exception error) =>
            MessageBox.Show(this, $"Could not export plate to '{destination}': {error.Message}",
                "Export Plate", MessageBoxButtons.OK, MessageBoxIcon.Error);

        public void ExportAll()
        {
            PlateManager.LoadFirst();

            do
            {
                if (!Export())
                    return;
            } while (PlateManager.LoadNext());
        }

        /// <summary>The report target fixed before the save dialog opens.</summary>
        internal sealed record NestReportTargets(Nest Nest, string SuggestedFileName);

        /// <summary>
        /// Capture the report target before the owned save dialog opens. The exact nest
        /// reference is fixed here; later failures are reported against it, not a newer
        /// active document. Whole-job nesting, open progress windows (via <paramref name="isJobBusy"/>),
        /// interactive fill and busy plate actions on every view sharing this nest are rejected.
        /// </summary>
        internal NestReportTargets CaptureReportTargets(Func<bool> isJobBusy)
        {
            ArgumentNullException.ThrowIfNull(isJobBusy);
            var nest = Nest;
            if (nest == null)
                throw new InvalidOperationException("No nest is available to report.");

            // The nest name becomes the suggested file name; reject unusable names before the dialog.
            if (
                string.IsNullOrWhiteSpace(nest.Name)
                || nest.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            )
                throw new InvalidOperationException(
                    $"The nest name '{nest.Name}' cannot be used as a report file name. Rename the nest first."
                );

            if (isJobBusy() || IsReportTargetBusy(nest))
                throw new InvalidOperationException(
                    $"Finish or cancel the current nesting or plate action before exporting the report for '{nest.Name}'."
                );

            return new NestReportTargets(nest, $"{nest.Name}.report.pdf");
        }

        /// <summary>
        /// Revalidate the fixed target after the dialog closed, then synchronously capture the
        /// snapshot on the UI thread and write it. The dialog pumps messages, so an async fill
        /// could have committed or started meanwhile; once capture begins nothing yields.
        /// </summary>
        internal void WriteNestReport(
            NestReportTargets targets,
            string destination,
            Func<bool> isJobBusy
        )
        {
            ArgumentNullException.ThrowIfNull(targets);
            ArgumentException.ThrowIfNullOrWhiteSpace(destination);
            ArgumentNullException.ThrowIfNull(isJobBusy);

            if (!ReferenceEquals(targets.Nest, Nest))
                throw new InvalidOperationException(
                    "The active document changed while saving. No report was written."
                );
            if (isJobBusy() || IsReportTargetBusy(targets.Nest))
                throw new InvalidOperationException(
                    "A nesting or plate operation started while saving. No report was written."
                );

            // Reporting must have no accounting side effects: no quantity refresh, no
            // selection or dirty-state change. Only the detached snapshot reaches the writer.
            var snapshot = NestReportBuilder.Capture(targets.Nest, DateTimeOffset.Now);
            NestPdfWriter.Write(snapshot, destination);
        }

        private bool IsReportTargetBusy(Nest nest) =>
            Application
                .OpenForms.OfType<EditNestForm>()
                .Where(form => ReferenceEquals(form.Nest, nest))
                .Select(form => form.PlateView)
                .Any(view =>
                    !view.IsDisposed
                    && (view.IsFillInProgress || view.Actions.CurrentAction?.IsBusy() == true)
                );

        public void RotateCw()
        {
            PlateView.Plate.Rotate90(RotationType.CW);
            PlateView.ZoomToFit();
        }

        public void RotateCcw()
        {
            PlateView.Plate.Rotate90(RotationType.CCW);
            PlateView.ZoomToFit();
        }

        public void Rotate180()
        {
            PlateView.Plate.Rotate180();
            PlateView.ZoomToFit();
        }

        public void ShowNestInfoEditor()
        {
            var form = new EditNestInfoForm();
            form.LoadNestInfo(Nest);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                form.SaveNestInfo(Nest);
                drawingListBox1.Units = Nest.Units;
                drawingListBox1.Invalidate();
            }
        }

        public void ResizePlateToFitParts()
        {
            var options = new PlateSizeOptions
            {
                SnapIncrement = Settings.Default.AutoSizePlateFactor,
            };
            PlateView.Plate.SnapToStandardSize(options);
            PlateView.ZoomToPlate();
            PlateView.Refresh();
            UpdatePlateList();
            UpdatePlateHeader();
        }

        public void SelectAllParts()
        {
            PlateView.SelectAll();
            PlateView.Invalidate();
        }

        public void ToggleRapid()
        {
            PlateView.DrawRapid = !PlateView.DrawRapid;
            PlateView.Invalidate();
        }

        public void TogglePiercePoints()
        {
            PlateView.DrawPiercePoints = !PlateView.DrawPiercePoints;
            PlateView.Invalidate();
        }

        public void ToggleDrawBounds()
        {
            PlateView.DrawBounds = !PlateView.DrawBounds;
            PlateView.Invalidate();
        }

        public void ToggleBendLines()
        {
            PlateView.ShowBendLines = !PlateView.ShowBendLines;
            PlateView.Invalidate();
        }

        public void ToggleDrawOffset()
        {
            PlateView.DrawOffset = !PlateView.DrawOffset;
            PlateView.Invalidate();
        }

        public void ToggleCutDirection()
        {
            PlateView.DrawCutDirection = !PlateView.DrawCutDirection;
            PlateView.Invalidate();
        }

        public void ToggleFillParts()
        {
            PlateView.FillParts = !PlateView.FillParts;
            PlateView.Invalidate();
        }

        public void SetCurrentPlateAsNestDefault()
        {
            Nest.PlateDefaults.SetFromExisting(PlateView.Plate);
        }

        public void EditPlate()
        {
            var form = new EditPlateForm(PlateView.Plate);
            form.Units = Nest.Units;

            if (form.ShowDialog() == DialogResult.OK)
            {
                PlateView.Invalidate();
                FirePlateChanged(false);
                UpdatePlateList();
            }

            Nest.UpdateDrawingQuantities();
            drawingListBox1.Refresh();

            if (PlateView.Plate.Parts.Count == 0)
                Nest.PlateDefaults.SetFromExisting(PlateView.Plate);
        }

        /// <summary>
        /// Opens the current plate as a DXF file by the default program set in Windows.
        /// </summary>
        public void OpenCurrentPlate()
        {
            var plate = PlateView.Plate;
            var name = string.Format("{0}-P{1}.dxf", Nest.Name, PlateManager.CurrentIndex + 1);
            var path = Path.Combine(Path.GetTempPath(), name);
            Dxf.ExportPlate(plate, path);

            Process.Start(path);
        }

        public void RemoveCurrentPlate()
        {
            PlateManager.RemoveCurrent();
        }

        public void AutoSequenceCurrentPlate()
        {
            SequencePlate(PlateView.Plate);
            PlateView.Invalidate();
        }

        public void AutoSequenceAllPlates()
        {
            var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
            PlateSequencing.ApplyAll(Nest.Plates, parameters);
            PlateView.Invalidate();
        }

        private static void SequencePlate(Plate plate)
        {
            var parameters = new SequenceParameters { Method = SequenceMethod.LeastCode };
            PlateSequencing.Apply(plate, parameters);
        }

        public void CalculateCurrentPlateCutTime()
        {
            var cutParamsForm = new CutParametersForm();
            cutParamsForm.Units = Nest.Units;

            if (cutParamsForm.ShowDialog() == DialogResult.OK)
            {
                var cutparams = cutParamsForm.GetCutParameters();
                var info = Timing.GetTimingInfo(PlateView.Plate);
                var time = Timing.CalculateTime(info, cutparams);

                var timingForm = new TimingForm();
                timingForm.Units = Nest.Units;
                timingForm.SetCutDistance(info.CutDistance);
                timingForm.SetCutTime(time);
                timingForm.SetIntersectionCount(info.IntersectionCount);
                timingForm.SetPierceCount(info.PierceCount);
                timingForm.SetRapidDistance(info.TravelDistance);
                timingForm.SetCutParameters(cutparams);

                timingForm.ShowDialog();
            }
        }

        public void CalculateNestCutTime()
        {
            var cutParamsForm = new CutParametersForm();
            cutParamsForm.Units = Nest.Units;

            if (cutParamsForm.ShowDialog() == DialogResult.OK)
            {
                var cutparams = cutParamsForm.GetCutParameters();
                var info = Timing.GetTimingInfo(Nest);
                var time = Timing.CalculateTime(info, cutparams);

                var timingForm = new TimingForm();
                timingForm.Units = Nest.Units;
                timingForm.SetCutDistance(info.CutDistance);
                timingForm.SetCutTime(time);
                timingForm.SetIntersectionCount(info.IntersectionCount);
                timingForm.SetPierceCount(info.PierceCount);
                timingForm.SetRapidDistance(info.TravelDistance);
                timingForm.SetCutParameters(cutparams);

                timingForm.ShowDialog();
            }
        }

        private void FirePlateChanged(bool updateListView = true)
        {
            if (updateListView && PlateManager.CurrentIndex < platesListView.Items.Count)
                platesListView.Items[PlateManager.CurrentIndex].Selected = true;

            UpdatePlateHeader();
            PlateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdatePlateHeader()
        {
            var plate = PlateManager.CurrentPlate;

            if (plate != null)
            {
                plateInfoLabel.Text = string.Format(
                    "Plate {0} of {1}  |  {2}",
                    PlateManager.CurrentIndex + 1,
                    PlateManager.Count,
                    plate.Size
                );
            }
            else
            {
                plateInfoLabel.Text = "No plates";
            }

            btnFirstPlate.Enabled = !PlateManager.IsFirst;
            btnPreviousPlate.Enabled = !PlateManager.IsFirst;
            btnNextPlate.Enabled = !PlateManager.IsLast;
            btnLastPlate.Enabled = !PlateManager.IsLast;
        }

        #region Overrides

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PlateView.Invalidate();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Icon = Icon.Clone() as Icon;
            PlateView.DrawBounds = Settings.Default.PlateViewDrawBounds;
            PlateView.DrawRapid = Settings.Default.PlateViewDrawRapid;
            splitContainer.SplitterDistance = Settings.Default.SplitterDistance;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            PlateManager.Dispose();

            Settings.Default.PlateViewDrawBounds = PlateView.DrawBounds;
            Settings.Default.PlateViewDrawRapid = PlateView.DrawRapid;
            Settings.Default.SplitterDistance = splitContainer.SplitterDistance;
            Settings.Default.Save();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PlateView.Invalidate();
        }

        #endregion

        #region Plates/Drawings Panel Events

        private void AddPlate_Click(object sender, EventArgs e)
        {
            Nest.CreatePlate();
        }

        private void EditSelectedPlate_Click(object sender, EventArgs e)
        {
            if (platesListView.SelectedIndices.Count != 0)
                EditPlate();
        }

        private void RemoveSelectedPlate_Click(object sender, EventArgs e)
        {
            RemoveCurrentPlate();
        }

        private void CalculateSelectedPlateCutTime_Click(object sender, EventArgs e)
        {
            CalculateCurrentPlateCutTime();
        }

        /// <summary>
        /// Opens the cutting planner for the active plate or every plate. The planner applies its
        /// plan itself, all or nothing; on success the confirmed settings become the saved defaults.
        /// </summary>
        public bool PlanCutting(bool allPlates, Func<bool> isOperationBusy, IWin32Window owner)
        {
            if (PlateView?.Plate == null || Nest == null)
                return false;

            // The form plans with its own copy; the plate's live settings stay plate state.
            var parameters = LoadOrDefaultParameters(PlateView.Plate.CuttingParameters);
            using var form = new CuttingPlanForm(PlateView, Nest, allPlates, parameters, isOperationBusy);
            if (form.ShowDialog(owner) != DialogResult.OK)
                return false;

            SaveCuttingParameters(form.ConfirmedParameters);
            UpdatePlateList();
            if (form.CommitResult?.RefreshErrors.Count > 0)
                MessageBox.Show(owner,
                    "The cutting plan was applied, but part of the display did not refresh: "
                        + form.CommitResult.RefreshErrors[0].Message,
                    "Plan Cutting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }

        public void AssignLeadIns_Click(object sender, EventArgs e)
        {
            if (PlateView?.Plate == null)
                return;

            var plate = PlateView.Plate;

            var parameters = LoadOrDefaultParameters(plate.CuttingParameters);

            using var dlg = new CuttingParametersDialog();
            dlg.LoadParameters(parameters);

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            parameters = dlg.GetParameters();
            plate.CuttingParameters = parameters;
            SaveCuttingParameters(parameters);

            var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
            assigner.Assign(plate);

            foreach (var lp in PlateView.Parts)
                lp.IsDirty = true;

            PlateView.Invalidate();
        }

        public void RemoveLeadIns_Click(object sender, EventArgs e)
        {
            if (PlateView?.Plate == null)
                return;

            var plate = PlateView.Plate;
            var count = 0;

            foreach (var part in plate.Parts)
            {
                if (part.HasManualLeadIns)
                {
                    part.RemoveLeadIns();
                    count++;
                }
            }

            if (count == 0)
                return;

            plate.CuttingParameters = null;

            // Rebuild all layout part graphics
            foreach (var lp in PlateView.Parts)
            {
                lp.IsDirty = true;
                lp.Update();
            }

            PlateView.Invalidate();
        }

        public void AssignLeadInsAllPlates()
        {
            if (Nest == null)
                return;

            var parameters = LoadOrDefaultParameters(PlateView?.Plate?.CuttingParameters);

            using var dlg = new CuttingParametersDialog();
            dlg.LoadParameters(parameters);

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            parameters = dlg.GetParameters();
            SaveCuttingParameters(parameters);

            var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };

            foreach (var plate in Nest.Plates)
            {
                plate.CuttingParameters = parameters;
                assigner.Assign(plate);
            }

            PlateView.Invalidate();
        }

        public void RemoveLeadInsAllPlates()
        {
            if (Nest == null)
                return;

            foreach (var plate in Nest.Plates)
            {
                foreach (var part in plate.Parts)
                {
                    if (part.HasManualLeadIns)
                        part.RemoveLeadIns();
                }

                plate.CuttingParameters = null;
            }

            foreach (var lp in PlateView.Parts)
            {
                lp.IsDirty = true;
                lp.Update();
            }

            PlateView.Invalidate();
        }

        public void PlaceLeadIn_Click(object sender, EventArgs e)
        {
            if (PlateView?.Plate == null)
                return;

            var plate = PlateView.Plate;

            if (plate.CuttingParameters == null)
                plate.CuttingParameters = LoadOrDefaultParameters(null);

            PlateView.SetAction(typeof(Actions.ActionLeadIn));
        }

        private static CuttingParameters LoadOrDefaultParameters(CuttingParameters existing)
        {
            if (existing != null)
                return existing;

            var json = Properties.Settings.Default.CuttingParametersJson;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    return CuttingParametersSerializer.Deserialize(json);
                }
                catch
                { /* fall through */
                }
            }

            return new CuttingParameters();
        }

        private static void SaveCuttingParameters(CuttingParameters parameters)
        {
            var json = CuttingParametersSerializer.Serialize(parameters);
            Properties.Settings.Default.CuttingParametersJson = json;
            Properties.Settings.Default.Save();
        }

        private void ImportDrawings_Click(object sender, EventArgs e)
        {
            Import();
        }

        private void ShapeLibrary_Click(object sender, EventArgs e)
        {
            var form = new ShapeLibraryForm(Nest.Drawings.Select(d => d.Name));
            form.ShowDialog();

            var drawings = form.GetDrawings();
            if (drawings.Count == 0)
                return;

            drawings.ForEach(d => Nest.Drawings.Add(d));
            UpdateDrawingList();
        }

        private void EditDrawingsInConverter_Click(object sender, EventArgs e)
        {
            if (Nest.Drawings.Count == 0)
                return;

            // Capture before loading the editor: its conversion can mutate programs in place.
            var snapshot = DrawingProgramSnapshot.Capture(
                Nest.Drawings,
                program => NestWriter.GetProgramText(program) + "\0" + NestWriter.GetSubProgramsText(program)
            );
            var converter = new CadConverterForm();
            // LoadDrawings can edit live programs even when the dialog is canceled.
            PlateView.InvalidateOverlapCheck();
            converter.LoadDrawings(Nest.Drawings);

            if (converter.ShowDialog() != DialogResult.OK)
                return;

            var newDrawings = converter.GetDrawings();
            var newByName = newDrawings.ToDictionary(d => d.Name);

            // Update existing drawings in-place so parts keep their BaseDrawing references
            foreach (var existing in Nest.Drawings.ToList())
            {
                if (newByName.TryGetValue(existing.Name, out var updated))
                {
                    existing.Program = updated.Program;
                    existing.SourceEntities = updated.SourceEntities;
                    existing.SuppressedEntityIds = updated.SuppressedEntityIds;
                    existing.Source = updated.Source;
                    existing.Customer = updated.Customer;
                    existing.Quantity.Required = updated.Quantity.Required;
                    existing.Bends.Clear();
                    existing.Bends.AddRange(updated.Bends);
                    newByName.Remove(existing.Name);
                }
                else
                {
                    Nest.Drawings.Remove(existing);
                }
            }

            // Add any new drawings that weren't in the original set
            foreach (var d in newByName.Values)
                Nest.Drawings.Add(d);

            // Leave unchanged parts' lead-ins, tabs, locks and program instances intact.
            var updatedParts = snapshot.UpdateChangedParts(Nest.Plates).ToHashSet();
            foreach (var layoutPart in PlateView.Parts)
            {
                if (!updatedParts.Contains(layoutPart.BasePart))
                    continue;

                layoutPart.IsDirty = true;
                layoutPart.InvalidateOffset();
            }

            UpdateDrawingList();
            PlateView.Invalidate();
        }

        private void CleanUnusedDrawings_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Remove unused drawings?",
                "Clean Drawings",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1
            );

            if (result == DialogResult.Yes)
            {
                Nest.Drawings.RemoveWhere(d => d.Quantity.Nested == 0);
                UpdateDrawingList();
            }
        }

        private void HideNestedButton_CheckedChanged(object sender, EventArgs e)
        {
            UpdateDrawingList();
        }

        #endregion

        #region PlateManager Events

        private void PlateManager_CurrentPlateChanged(object sender, PlateChangedEventArgs e)
        {
            PlateView.Plate = PlateManager.CurrentPlate;
            PlateView.ZoomToFit();
            UpdatePlateHeader();
            UpdateRemovePlateButton();
            PlateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void PlateManager_PlateListChanged(object sender, EventArgs e)
        {
            UpdatePlateList();
            UpdatePlateHeader();
            UpdateRemovePlateButton();
        }

        private void UpdateRemovePlateButton()
        {
            toolStripLabel2.Enabled = PlateManager.CanRemoveCurrent;
        }

        #endregion

        private static ListViewItem GetListViewItem(Plate plate, int id)
        {
            var item = new ListViewItem();
            item.Text = id.ToString();
            item.SubItems.Add(plate.Size.ToString());
            item.SubItems.Add(plate.Quantity.ToString());

            var partCount = plate.Parts.Count(p => !p.BaseDrawing.IsCutOff);
            item.SubItems.Add(partCount.ToString());

            var util = plate.Utilization();
            item.SubItems.Add(partCount > 0 ? $"{util:P0}" : "");

            return item;
        }

        private void PlateView_PartRemoved(object sender, ItemRemovedEventArgs<Part> e)
        {
            updateDrawingListTimer.Stop();
            updateDrawingListTimer.Start();
        }

        private void PlateView_PartAdded(object sender, ItemAddedEventArgs<Part> e)
        {
            updateDrawingListTimer.Stop();
            updateDrawingListTimer.Start();
        }

        private void PlateView_PartsReordered(object sender, EventArgs e)
        {
            updateDrawingListTimer.Stop();
            updateDrawingListTimer.Start();
        }

        private void drawingListUpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!drawingListBox1.IsHandleCreated)
                return;

            drawingListBox1.Invoke(
                new MethodInvoker(() =>
                {
                    if (hideNestedButton.Checked)
                    {
                        drawingListBox1.BeginUpdate();

                        for (var i = drawingListBox1.Items.Count - 1; i >= 0; i--)
                        {
                            var dwg = (Drawing)drawingListBox1.Items[i];
                            if (dwg.Quantity.Required > 0 && dwg.Quantity.Remaining == 0)
                                drawingListBox1.Items.RemoveAt(i);
                        }

                        foreach (var dwg in Nest.Drawings.OrderBy(d => d.Name))
                        {
                            if (dwg.Quantity.Required > 0 && dwg.Quantity.Remaining == 0)
                                continue;

                            if (!drawingListBox1.Items.Contains(dwg))
                                drawingListBox1.Items.Add(dwg);
                        }

                        drawingListBox1.EndUpdate();
                    }

                    drawingListBox1.Invalidate();

                    // Parts were added or removed: rebuild the plate rows so the
                    // part-count and utilization columns stay current. The debounce
                    // timer collapses a fill's many per-part events into one refresh.
                    UpdatePlateList();
                })
            );
        }

        private void drawingListBox1_DoubleClick(object sender, EventArgs e)
        {
            addPart = false;

            var drawing = drawingListBox1.SelectedItem as Drawing;

            if (drawing == null)
                return;

            var form = new EditDrawingForm();
            form.LoadDrawing(drawing);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                form.SaveDrawing(drawing);

                RefreshDrawingColor(drawing);

                UpdateDrawingList();
                PlateView.Invalidate();
            }
        }

        internal void RefreshDrawingColor(Drawing drawing)
        {
            // Metadata edits only refresh color; never rebuild the placed part program.
            foreach (var layoutPart in PlateView.Parts)
                if (ReferenceEquals(layoutPart.BasePart.BaseDrawing, drawing))
                    layoutPart.Update();
        }

        private void drawingListBox1_DeleteRequested(object sender, Drawing drawing)
        {
            var result = MessageBox.Show(
                $"Delete drawing '{drawing.Name}' and all its parts from every plate?",
                "Delete Drawing",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2
            );

            if (result != DialogResult.Yes)
                return;

            foreach (var plate in Nest.Plates)
            {
                for (var i = plate.Parts.Count - 1; i >= 0; i--)
                {
                    if (plate.Parts[i].BaseDrawing == drawing)
                        plate.Parts.RemoveAt(i);
                }
            }

            Nest.Drawings.Remove(drawing);
            UpdateDrawingList();
            UpdatePlateList();
            PlateView.Invalidate();
        }

        private void drawingListBox1_Click(object sender, EventArgs e)
        {
            addPart = true;
        }

        private void PlateView_MouseEnter(object sender, EventArgs e)
        {
            if (!PlateView.Focused)
                PlateView.Focus();
        }

        private void PlateView_Enter(object sender, EventArgs e)
        {
            if (!addPart)
                return;

            var drawing = drawingListBox1.SelectedItem as Drawing;

            if (drawing == null)
                return;

            PlateView.SetAction(typeof(ActionClone), drawing);

            addPart = false;
        }

        private void toolStripLabel2_Click(object sender, EventArgs e)
        {
            RemoveSelectedPlate_Click(sender, e);
        }

        private void toolStripLabel1_Click(object sender, EventArgs e)
        {
            AddPlate_Click(sender, e);
        }
    }
}
